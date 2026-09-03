<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$id = (string)($_POST['id'] ?? '');
$email = strtolower(trim((string)($_POST['email'] ?? '')));
$reason = trim((string)($_POST['reason'] ?? ''));
if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $id === '' || $reason === '' || strlen($reason) > 1000) { http_response_code(400); exit('Invalid report.'); }
$database = air_screen_db();
$sourceAllowed = air_screen_rate_consume('report_source', air_screen_source_bucket_key(), 20, 3600);
if (!$sourceAllowed) { http_response_code(429); exit('Report limit reached.'); }
try {
    $lockName = air_screen_acquire_advisory_lock($database, 'report', $email);
} catch (Throwable $error) {
    error_log('Custom-screen report lock failed: ' . $error::class);
    http_response_code(503);
    exit('The report service is temporarily busy. Try again.');
}
$rejection = null;
try {
    $packageStatement = $database->prepare("SELECT name FROM air_screen_packages WHERE id = :id AND status = 'approved'");
    $packageStatement->execute(['id' => $id]);
    $screenName = $packageStatement->fetchColumn();
    if (!is_string($screenName)) { $rejection = [404, 'Screen not found.']; }
    $quotaStatement = $database->prepare(
        'SELECT COUNT(*) FROM air_screen_reports WHERE reporter_email = :email AND created_at > DATE_SUB(CURRENT_TIMESTAMP, INTERVAL 1 DAY)');
    $quotaStatement->execute(['email' => $email]);
    if ($rejection === null && (int)$quotaStatement->fetchColumn() >= 5) { $rejection = [429, 'Report limit reached.']; }
    $duplicateStatement = $database->prepare(
        'SELECT COUNT(*) FROM air_screen_reports WHERE package_id = :id AND reporter_email = :email AND created_at > DATE_SUB(CURRENT_TIMESTAMP, INTERVAL 1 DAY)');
    $duplicateStatement->execute(['id' => $id, 'email' => $email]);
    if ($rejection === null && (int)$duplicateStatement->fetchColumn() > 0) { $rejection = [429, 'This screen was already reported recently.']; }
    if ($rejection === null) {
        $serviceAllowed = air_screen_rate_consume(
            'report_service', air_screen_scoped_bucket_key('report-service', 'v1'), 500, 86400);
        if (!$serviceAllowed) {
            $rejection = [429, 'Report limit reached.'];
        } else {
            $stmt = $database->prepare('INSERT INTO air_screen_reports (package_id, reporter_email, reason) VALUES (:id, :email, :reason)');
            $stmt->execute(['id' => $id, 'email' => $email, 'reason' => $reason]);
        }
    }
} finally {
    air_screen_release_advisory_lock($database, $lockName);
}
if ($rejection !== null) { http_response_code($rejection[0]); exit($rejection[1]); }
air_screen_notify_screen_report($id, $screenName, $email, $reason);
air_screen_redirect('view.php?id=' . rawurlencode($id) . '&reported=1');
