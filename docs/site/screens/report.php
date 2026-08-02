<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$id = (string)($_POST['id'] ?? '');
$email = trim((string)($_POST['email'] ?? ''));
$reason = trim((string)($_POST['reason'] ?? ''));
if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $id === '' || $reason === '' || strlen($reason) > 1000) { http_response_code(400); exit('Invalid report.'); }
$database = air_screen_db();
$stmt = $database->prepare('INSERT INTO air_screen_reports (package_id, reporter_email, reason) VALUES (:id, :email, :reason)');
$stmt->execute(['id' => $id, 'email' => $email, 'reason' => $reason]);
$nameStatement = $database->prepare('SELECT name FROM air_screen_packages WHERE id = :id');
$nameStatement->execute(['id' => $id]);
$screenName = $nameStatement->fetchColumn();
air_screen_notify_screen_report($id, is_string($screenName) ? $screenName : 'Custom screen', $email, $reason);
air_screen_redirect('view.php?id=' . rawurlencode($id) . '&reported=1');
