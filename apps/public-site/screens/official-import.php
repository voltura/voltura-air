<?php
require_once __DIR__ . '/lib.php';
$admin = air_screen_require_admin();
air_screen_require_csrf();

const AIR_SCREEN_OFFICIAL_MAX_BUNDLE_BYTES = 128 * 1024 * 1024;

$oldPaths = [];
$db = air_screen_db();
$lockAcquired = false;
try {
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

    foreach ($validated as &$item) {
        $item['hash'] = hash('sha256', $item['json']);
        $item['basename'] = $item['hash'] . '.volturascreen';
        $item['finalPath'] = air_screen_package_path($item['basename']);
        if (is_file($item['finalPath'])) {
            $existingHash = hash_file('sha256', $item['finalPath']);
            if (!is_string($existingHash) || !hash_equals($item['hash'], $existingHash)) {
                throw new RuntimeException('An official package content address is occupied by different bytes.');
            }
            continue;
        }
        $db->beginTransaction();
        air_screen_enqueue_cleanup($db, $item['basename'], $item['hash']);
        $db->commit();
        air_screen_write_content_file($item['finalPath'], $item['json']);
        air_screen_official_import_failure('stage_write');
    }
    unset($item);

    $lockStatement = $db->query("SELECT GET_LOCK('voltura_air_official_import', 30)");
    $lockAcquired = (int)$lockStatement->fetchColumn() === 1;
    if (!$lockAcquired) { throw new RuntimeException('Another official import is still running.'); }

    $existingStatement = $db->query("SELECT id, official_id, storage_basename FROM air_screen_packages WHERE official_source = 'voltura' AND official_id IS NOT NULL");
    $existing = [];
    foreach ($existingStatement->fetchAll() as $row) { $existing[(string)$row['official_id']] = $row; }
    $collision = $db->prepare("SELECT id FROM air_screen_packages WHERE screen_id = :screenId AND (official_source IS NULL OR official_source <> 'voltura') LIMIT 1");
    foreach ($validated as &$item) {
        $collision->execute(['screenId' => $item['officialId']]);
        if ($collision->fetchColumn() !== false) {
            throw new RuntimeException('An official screen identifier collides with a user-owned package.');
        }
        $item['id'] = isset($existing[$item['officialId']]) ? (string)$existing[$item['officialId']]['id'] : air_screen_uuid();
        $existingBasename = isset($existing[$item['officialId']])
            ? (string)$existing[$item['officialId']]['storage_basename']
            : null;
        if (is_file($item['finalPath'])) {
            if (!hash_equals($item['hash'], (string)hash_file('sha256', $item['finalPath'])))
                throw new RuntimeException('An official package content address is occupied by different bytes.');
        } else {
            throw new RuntimeException('A staged official package is missing.');
        }
        if ($existingBasename !== null && $existingBasename !== $item['basename']) {
            $oldPaths[$existingBasename] = substr($existingBasename, 0, 64);
        }
        air_screen_official_import_failure('install_rename');
    }
    unset($item);

    $db->beginTransaction();
    $statement = $db->prepare('INSERT INTO air_screen_packages (id, owner_id, name, description, tags, package_version, screen_json, storage_basename, status, approved_at, screen_id, official_source, official_id, is_official, official_metadata) VALUES (:id, :owner, :name, :description, :tags, 1, :json, :basename, \'approved\', CURRENT_TIMESTAMP, :screenId, \'voltura\', :officialId, TRUE, :metadata) ON DUPLICATE KEY UPDATE owner_id = VALUES(owner_id), name = VALUES(name), description = VALUES(description), tags = VALUES(tags), package_version = 1, screen_json = VALUES(screen_json), storage_basename = VALUES(storage_basename), status = \'approved\', rejection_feedback = NULL, approved_at = CURRENT_TIMESTAMP, screen_id = VALUES(screen_id), official_source = \'voltura\', is_official = TRUE, official_metadata = VALUES(official_metadata)');
    foreach ($validated as $item) {
        $statement->execute(['id' => $item['id'], 'owner' => $admin['id'], 'name' => $item['name'], 'description' => $item['description'], 'tags' => $item['tags'], 'json' => $item['json'], 'basename' => $item['basename'], 'screenId' => $item['officialId'], 'officialId' => $item['officialId'], 'metadata' => $item['metadata']]);
        $db->prepare('DELETE FROM air_screen_cleanup_jobs WHERE storage_basename = :basename AND expected_sha256 = :hash')
            ->execute(['basename' => $item['basename'], 'hash' => substr($item['basename'], 0, 64)]);
        air_screen_official_import_failure('db_upsert');
    }
    $suppliedIds = array_column($validated, 'officialId');
    foreach ($existing as $officialId => $row) {
        if (in_array($officialId, $suppliedIds, true)) continue;
        $db->prepare('DELETE FROM air_screen_reports WHERE package_id = :id')->execute(['id' => $row['id']]);
        $db->prepare("DELETE FROM air_screen_packages WHERE id = :id AND official_source = 'voltura' AND official_id = :officialId")
            ->execute(['id' => $row['id'], 'officialId' => $officialId]);
        $oldPaths[(string)$row['storage_basename']] = substr((string)$row['storage_basename'], 0, 64);
    }
    foreach ($oldPaths as $basename => $hash) air_screen_enqueue_cleanup($db, $basename, $hash);
    air_screen_official_import_failure('db_commit');
    $db->commit();
    air_screen_official_import_failure('db_committed');

    air_screen_drain_cleanup_jobs();
    $db->query("SELECT RELEASE_LOCK('voltura_air_official_import')");
    $lockAcquired = false;
    air_screen_redirect('admin.php?officialImported=' . count($validated));
} catch (Throwable $exception) {
    if ($db->inTransaction()) {
        try {
            air_screen_official_import_failure('db_rollback');
            $db->rollBack();
        } catch (Throwable $rollbackException) {
            error_log('Voltura Air official import rollback failed: ' . $rollbackException->getMessage());
        }
    }
    try { air_screen_drain_cleanup_jobs(); }
    catch (Throwable $cleanupException) { error_log('Voltura Air official import cleanup drain failed: ' . $cleanupException::class); }
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

function air_screen_write_content_file(string $path, string $contents): void
{
    $stream = fopen($path, 'x+b');
    if ($stream === false) throw new RuntimeException('An official package could not be staged.');
    try {
        if (fwrite($stream, $contents) !== strlen($contents) || !fflush($stream) || (function_exists('fsync') && !fsync($stream))) {
            throw new RuntimeException('An official package could not be staged.');
        }
    } finally {
        fclose($stream);
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
