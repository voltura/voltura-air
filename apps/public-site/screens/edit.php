<?php
require_once __DIR__ . '/lib.php';
$user = air_screen_require_user();
$id = (string)($_GET['id'] ?? $_POST['id'] ?? '');
$stmt = air_screen_db()->prepare('SELECT id, name, description, tags, status, rejection_feedback FROM air_screen_packages WHERE id = :id AND owner_id = :owner');
$stmt->execute(['id' => $id, 'owner' => $user['id']]);
$item = $stmt->fetch();
if (!$item) { http_response_code(404); exit('Submission not found.'); }
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $name = (string)$item['name'];
    $description = trim((string)($_POST['description'] ?? ''));
    $tags = trim((string)($_POST['tags'] ?? ''));
    if ($name === '' || strlen($name) > 24 || strlen($description) > 1000 || strlen($tags) > 500) {
        $error = 'Metadata exceeds the allowed length.';
    } else {
        $update = air_screen_db()->prepare("UPDATE air_screen_packages SET name = :name, description = :description, tags = :tags, status = CASE WHEN status IN ('approved', 'rejected') THEN 'pending' ELSE status END, rejection_feedback = NULL, approved_at = CASE WHEN status = 'approved' THEN NULL ELSE approved_at END WHERE id = :id AND owner_id = :owner");
        $update->execute(['name' => $name, 'description' => $description, 'tags' => $tags, 'id' => $id, 'owner' => $user['id']]);
        air_screen_redirect('upload.php');
    }
}
$reviewFeedback = in_array($item['status'], ['approved', 'rejected'], true) ? trim((string)$item['rejection_feedback']) : '';
$feedbackNotice = $reviewFeedback !== ''
    ? '<aside class="catalog-reviewer-feedback"><strong>Reviewer feedback</strong><p>' . air_screen_h($reviewFeedback) . '</p></aside>'
    : '';
$body = (!empty($error) ? '<p>' . air_screen_h($error) . '</p>' : '') . '<p>Status: ' . air_screen_h($item['status']) . '</p>' . $feedbackNotice . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input type="hidden" name="id" value="' . air_screen_h($id) . '"><p><strong>Screen name:</strong> ' . air_screen_h($item['name']) . '</p><p class="catalog-help">The screen name is part of the uploaded package and cannot be changed separately.</p><label>Author notes<textarea name="description" maxlength="1000">' . air_screen_h($item['description']) . '</textarea></label><div class="catalog-tag-field"><label for="catalog-edit-tags">Tags</label><span class="catalog-tag-editor" data-tag-editor><span class="catalog-tag-pills" data-tag-pills></span><input id="catalog-edit-tags" data-tag-input maxlength="500" placeholder="media, presentation, productivity"><input type="hidden" name="tags" value="' . air_screen_h($item['tags']) . '" data-tags-value></span></div><button>Save and submit for review</button></form><form method="post" action="withdraw.php"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input type="hidden" name="id" value="' . air_screen_h($id) . '"><button>Withdraw submission</button></form>';
air_screen_layout('Edit submission', $body);
