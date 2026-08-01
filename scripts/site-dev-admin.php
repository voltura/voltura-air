<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli' || count($argv) !== 2) {
    fwrite(STDERR, "This helper must be run by site-dev-admin.ps1.\n");
    exit(1);
}

$config = require $argv[1];
$email = strtolower((string)getenv('VOLTURA_AIR_ADMIN_EMAIL'));
$pdo = new PDO(
    (string)$config['dsn'],
    (string)$config['username'],
    (string)$config['password'],
    [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]
);
$exists = $pdo->prepare('SELECT id FROM air_screen_users WHERE email = :email');
$exists->execute(['email' => $email]);
if (!$exists->fetchColumn()) {
    exit(2);
}
$statement = $pdo->prepare("UPDATE air_screen_users SET role = 'admin' WHERE email = :email");
$statement->execute(['email' => $email]);
