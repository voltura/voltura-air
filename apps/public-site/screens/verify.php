<?php
require_once __DIR__ . '/lib.php';
$token = (string)($_GET['token'] ?? '');
$verified = false;
if (preg_match('/^[a-f0-9]{64}$/D', $token)) {
    $database = air_screen_db();
    try {
        $database->beginTransaction();
        $select = $database->prepare(
            'SELECT user_id FROM air_screen_verification_tokens WHERE token_hash = :hash AND expires_at > CURRENT_TIMESTAMP FOR UPDATE');
        $select->execute(['hash' => hash('sha256', $token)]);
        $userId = $select->fetchColumn();
        if ($userId !== false) {
            $database->prepare('UPDATE air_screen_users SET verified_at = COALESCE(verified_at, CURRENT_TIMESTAMP) WHERE id = :id')
                ->execute(['id' => $userId]);
            $database->prepare('DELETE FROM air_screen_verification_tokens WHERE user_id = :id')
                ->execute(['id' => $userId]);
            $verified = true;
        }
        $database->commit();
    } catch (Throwable $error) {
        if ($database->inTransaction()) $database->rollBack();
        error_log('Custom-screen verification failed: ' . $error::class);
    }
}
$message = $verified
    ? '<p>Your email is verified. You can now <a href="login.php">sign in</a>.</p>'
    : '<p>The verification link is invalid or expired.</p><p><a href="resend-verification.php">Request a new link</a>.</p>';
air_screen_layout('Verify catalog account', $message);
