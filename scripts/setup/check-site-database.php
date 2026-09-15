<?php
// Read-only local readiness probe. Never emit credentials or database exception text.
try {
    $config = require $argv[1];
    $port = (int)($argv[2] ?? 3306);
    if ($config['dsn'] !== "mysql:host=127.0.0.1;port=$port;dbname=voltura_air_dev;charset=utf8mb4") {
        throw new RuntimeException('Unexpected local database target.');
    }
    $pdo = new PDO($config['dsn'], $config['username'], $config['password'], [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    foreach (['air_screen_users', 'air_screen_packages', 'air_screen_cleanup_jobs', 'air_screen_maintenance', 'air_telemetry_daily', 'air_telemetry_batches', 'air_telemetry_maintenance'] as $table) {
        $pdo->query("SELECT 1 FROM `$table` LIMIT 0");
    }
    echo "Local database connection and required tables are available.\n";
} catch (Throwable $error) {
    fwrite(STDERR, "Local database configuration, authentication, or schema is incomplete.\n");
    exit(1);
}
