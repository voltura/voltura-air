<?php
require_once __DIR__ . '/lib.php';

air_screen_require_admin();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();

$id = (string)($_POST['id'] ?? '');
if ($id === '') { http_response_code(400); exit('Invalid screen.'); }
$database = air_screen_db();
try {
    $lockName = air_screen_acquire_advisory_lock($database, 'delete', $id);
} catch (Throwable $error) {
    error_log('Custom-screen deletion lock failed: ' . $error::class);
    http_response_code(503);
    exit('The catalog is temporarily busy. Try again.');
}

try {
$statement = $database->prepare("SELECT storage_basename FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$statement->execute(['id' => $id]);
$item = $statement->fetch();
if (!$item) {
    air_screen_release_advisory_lock($database, $lockName);
    $lockName = null;
    http_response_code(404);
    exit('Screen not found.');
}

$basename = air_screen_storage_basename((string)$item['storage_basename']);
$sha256 = substr($basename, 0, 64);

try {
    $database->beginTransaction();
    $database->prepare('DELETE FROM air_screen_reports WHERE package_id = :id')->execute(['id' => $id]);
    $database->prepare('DELETE FROM air_screen_ratings WHERE package_id = :id')->execute(['id' => $id]);
    $delete = $database->prepare("DELETE FROM air_screen_packages WHERE id = :id AND status = 'approved'");
    $delete->execute(['id' => $id]);
    if ($delete->rowCount() !== 1) {
        throw new RuntimeException('The screen was already changed or removed.');
    }
    air_screen_enqueue_cleanup($database, $basename, $sha256);
    $database->commit();
} catch (Throwable $error) {
    if ($database->inTransaction()) {
        $database->rollBack();
    }
    throw $error;
}

air_screen_drain_cleanup_jobs();
air_screen_release_advisory_lock($database, $lockName);
$lockName = null;
air_screen_redirect('./?deleted=1');
} finally {
    if ($lockName !== null) {
        air_screen_release_advisory_lock($database, $lockName);
    }
}
