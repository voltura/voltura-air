<?php
require_once __DIR__ . '/lib.php';

$user = air_screen_require_user();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();

$statement = air_screen_db()->prepare("UPDATE air_screen_packages SET status = 'removed', removed_at = CURRENT_TIMESTAMP WHERE owner_id = :owner AND status = 'rejected'");
$statement->execute(['owner' => $user['id']]);
air_screen_redirect('upload.php?rejectedRemoved=' . $statement->rowCount());
