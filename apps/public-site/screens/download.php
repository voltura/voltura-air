<?php
require_once __DIR__ . '/lib.php';
$stmt = air_screen_db()->prepare("SELECT id, name, storage_path FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$stmt->execute(['id' => (string)($_GET['id'] ?? '')]);
$item = $stmt->fetch();
if (!$item || !is_file($item['storage_path'])) { http_response_code(404); exit('Screen not found.'); }
air_screen_db()->prepare('UPDATE air_screen_packages SET downloads = downloads + 1 WHERE id = :id')->execute(['id' => $item['id']]);
header('Content-Type: application/json');
header('Content-Disposition: attachment; filename="' . preg_replace('/[^A-Za-z0-9._-]/', '_', $item['name']) . '.volturascreen"');
header('Content-Length: ' . filesize($item['storage_path']));
readfile($item['storage_path']);
