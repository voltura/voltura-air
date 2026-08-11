<?php
require_once __DIR__ . '/lib.php';
$admin = air_screen_require_admin();
air_screen_require_csrf();

const AIR_SCREEN_OFFICIAL_MAX_BUNDLE_BYTES = 128 * 1024 * 1024;

$stagedPaths = [];
$installedPaths = [];
$oldPaths = [];
$db = air_screen_db();
$lockAcquired = false;
$commitAttempted = false;
$rollbackConfirmed = false;
try {
    $lockStatement = $db->query("SELECT GET_LOCK('voltura_air_official_import', 30)");
    $lockAcquired = (int)$lockStatement->fetchColumn() === 1;
    if (!$lockAcquired) { throw new RuntimeException('Another official import is still running.'); }
    if (($_POST['smoke_confirmed'] ?? '') !== '1') {
        throw new InvalidArgumentException('Confirm the current Windows 11 smoke-test matrix before publishing official screens.');
    }
    if ($_SERVER['REQUEST_METHOD'] !== 'POST' ||
        !isset($_FILES['bundle']) ||
        $_FILES['bundle']['error'] !== UPLOAD_ERR_OK ||
        $_FILES['bundle']['size'] < 1 ||
        $_FILES['bundle']['size'] > AIR_SCREEN_OFFICIAL_MAX_BUNDLE_BYTES) {
        throw new InvalidArgumentException('Choose a valid official-screen ZIP bundle under 128 MB.');
    }

    $zip = new ZipArchive();
    if ($zip->open($_FILES['bundle']['tmp_name'], ZipArchive::RDONLY) !== true) {
        throw new InvalidArgumentException('The official-screen bundle is not a readable ZIP file.');
    }
    try {
        $entries = [];
        $totalBytes = 0;
        for ($index = 0; $index < $zip->numFiles; $index++) {
            $stat = $zip->statIndex($index, ZipArchive::FL_UNCHANGED);
            $name = is_array($stat) ? (string)($stat['name'] ?? '') : '';
            $size = is_array($stat) ? (int)($stat['size'] ?? -1) : -1;
            if ($name === '' || basename($name) !== $name || isset($entries[$name]) || $size < 0 || $size > AIR_SCREEN_MAX_BYTES) {
                throw new InvalidArgumentException('The bundle contains an invalid, duplicate, nested, or oversized entry.');
            }
            $totalBytes += $size;
            if ($totalBytes > AIR_SCREEN_OFFICIAL_MAX_BUNDLE_BYTES) {
                throw new InvalidArgumentException('The expanded official-screen bundle is too large.');
            }
            $contents = $zip->getFromIndex($index);
            if (!is_string($contents) || strlen($contents) !== $size) {
                throw new InvalidArgumentException('A bundle entry could not be read completely.');
            }
            $entries[$name] = $contents;
        }
    } finally {
        $zip->close();
    }

    if (!isset($entries['catalog.json'])) {
        throw new InvalidArgumentException('The bundle does not contain catalog.json.');
    }
    $catalog = json_decode($entries['catalog.json'], true, 32, JSON_THROW_ON_ERROR);
    if (!is_array($catalog) || ($catalog['catalogVersion'] ?? null) !== 1 || !is_array($catalog['screens'] ?? null) || count($catalog['screens']) < 10 || count($catalog['screens']) > 128) {
        throw new InvalidArgumentException('The official catalog manifest is invalid.');
    }
    air_screen_require_exact_keys($catalog, ['catalogVersion', 'screens'], ['catalogVersion', 'screens']);

    $validated = [];
    $officialIds = [];
    $filenames = [];
    foreach ($catalog['screens'] as $metadata) {
        if (!is_array($metadata)) { throw new InvalidArgumentException('An official catalog entry is invalid.'); }
        $metadataKeys = ['id', 'name', 'shortDescription', 'longDescription', 'tags', 'category', 'minimumVolturaAirVersion', 'requiredCapabilities', 'optionalTargetApplication', 'official', 'packageFilename'];
        air_screen_require_exact_keys($metadata, $metadataKeys, $metadataKeys);
        $officialId = (string)($metadata['id'] ?? '');
        $name = trim((string)($metadata['name'] ?? ''));
        $shortDescription = trim((string)($metadata['shortDescription'] ?? ''));
        $longDescription = trim((string)($metadata['longDescription'] ?? ''));
        $category = trim((string)($metadata['category'] ?? ''));
        $filename = (string)($metadata['packageFilename'] ?? '');
        $minimumVersion = (string)($metadata['minimumVolturaAirVersion'] ?? '');
        $tags = $metadata['tags'] ?? null;
        $capabilities = $metadata['requiredCapabilities'] ?? null;
        $targetApplication = $metadata['optionalTargetApplication'] ?? null;
        if (($metadata['official'] ?? null) !== true ||
            !preg_match('/^official\.[A-Za-z0-9._-]{1,55}$/D', $officialId) ||
            isset($officialIds[$officialId]) || isset($filenames[$filename]) ||
            basename($filename) !== $filename || !str_ends_with($filename, '.volturascreen') ||
            !isset($entries[$filename]) ||
            $name === '' || strlen($name) > 24 ||
            $shortDescription === '' || strlen($shortDescription) > 500 ||
            $longDescription === '' || strlen($longDescription) > 1000 ||
            $category === '' || strlen($category) > 80 ||
            !preg_match('/^\d+\.\d+\.\d+$/D', $minimumVersion) ||
            !air_screen_official_string_list($tags, 20, 80) ||
            !air_screen_official_string_list($capabilities, 20, 80) ||
            array_diff($capabilities, ['customScreens', 'hostActions', 'remoteAppLaunch', 'remoteInput', 'urlOpen', 'volumeControl']) !== [] ||
            ($targetApplication !== null && (!is_string($targetApplication) || !preg_match('/^[A-Za-z0-9._-]{1,64}$/D', $targetApplication)))) {
            throw new InvalidArgumentException('An official catalog entry has invalid metadata.');
        }
        $tagsText = implode(', ', array_map(static fn($tag): string => trim((string)$tag), $tags));
        if (strlen($tagsText) > 500) { throw new InvalidArgumentException('Official screen tags are too long.'); }
        $package = air_screen_validate_package($entries[$filename]);
        $screen = $package['screen'];
        if ((string)$screen['id'] !== $officialId || trim((string)$screen['name']) !== $name) {
            throw new InvalidArgumentException('Official manifest identity does not match its package.');
        }
        $officialIds[$officialId] = true;
        $filenames[$filename] = true;
        $validated[] = [
            'officialId' => $officialId,
            'name' => $name,
            'description' => $longDescription,
            'tags' => $tagsText,
            'metadata' => json_encode($metadata, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES),
            'json' => json_encode($package, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES),
        ];
    }
    if (count($entries) !== count($validated) + 1) {
        throw new InvalidArgumentException('The bundle contains files not declared by catalog.json.');
    }

    $existingStatement = $db->query("SELECT id, official_id, storage_path FROM air_screen_packages WHERE is_official = TRUE AND official_id IS NOT NULL");
    $existing = [];
    foreach ($existingStatement->fetchAll() as $row) { $existing[(string)$row['official_id']] = $row; }
    $storage = air_screen_storage_path();
    $storageRoot = realpath($storage);
    if ($storageRoot === false) { throw new RuntimeException('The official package storage is unavailable.'); }
    foreach ($validated as &$item) {
        $item['id'] = isset($existing[$item['officialId']]) ? (string)$existing[$item['officialId']]['id'] : air_screen_uuid();
        $hash = substr(hash('sha256', $item['json']), 0, 16);
        $finalPath = $storage . DIRECTORY_SEPARATOR . $item['id'] . '.official.' . $hash . '.volturascreen';
        $existingPath = isset($existing[$item['officialId']])
            ? (string)$existing[$item['officialId']]['storage_path']
            : null;
        if ($existingPath === $finalPath && is_file($finalPath)) {
            $resolvedFinalPath = realpath($finalPath);
            if ($resolvedFinalPath === false || dirname($resolvedFinalPath) !== $storageRoot) {
                throw new RuntimeException('An existing official package path is invalid.');
            }
            if (!hash_equals(hash('sha256', $item['json']), hash_file('sha256', $finalPath))) {
                throw new RuntimeException('An existing official package file failed its content check.');
            }
            $item['path'] = $finalPath;
            continue;
        }
        if (is_file($finalPath)) {
            throw new RuntimeException('An official package file already exists outside the current catalog record.');
        }
        $stagedPath = $finalPath . '.' . bin2hex(random_bytes(8)) . '.tmp';
        if (file_put_contents($stagedPath, $item['json'], LOCK_EX) === false) { throw new RuntimeException('An official package could not be staged.'); }
        $stagedPaths[] = $stagedPath;
        air_screen_official_import_failure('stage_write');
        if (!rename($stagedPath, $finalPath)) { throw new RuntimeException('An official package could not be installed.'); }
        $stagedPaths = array_values(array_diff($stagedPaths, [$stagedPath]));
        $installedPaths[] = $finalPath;
        $item['path'] = $finalPath;
        if ($existingPath !== null && $existingPath !== $finalPath) {
            $resolvedOldPath = realpath($existingPath);
            $expectedName = '/^' . preg_quote($item['id'], '/') . '\.official\.[a-f0-9]{16}\.volturascreen$/D';
            if ($resolvedOldPath === false || dirname($resolvedOldPath) !== $storageRoot || !preg_match($expectedName, basename($resolvedOldPath))) {
                throw new RuntimeException('An existing official package path is invalid.');
            }
            $oldPaths[] = $resolvedOldPath;
        }
        air_screen_official_import_failure('install_rename');
    }
    unset($item);

    $db->beginTransaction();
    $statement = $db->prepare('INSERT INTO air_screen_packages (id, owner_id, name, description, tags, package_version, screen_json, storage_path, status, approved_at, official_id, is_official, official_metadata) VALUES (:id, :owner, :name, :description, :tags, 1, :json, :path, \'approved\', CURRENT_TIMESTAMP, :officialId, TRUE, :metadata) ON DUPLICATE KEY UPDATE owner_id = VALUES(owner_id), name = VALUES(name), description = VALUES(description), tags = VALUES(tags), package_version = 1, screen_json = VALUES(screen_json), storage_path = VALUES(storage_path), status = \'approved\', rejection_feedback = NULL, approved_at = CURRENT_TIMESTAMP, is_official = TRUE, official_metadata = VALUES(official_metadata)');
    foreach ($validated as $item) {
        $statement->execute(['id' => $item['id'], 'owner' => $admin['id'], 'name' => $item['name'], 'description' => $item['description'], 'tags' => $item['tags'], 'json' => $item['json'], 'path' => $item['path'], 'officialId' => $item['officialId'], 'metadata' => $item['metadata']]);
        air_screen_official_import_failure('db_upsert');
    }
    air_screen_official_import_failure('db_commit');
    $commitAttempted = true;
    $db->commit();

    foreach (array_unique($oldPaths) as $oldPath) {
        if (is_file($oldPath) && !unlink($oldPath)) { error_log('Voltura Air official import left an obsolete package file: ' . $oldPath); }
    }
    $db->query("SELECT RELEASE_LOCK('voltura_air_official_import')");
    $lockAcquired = false;
    air_screen_redirect('admin.php?officialImported=' . count($validated));
} catch (Throwable $exception) {
    if ($db->inTransaction()) {
        try {
            air_screen_official_import_failure('db_rollback');
            $db->rollBack();
            $rollbackConfirmed = true;
        } catch (Throwable $rollbackException) {
            error_log('Voltura Air official import rollback failed: ' . $rollbackException->getMessage());
        }
    }
    $cleanupPaths = $commitAttempted && !$rollbackConfirmed ? $stagedPaths : array_merge($stagedPaths, $installedPaths);
    foreach ($cleanupPaths as $path) {
        if (is_file($path) && !@unlink($path)) { error_log('Voltura Air official import cleanup failed: ' . $path); }
    }
    if ($lockAcquired) {
        try { $db->query("SELECT RELEASE_LOCK('voltura_air_official_import')"); $lockAcquired = false; }
        catch (Throwable $lockException) { error_log('Voltura Air official import lock release failed: ' . $lockException->getMessage()); }
    }
    http_response_code(400);
    air_screen_layout('Official import failed', '<p class="catalog-moderation-error" role="alert">' . air_screen_h($exception->getMessage()) . '</p><p><a href="admin.php">Return to moderation</a></p>');
} finally {
    if ($lockAcquired) {
        try { $db->query("SELECT RELEASE_LOCK('voltura_air_official_import')"); }
        catch (Throwable $lockException) { error_log('Voltura Air official import lock release failed: ' . $lockException->getMessage()); }
    }
}

function air_screen_official_import_failure(string $boundary): void
{
    $failures = array_filter(array_map('trim', explode(',', (string)getenv('VOLTURA_AIR_OFFICIAL_IMPORT_FAIL'))));
    if (getenv('VOLTURA_AIR_SITE_DEV') && in_array($boundary, $failures, true)) {
        throw new RuntimeException('Injected official import failure at ' . $boundary . '.');
    }
}

function air_screen_official_string_list(mixed $value, int $maximumCount, int $maximumLength): bool
{
    if (!is_array($value) || count($value) === 0 || count($value) > $maximumCount) {
        return false;
    }
    foreach ($value as $item) {
        if (!is_string($item) || trim($item) !== $item || $item === '' || strlen($item) > $maximumLength || preg_match('/[\x00-\x1F\x7F]/', $item)) {
            return false;
        }
    }
    return count(array_unique($value, SORT_STRING)) === count($value);
}
