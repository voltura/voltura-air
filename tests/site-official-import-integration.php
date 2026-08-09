<?php
declare(strict_types=1);

$root = dirname(__DIR__);
$configPath = getenv('VOLTURA_AIR_SCREENS_CONFIG');
if (!$configPath || !is_file($configPath)) {
    fwrite(STDERR, "Set VOLTURA_AIR_SCREENS_CONFIG to an isolated MariaDB test catalog configuration.\n");
    exit(2);
}
require $root . '/docs/site/screens/lib.php';
if (!class_exists(ZipArchive::class)) { throw new RuntimeException('PHP ZipArchive is required.'); }

$db = air_screen_db();
$prefix = 'official.integration.';
$storage = air_screen_storage_path();
$beforeFiles = glob($storage . DIRECTORY_SEPARATOR . '*.volturascreen') ?: [];
$createdUserIds = [];
$bundle = tempnam(sys_get_temp_dir(), 'voltura-official-integration-');
$wrapper = tempnam(sys_get_temp_dir(), 'voltura-official-wrapper-');
if ($bundle === false || $wrapper === false) { throw new RuntimeException('Could not create integration test files.'); }

try {
    cleanupRows($db, $prefix);
    $adminId = ensureUser($db, 'admin', $createdUserIds);
    $ratingUserId = ensureUser($db, 'user', $createdUserIds);
    buildBundle($root, $bundle, $prefix);
    file_put_contents($wrapper, <<<'PHP'
<?php
putenv('VOLTURA_AIR_SITE_DEV=1');
putenv('VOLTURA_AIR_OFFICIAL_IMPORT_FAIL=' . ($argv[2] ?? ''));
require $argv[3] . '/docs/site/screens/lib.php';
$_SESSION['air_screen_user'] = ['id' => (int)$argv[4], 'role' => 'admin'];
$_SESSION['air_screen_csrf'] = 'integration-csrf';
$_SERVER['REQUEST_METHOD'] = 'POST';
$_POST = ['smoke_confirmed' => '1', 'csrf' => 'integration-csrf'];
$_FILES = ['bundle' => ['error' => UPLOAD_ERR_OK, 'size' => filesize($argv[1]), 'tmp_name' => $argv[1]]];
require $argv[3] . '/docs/site/screens/official-import.php';
PHP);

    foreach (['stage_write', 'install_rename', 'db_upsert', 'db_commit', 'db_upsert,db_rollback'] as $failure) {
        runImport($wrapper, $bundle, $root, $adminId, $failure);
        assertSame(0, countRows($db, $prefix), "Failure boundary {$failure} changed MariaDB rows.");
        assertSame($beforeFiles, glob($storage . DIRECTORY_SEPARATOR . '*.volturascreen') ?: [], "Failure boundary {$failure} leaked package files.");
    }

    runImport($wrapper, $bundle, $root, $adminId, '');
    assertSame(14, countRows($db, $prefix), 'Successful import did not publish all 14 rows.');
    $row = $db->query("SELECT id FROM air_screen_packages WHERE official_id LIKE 'official.integration.%' ORDER BY official_id LIMIT 1")->fetch();
    if (!$row) { throw new RuntimeException('Imported integration row is missing.'); }
    $packageId = (string)$row['id'];
    $db->prepare('UPDATE air_screen_packages SET downloads = 17 WHERE id = :id')->execute(['id' => $packageId]);
    $db->prepare('INSERT INTO air_screen_ratings (package_id, user_id, rating) VALUES (:package, :user, 4)')->execute(['package' => $packageId, 'user' => $ratingUserId]);
    runImport($wrapper, $bundle, $root, $adminId, '');
    $preserved = $db->prepare('SELECT id, downloads FROM air_screen_packages WHERE id = :id');
    $preserved->execute(['id' => $packageId]);
    $updated = $preserved->fetch();
    assertSame($packageId, (string)($updated['id'] ?? ''), 'Stable official row ID changed.');
    assertSame(17, (int)($updated['downloads'] ?? -1), 'Download counter changed.');
    $rating = $db->prepare('SELECT rating FROM air_screen_ratings WHERE package_id = :package AND user_id = :user');
    $rating->execute(['package' => $packageId, 'user' => $ratingUserId]);
    assertSame(4, (int)$rating->fetchColumn(), 'Rating changed.');
    echo "Official importer MariaDB integration passed.\n";
} finally {
    cleanupRows($db, $prefix);
    foreach (glob($storage . DIRECTORY_SEPARATOR . '*.volturascreen') ?: [] as $file) {
        if (!in_array($file, $beforeFiles, true)) { @unlink($file); }
    }
    foreach ($createdUserIds as $id) { $db->prepare('DELETE FROM air_screen_users WHERE id = :id')->execute(['id' => $id]); }
    @unlink($bundle);
    @unlink($wrapper);
}

