<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$id = (string)($_POST['id'] ?? '');
$email = trim((string)($_POST['email'] ?? ''));
$reason = trim((string)($_POST['reason'] ?? ''));
if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $id === '' || $reason === '' || strlen($reason) > 1000) { http_response_code(400); exit('Invalid report.'); }
$stmt = air_screen_db()->prepare('INSERT INTO air_screen_reports (package_id, reporter_email, reason) VALUES (:id, :email, :reason)');
$stmt->execute(['id' => $id, 'email' => $email, 'reason' => $reason]);
air_screen_redirect('view.php?id=' . rawurlencode($id));
