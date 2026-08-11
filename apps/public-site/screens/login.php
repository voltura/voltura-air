<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $stmt = air_screen_db()->prepare('SELECT id, email, display_name, role, password_hash FROM air_screen_users WHERE email = :email');
    $stmt->execute(['email' => strtolower(trim((string)($_POST['email'] ?? '')))]);
    $user = $stmt->fetch();
    if ($user && password_verify((string)($_POST['password'] ?? ''), $user['password_hash'])) {
        session_regenerate_id(true);
        unset($user['password_hash']);
        $_SESSION['air_screen_user'] = $user;
        air_screen_redirect('upload.php');
    }
    $error = 'The email or password was not accepted.';
}
$body = (!empty($error) ? '<p>' . air_screen_h($error) . '</p>' : '') . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><input name="password" type="password" required placeholder="Password"><button>Sign in</button></form>';
air_screen_layout('Sign in', $body);
