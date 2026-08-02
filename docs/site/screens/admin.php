<?php
require_once __DIR__ . '/lib.php';
air_screen_require_admin();
$moderationError = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $status = (string)($_POST['status'] ?? '');
    if (!in_array($status, ['approved', 'rejected', 'hidden', 'removed'], true)) { http_response_code(400); exit('Invalid moderation state.'); }
    $feedback = trim((string)($_POST['rejection_feedback'] ?? ''));
    if ($status === 'rejected' && $feedback === '') {
        http_response_code(400);
        $moderationError = 'Give the author a reason before rejecting the screen.';
    } elseif (strlen($feedback) > 1000) {
        http_response_code(400);
        $moderationError = 'Rejection feedback must be 1000 characters or fewer.';
    } else {
        $packageId = (string)($_POST['id'] ?? '');
        $submissionStatement = air_screen_db()->prepare('SELECT p.name, u.email FROM air_screen_packages p JOIN air_screen_users u ON u.id = p.owner_id WHERE p.id = :id');
        $submissionStatement->execute(['id' => $packageId]);
        $submission = $submissionStatement->fetch();
        $stmt = air_screen_db()->prepare('UPDATE air_screen_packages SET status = :status, rejection_feedback = :feedback, approved_at = CASE WHEN :status = \'approved\' THEN CURRENT_TIMESTAMP ELSE approved_at END WHERE id = :id');
        $stmt->execute([
            'status' => $status,
            'feedback' => in_array($status, ['approved', 'rejected'], true) && $feedback !== '' ? $feedback : null,
            'id' => $packageId
        ]);
        if ($stmt->rowCount() === 1 && $submission && in_array($status, ['approved', 'rejected'], true)) {
            air_screen_notify_submitter_status(
                $packageId,
                (string)$submission['name'],
                (string)$submission['email'],
                $status,
                $feedback);
        }
    }
}
$items = air_screen_db()->query("SELECT p.id, p.name, p.description, p.status, p.created_at, p.screen_json, u.display_name AS author FROM air_screen_packages p JOIN air_screen_users u ON u.id = p.owner_id WHERE p.status = 'pending' ORDER BY p.created_at ASC")->fetchAll();
$body = ($moderationError !== '' ? '<p class="catalog-moderation-error" role="alert">' . air_screen_h($moderationError) . '</p>' : '') . '<p class="catalog-lede">Review screens before publishing.</p><section class="catalog-moderation">';
foreach ($items as $item) {
    $date = date('F j, Y', strtotime((string)$item['created_at']));
    $body .= '<article><div class="catalog-moderation-grid">' . air_screen_preview((string)$item['screen_json'], (string)$item['name'], false, (string)$item['id']) . '<div><h2>' . air_screen_h($item['name']) . '</h2><p class="catalog-byline">By ' . air_screen_h($item['author']) . ' &middot; submitted ' . air_screen_h($date) . '</p><h3>Author notes</h3><p>' . air_screen_h($item['description']) . '</p></div></div><form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input type="hidden" name="id" value="' . air_screen_h($item['id']) . '"><label class="catalog-rejection-feedback">Feedback to the author <small>Optional for approval &middot; Required for rejection &middot; Emailed to author</small><textarea name="rejection_feedback" maxlength="1000" placeholder="Share praise, suggestions, or what needs to change"></textarea></label><button name="status" value="approved">Approve</button><button name="status" value="rejected">Reject</button></form></article>';
}
if (!$items) { $body .= '<p class="catalog-empty">No screens are waiting for review.</p>'; }
$body .= '</section>';
air_screen_layout('Moderation', $body);
