<?php
require_once __DIR__ . '/lib.php';
$user = air_screen_require_user();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$stmt = air_screen_db()->prepare("UPDATE air_screen_packages SET status = 'removed', removed_at = CURRENT_TIMESTAMP WHERE id = :id AND owner_id = :owner");
$stmt->execute(['id' => (string)($_POST['id'] ?? ''), 'owner' => $user['id']]);
air_screen_redirect('upload.php');
