<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $email = strtolower(trim((string)($_POST['email'] ?? '')));
    $password = (string)($_POST['password'] ?? '');
    $emailKey = air_screen_email_bucket_key($email);
    $sourceKey = air_screen_source_bucket_key();
    $database = air_screen_db();
    $sourceLock = null;
    $emailLock = null;
    $user = false;
    $authenticated = false;
    try {
        $serviceAllowed = air_screen_rate_consume(
            'login_attempt_service', air_screen_scoped_bucket_key('login-attempt-service', 'v1'), 10000, 86400);
        if ($serviceAllowed) {
            $sourceLock = air_screen_acquire_advisory_lock($database, 'login_source', $sourceKey);
            $emailLock = air_screen_acquire_advisory_lock($database, 'login_email', $emailKey);
            $sourceBlocked = air_screen_rate_is_blocked('login_source', $sourceKey);
            $emailBlocked = air_screen_rate_is_blocked('login_email', $emailKey);
            if (!$sourceBlocked) {
                $stmt = $database->prepare(
                    'SELECT id, email, display_name, role, password_hash, verified_at FROM air_screen_users WHERE email = :email');
                $stmt->execute(['email' => $email]);
                $user = $stmt->fetch();
                $authenticated = is_array($user) && $user['verified_at'] !== null &&
                    password_verify($password, (string)$user['password_hash']);
            }
            if ($authenticated) {
                air_screen_rate_clear('login_email', $emailKey);
            } elseif (!$sourceBlocked) {
                if (!$emailBlocked) {
                    air_screen_rate_consume('login_email', $emailKey, 4, 900, 1800);
                }
                air_screen_rate_consume('login_source', $sourceKey, 19, 900, 1800);
            }
        }
    } catch (Throwable $error) {
        error_log('Custom-screen login rate control failed: ' . $error::class);
        $authenticated = false;
    } finally {
        if ($emailLock !== null) air_screen_release_advisory_lock($database, $emailLock);
        if ($sourceLock !== null) air_screen_release_advisory_lock($database, $sourceLock);
    }
    if ($authenticated && is_array($user)) {
        if (password_needs_rehash((string)$user['password_hash'], PASSWORD_DEFAULT)) {
            try {
                $database->prepare('UPDATE air_screen_users SET password_hash = :hash WHERE id = :id')
                    ->execute(['hash' => password_hash($password, PASSWORD_DEFAULT), 'id' => $user['id']]);
            } catch (Throwable $error) {
                error_log('Custom-screen password rehash failed: ' . $error::class);
            }
        }
        session_regenerate_id(true);
        unset($_SESSION['air_screen_csrf'], $user['password_hash'], $user['verified_at']);
        $_SESSION['air_screen_user'] = $user;
        air_screen_redirect('upload.php');
    }
    $error = 'The email or password was not accepted.';
}
$body = (!empty($error) ? '<p>' . air_screen_h($error) . '</p>' : '')
    . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><input name="password" type="password" required placeholder="Password"><button>Sign in</button></form>';
air_screen_layout('Sign in', $body);
