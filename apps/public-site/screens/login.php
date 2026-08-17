<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $email = strtolower(trim((string)($_POST['email'] ?? '')));
    $password = (string)($_POST['password'] ?? '');
    $emailKey = air_screen_email_bucket_key($email);
    $sourceKey = air_screen_source_bucket_key();
    $blocked = air_screen_rate_is_blocked('login_email', $emailKey) ||
        air_screen_rate_is_blocked('login_source', $sourceKey);
    $user = false;
    if (!$blocked) {
        $stmt = air_screen_db()->prepare(
            'SELECT id, email, display_name, role, password_hash, verified_at FROM air_screen_users WHERE email = :email');
        $stmt->execute(['email' => $email]);
        $user = $stmt->fetch();
    }
    if (is_array($user) && $user['verified_at'] !== null && password_verify($password, (string)$user['password_hash'])) {
        if (password_needs_rehash((string)$user['password_hash'], PASSWORD_DEFAULT)) {
            try {
                air_screen_db()->prepare('UPDATE air_screen_users SET password_hash = :hash WHERE id = :id')
                    ->execute(['hash' => password_hash($password, PASSWORD_DEFAULT), 'id' => $user['id']]);
            } catch (Throwable $error) {
                error_log('Custom-screen password rehash failed: ' . $error::class);
            }
        }
        air_screen_rate_clear('login_email', $emailKey);
        session_regenerate_id(true);
        unset($_SESSION['air_screen_csrf'], $user['password_hash'], $user['verified_at']);
        $_SESSION['air_screen_user'] = $user;
        air_screen_redirect('upload.php');
    }
    if (!$blocked) {
        air_screen_rate_consume('login_email', $emailKey, 4, 900, 1800);
        air_screen_rate_consume('login_source', $sourceKey, 19, 900, 1800);
    }
    $error = 'The email or password was not accepted.';
}
$body = (!empty($error) ? '<p>' . air_screen_h($error) . '</p>' : '')
    . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><input name="password" type="password" required placeholder="Password"><button>Sign in</button></form>';
air_screen_layout('Sign in', $body);
