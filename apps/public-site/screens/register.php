<?php
require_once __DIR__ . '/lib.php';
$message = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $email = strtolower(trim((string)($_POST['email'] ?? '')));
    $name = trim((string)($_POST['name'] ?? ''));
    $password = (string)($_POST['password'] ?? '');
    $generic = 'If that address can be registered, a verification link has been sent.';
    try {
        if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $name === '' || strlen($name) > 80 || strlen($password) < 12) {
            throw new InvalidArgumentException('invalid');
        }
        $emailKey = air_screen_email_bucket_key($email);
        $sourceKey = air_screen_source_bucket_key();
        $emailAllowed = air_screen_rate_consume('register_email', $emailKey, 3, 86400);
        $sourceAllowed = air_screen_rate_consume('register_source', $sourceKey, 5, 3600);
        if (!$emailAllowed || !$sourceAllowed) {
            throw new RuntimeException('limited');
        }
        $database = air_screen_db();
        $lock = air_screen_acquire_advisory_lock($database, 'register', $email);
        try {
            $database->beginTransaction();
            $select = $database->prepare('SELECT id, verified_at FROM air_screen_users WHERE email = :email FOR UPDATE');
            $select->execute(['email' => $email]);
            $existing = $select->fetch();
            if (is_array($existing) && $existing['verified_at'] !== null) {
                $database->commit();
            } else {
                $hash = password_hash($password, PASSWORD_DEFAULT);
                if (is_array($existing)) {
                    $userId = (int)$existing['id'];
                    $update = $database->prepare('UPDATE air_screen_users SET password_hash = :hash, display_name = :name WHERE id = :id AND verified_at IS NULL');
                    $update->execute(['hash' => $hash, 'name' => $name, 'id' => $userId]);
                } else {
                    $insert = $database->prepare('INSERT INTO air_screen_users (email, password_hash, display_name) VALUES (:email, :hash, :name)');
                    $insert->execute(['email' => $email, 'hash' => $hash, 'name' => $name]);
                    $userId = (int)$database->lastInsertId();
                }
                $token = bin2hex(random_bytes(32));
                $tokenHash = hash('sha256', $token);
                $rotate = $database->prepare(
                    'INSERT INTO air_screen_verification_tokens (user_id, token_hash, expires_at) '
                    . 'VALUES (:user, :hash, DATE_ADD(CURRENT_TIMESTAMP, INTERVAL 24 HOUR)) '
                    . 'ON DUPLICATE KEY UPDATE token_hash = VALUES(token_hash), expires_at = VALUES(expires_at), created_at = CURRENT_TIMESTAMP');
                $rotate->execute(['user' => $userId, 'hash' => $tokenHash]);
                $database->commit();
                air_screen_send_verification($email, $name, $token);
            }
        } finally {
            if ($database->inTransaction()) $database->rollBack();
            air_screen_release_advisory_lock($database, $lock);
        }
    } catch (Throwable $error) {
        if (!in_array($error->getMessage(), ['invalid', 'limited'], true)) {
            error_log('Custom-screen account registration failed: ' . $error::class);
        }
    }
    $message = $generic;
}
$body = ($message !== '' ? '<p>' . air_screen_h($message) . '</p>' : '')
    . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><input name="name" required maxlength="80" placeholder="Display name"><input name="password" type="password" required minlength="12" placeholder="Password"><button>Create account</button></form>';
air_screen_layout('Create catalog account', $body);
