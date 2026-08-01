<?php
require_once __DIR__ . '/lib.php';

air_screen_require_admin();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();

$id = (string)($_POST['id'] ?? '');
$statement = air_screen_db()->prepare("SELECT storage_path FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$statement->execute(['id' => $id]);
$item = $statement->fetch();
if (!$item) { http_response_code(404); exit('Screen not found.'); }

$storageRoot = realpath(air_screen_storage_path());
$packagePath = realpath((string)$item['storage_path']);
$stagedPath = null;
if ($packagePath !== false) {
    if ($storageRoot === false || dirname($packagePath) !== $storageRoot) {
        throw new RuntimeException('The stored package path is invalid.');
    }
    $stagedPath = $packagePath . '.deleting-' . bin2hex(random_bytes(8));
    if (!rename($packagePath, $stagedPath)) {
        throw new RuntimeException('The stored package could not be prepared for deletion.');
    }
}

$database = air_screen_db();
try {
    $database->beginTransaction();
    $database->prepare('DELETE FROM air_screen_reports WHERE package_id = :id')->execute(['id' => $id]);
    $database->prepare('DELETE FROM air_screen_ratings WHERE package_id = :id')->execute(['id' => $id]);
    $delete = $database->prepare("DELETE FROM air_screen_packages WHERE id = :id AND status = 'approved'");
    $delete->execute(['id' => $id]);
    if ($delete->rowCount() !== 1) {
        throw new RuntimeException('The screen was already changed or removed.');
    }
    $database->commit();
} catch (Throwable $error) {
    if ($database->inTransaction()) {
        $database->rollBack();
    }
    if ($stagedPath !== null && is_file($stagedPath)) {
        @rename($stagedPath, $packagePath);
    }
    throw $error;
}

if ($stagedPath !== null && is_file($stagedPath) && !@unlink($stagedPath)) {
    error_log('Voltura Air could not remove deleted catalog package ' . $id . '.');
}
air_screen_redirect('./?deleted=1');