function buildBundle(string $root, string $target, string $prefix): void
{
    $source = $root . '/artifacts/custom-screens/official';
    $catalog = json_decode(file_get_contents($source . '/catalog.json'), true, 32, JSON_THROW_ON_ERROR);
    $zip = new ZipArchive();
    if ($zip->open($target, ZipArchive::CREATE | ZipArchive::OVERWRITE) !== true) { throw new RuntimeException('Could not create test bundle.'); }
    foreach ($catalog['screens'] as $index => &$metadata) {
        $package = json_decode(file_get_contents($source . '/' . $metadata['packageFilename']), true, 32, JSON_THROW_ON_ERROR);
        $id = $prefix . $index;
        $filename = "integration-{$index}.volturascreen";
        $metadata['id'] = $id;
        $metadata['packageFilename'] = $filename;
        $package['screen']['id'] = $id;
        $zip->addFromString($filename, json_encode($package, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES));
    }
    unset($metadata);
    $zip->addFromString('catalog.json', json_encode($catalog, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES));
    $zip->close();
}

function runImport(string $wrapper, string $bundle, string $root, int $adminId, string $failure): void
{
    $command = [PHP_BINARY, '-c', php_ini_loaded_file() ?: '', '-d', 'extension=zip', $wrapper, $bundle, $failure, $root, (string)$adminId];
    $environment = array_merge($_ENV, ['VOLTURA_AIR_SCREENS_CONFIG' => (string)getenv('VOLTURA_AIR_SCREENS_CONFIG')]);
    $process = proc_open($command, [1 => ['pipe', 'w'], 2 => ['pipe', 'w']], $pipes, $root, $environment);
    if (!is_resource($process)) { throw new RuntimeException('Could not start importer process.'); }
    $output = stream_get_contents($pipes[1]) . stream_get_contents($pipes[2]);
    fclose($pipes[1]); fclose($pipes[2]);
    $status = proc_close($process);
    if ($status !== 0) { throw new RuntimeException("Importer process failed ({$status}): {$output}"); }
}

function ensureUser(PDO $db, string $role, array &$created): int
{
    $existing = $db->query("SELECT id FROM air_screen_users WHERE role = " . $db->quote($role) . ' ORDER BY id LIMIT 1')->fetchColumn();
    if ($existing) { return (int)$existing; }
    $email = 'official-integration-' . $role . '-' . bin2hex(random_bytes(6)) . '@example.invalid';
    $statement = $db->prepare('INSERT INTO air_screen_users (email, password_hash, display_name, role) VALUES (:email, :password, :name, :role)');
    $statement->execute(['email' => $email, 'password' => password_hash(bin2hex(random_bytes(16)), PASSWORD_DEFAULT), 'name' => 'Official integration', 'role' => $role]);
    $id = (int)$db->lastInsertId();
    $created[] = $id;
    return $id;
}

function countRows(PDO $db, string $prefix): int
{
    $statement = $db->prepare('SELECT COUNT(*) FROM air_screen_packages WHERE official_id LIKE :prefix');
    $statement->execute(['prefix' => $prefix . '%']);
    return (int)$statement->fetchColumn();
}

function cleanupRows(PDO $db, string $prefix): void
{
    $rows = $db->prepare('SELECT id FROM air_screen_packages WHERE official_id LIKE :prefix');
    $rows->execute(['prefix' => $prefix . '%']);
    foreach (array_column($rows->fetchAll(), 'id') as $id) {
        $db->prepare('DELETE FROM air_screen_reports WHERE package_id = :id')->execute(['id' => $id]);
        $db->prepare('DELETE FROM air_screen_ratings WHERE package_id = :id')->execute(['id' => $id]);
        $db->prepare('DELETE FROM air_screen_packages WHERE id = :id')->execute(['id' => $id]);
    }
}

function assertSame(mixed $expected, mixed $actual, string $message): void
{
    if ($expected !== $actual) { throw new RuntimeException($message); }
}
