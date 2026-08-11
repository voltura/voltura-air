<?php
require_once __DIR__ . '/lib.php';

$user = air_screen_require_user();
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$id = (string)($_POST['id'] ?? '');
$remove = ($_POST['action'] ?? '') === 'remove';
if ($id === '') {
    http_response_code(400);
    exit('Choose a screen to rate.');
}
$rating = $remove ? null : filter_var($_POST['rating'] ?? null, FILTER_VALIDATE_INT);
if (!$remove && ($rating === false || $rating < 1 || $rating > 5)) {
    http_response_code(400);
    exit('Choose a rating from 1 to 5.');
}
$exists = air_screen_db()->prepare("SELECT 1 FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$exists->execute(['id' => $id]);
if (!$exists->fetchColumn()) { http_response_code(404); exit('Screen not found.'); }
if ($remove) {
    $stmt = air_screen_db()->prepare('DELETE FROM air_screen_ratings WHERE package_id = :package AND user_id = :user');
    $stmt->execute(['package' => $id, 'user' => $user['id']]);
    air_screen_redirect('view.php?id=' . rawurlencode($id) . '&ratingRemoved=1');
}
$stmt = air_screen_db()->prepare('INSERT INTO air_screen_ratings (package_id, user_id, rating) VALUES (:package, :user, :rating) ON DUPLICATE KEY UPDATE rating = VALUES(rating), updated_at = CURRENT_TIMESTAMP');
$stmt->execute(['package' => $id, 'user' => $user['id'], 'rating' => $rating]);
air_screen_redirect('view.php?id=' . rawurlencode($id) . '&rated=1');
