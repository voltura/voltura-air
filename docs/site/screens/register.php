<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $email = strtolower(trim((string)($_POST['email'] ?? '')));
    $name = trim((string)($_POST['name'] ?? ''));
    $password = (string)($_POST['password'] ?? '');
    if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $name === '' || strlen($name) > 80 || strlen($password) < 12) {
        $error = 'Use a valid email, display name, and password of at least 12 characters.';
    } else {
        try {
            $stmt = air_screen_db()->prepare('INSERT INTO air_screen_users (email, password_hash, display_name) VALUES (:email, :hash, :name)');
            $stmt->execute(['email' => $email, 'hash' => password_hash($password, PASSWORD_DEFAULT), 'name' => $name]);
            air_screen_redirect('login.php');
        } catch (PDOException) { $error = 'That email address is already registered.'; }
    }
}
$error = $error ?? '';
$body = ($error ? '<p>' . air_screen_h($error) . '</p>' : '') . '<form method="post"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Email"><input name="name" required maxlength="80" placeholder="Display name"><input name="password" type="password" required minlength="12" placeholder="Password"><button>Create account</button></form>';
air_screen_layout('Create catalog account', $body);
