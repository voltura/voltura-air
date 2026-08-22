<?php
declare(strict_types=1);

function applySchema(PDO $database, string $path): void
{
    $schema = file_get_contents($path);
    if (!is_string($schema)) {
        throw new RuntimeException('Could not read telemetry schema.');
    }
    foreach (preg_split('/;\s*(?:\r?\n|$)/', $schema, -1, PREG_SPLIT_NO_EMPTY) ?: [] as $statement) {
        $database->exec($statement);
    }
}

function randomTelemetryUuid(): string
{
    $bytes = random_bytes(16);
    $bytes[6] = chr((ord($bytes[6]) & 0x0f) | 0x40);
    $bytes[8] = chr((ord($bytes[8]) & 0x3f) | 0x80);
    $hex = bin2hex($bytes);
    return substr($hex, 0, 8) . '-' . substr($hex, 8, 4) . '-' . substr($hex, 12, 4)
        . '-' . substr($hex, 16, 4) . '-' . substr($hex, 20);
}

function makeBatch(
    string $installationId,
    string $batchId,
    int $hostStarts = 1,
    int $trackpad = 0,
    int $dictation = 0): array
{
    return [
        'schemaVersion' => 1,
        'installationId' => $installationId,
        'batchId' => $batchId,
        'hostVersion' => '1.0.5',
        'hostStarts' => $hostStarts,
        'connections' => ['standardLocal' => 0, 'enhancedDirect' => 0, 'relay' => 0],
        'features' => [
            'trackpad' => $trackpad,
            'keyboard' => 0,
            'dictation' => $dictation,
            'mediaControls' => 0,
            'presentation' => 0,
            'customScreens' => 0,
            'files' => 0,
            'screenViewing' => 0,
            'phoneWebcam' => 0,
            'gyroMouse' => 0,
        ],
    ];
}

function fetchDaily(PDO $database, string $hash, string $version): array
{
    $statement = $database->prepare(
        'SELECT HEX(installation_hash) AS installation_hex, host_starts, features_trackpad, features_dictation '
        . 'FROM ' . air_telemetry_table('daily') .
        ' WHERE activity_date = UTC_DATE() AND installation_hash = :hash AND host_version = :version');
    $statement->bindValue('hash', $hash, PDO::PARAM_LOB);
    $statement->bindValue('version', $version, PDO::PARAM_STR);
    $statement->execute();
    $row = $statement->fetch();
    if (!is_array($row)) {
        throw new RuntimeException('Telemetry daily fixture row is missing.');
    }
    return $row;
}

function countRowsForHash(PDO $database, string $table, string $hash): int
{
    $sql = match ($table) {
        'air_telemetry_batches' => 'SELECT COUNT(*) FROM ' . air_telemetry_table('batches') . ' WHERE installation_hash = :hash',
        'air_telemetry_daily' => 'SELECT COUNT(*) FROM ' . air_telemetry_table('daily') . ' WHERE installation_hash = :hash',
        default => throw new InvalidArgumentException('Unexpected telemetry table.'),
    };
    $statement = $database->prepare($sql);
    $statement->bindValue('hash', $hash, PDO::PARAM_LOB);
    $statement->execute();
    return (int)$statement->fetchColumn();
}

function runLockedRetentionCleanup(PDO $database, int $limit): array
{
    $database->beginTransaction();
    try {
        air_telemetry_lock_data_writes($database);
        $counts = air_telemetry_retention_cleanup($database, $limit);
        $database->commit();
        return $counts;
    } catch (Throwable $error) {
        if ($database->inTransaction()) {
            $database->rollBack();
        }
        throw $error;
    }
}

function catalogCounts(PDO $database): array
{
    $tables = ['air_screen_users', 'air_screen_packages', 'air_screen_ratings', 'air_screen_reports'];
    $counts = [];
    foreach ($tables as $table) {
        $counts[$table] = (int)$database->query('SELECT COUNT(*) FROM ' . $table)->fetchColumn();
    }
    return $counts;
}

function assertSame(mixed $expected, mixed $actual, string $message): void
{
    if ($expected !== $actual) {
        throw new RuntimeException($message . ' Expected ' . var_export($expected, true) . ', got ' . var_export($actual, true) . '.');
    }
}

function assertTrue(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}
