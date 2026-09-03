<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] !== 'GET') { http_response_code(405); exit('GET required.'); }
$stmt = air_screen_db()->prepare("SELECT id, name, storage_basename FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$stmt->execute(['id' => (string)($_GET['id'] ?? '')]);
$item = $stmt->fetch();
if (!$item) { http_response_code(404); exit('Screen not found.'); }
$packagePath = air_screen_package_path((string)$item['storage_basename']);
if (!is_file($packagePath)) {
    http_response_code(404);
    exit('Screen not found.');
}
$package = @fopen($packagePath, 'rb');
if ($package === false) { http_response_code(404); exit('Screen not found.'); }
$metadata = fstat($package);
if (!is_array($metadata) || !isset($metadata['size'])) { fclose($package); http_response_code(500); exit('Screen could not be read.'); }
header('Content-Type: application/json');
header('Content-Disposition: attachment; filename="' . preg_replace('/[^A-Za-z0-9._-]/', '_', $item['name']) . '.volturascreen"');
header('Content-Length: ' . $metadata['size']);
$bytesSent = fpassthru($package);
fclose($package);
if ($bytesSent === $metadata['size']) {
    try {
        $source = (string)($_SERVER['REMOTE_ADDR'] ?? 'unknown');
        $serviceAllowed = air_screen_rate_consume(
            'download_service', air_screen_scoped_bucket_key('download-service', 'v1'), 50000, 86400);
        $sourceAllowed = $serviceAllowed && air_screen_rate_consume(
            'download_source', air_screen_scoped_bucket_key('download-source', $source), 100, 86400);
        $downloadAllowed = $sourceAllowed && air_screen_rate_consume(
            'download_package', air_screen_scoped_bucket_key('download-package', (string)$item['id'], $source), 1, 86400);
        if ($downloadAllowed) {
            air_screen_db()->prepare('UPDATE air_screen_packages SET downloads = downloads + 1 WHERE id = :id')
                ->execute(['id' => $item['id']]);
        }
        air_screen_maybe_maintain_catalog();
    } catch (Throwable $error) {
        error_log('Custom-screen download counter update failed: ' . $error::class);
    }
}
