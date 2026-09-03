<?php
require_once __DIR__ . '/lib.php';
$message = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $email = strtolower(trim((string)($_POST['email'] ?? '')));
    try {
        if (filter_var($email, FILTER_VALIDATE_EMAIL)) {
            $sourceAllowed = air_screen_rate_consume('resend_source', air_screen_source_bucket_key(), 10, 3600);
            if ($sourceAllowed) {
                $emailAllowed = air_screen_rate_consume('resend_email', air_screen_email_bucket_key($email), 3, 3600);
            }
        }
        if (($emailAllowed ?? false) && ($sourceAllowed ?? false)) {
            $database = air_screen_db();
            $database->beginTransaction();
            $select = $database->prepare('SELECT id, display_name FROM air_screen_users WHERE email = :email AND verified_at IS NULL FOR UPDATE');
            $select->execute(['email' => $email]);
            $user = $select->fetch();
            if (is_array($user)) {
                $token = bin2hex(random_bytes(32));
                $database->prepare(
                    'INSERT INTO air_screen_verification_tokens (user_id, token_hash, expires_at) VALUES (:user, :hash, DATE_ADD(CURRENT_TIMESTAMP, INTERVAL 24 HOUR)) '
                    . 'ON DUPLICATE KEY UPDATE token_hash = VALUES(token_hash), expires_at = VALUES(expires_at), created_at = CURRENT_TIMESTAMP')
                    ->execute(['user' => $user['id'], 'hash' => hash('sha256', $token)]);
                $database->commit();
                air_screen_send_verification($email, (string)$user['display_name'], $token);
            } else {
                $database->commit();
            }
        }
    } catch (Throwable $error) {
        if (isset($database) && $database->inTransaction()) $database->rollBack();
        error_log('Custom-screen verification resend failed: ' . $error::class);
    }
    air_screen_maybe_maintain_catalog();
    $message = 'If that address has a pending account, a new verification link has been sent.';
}
$body = ($message !== '' ? '<p>' . air_screen_h($message) . '</p>' : '')
    . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><button>Send verification link</button></form>';
air_screen_layout('Resend verification', $body);
