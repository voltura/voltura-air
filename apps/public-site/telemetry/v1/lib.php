<?php
declare(strict_types=1);

const AIR_TELEMETRY_MAX_BODY_BYTES = 4096;
const AIR_TELEMETRY_RESPONSE_MAX_BYTES = 1024;
const AIR_TELEMETRY_INSTALLATION_DAILY_LIMIT = 24;
const AIR_TELEMETRY_SOURCE_HOURLY_LIMIT = 240;
const AIR_TELEMETRY_SERVICE_DAILY_LIMIT = 50000;
const AIR_TELEMETRY_SERVICE_REQUEST_DAILY_LIMIT = 100000;
const AIR_TELEMETRY_COUNTER_MAX = 65535;
const AIR_TELEMETRY_CLEANUP_LIMIT = 500;

function air_telemetry_table(string $owner): string
{
    $testTables = getenv('VOLTURA_AIR_SITE_DEV') === '1' &&
        getenv('VOLTURA_AIR_TELEMETRY_TEST_TABLES') === '1';
    return match ($owner) {
        'daily' => $testTables ? 'air_telemetry_test_daily' : 'air_telemetry_daily',
        'batches' => $testTables ? 'air_telemetry_test_batches' : 'air_telemetry_batches',
        'rates' => $testTables ? 'air_telemetry_test_rate_buckets' : 'air_telemetry_rate_buckets',
        'ingest' => $testTables ? 'air_telemetry_test_ingest_daily' : 'air_telemetry_ingest_daily',
        'maintenance' => $testTables ? 'air_telemetry_test_maintenance' : 'air_telemetry_maintenance',
        default => throw new InvalidArgumentException('Invalid telemetry table owner.'),
    };
}

function air_telemetry_config(): array
{
    $path = getenv('VOLTURA_AIR_SCREENS_CONFIG');
    if (!$path) {
        $path = dirname(__DIR__, 2) . '/config.php';
    }
    if (!is_file($path)) {
        throw new RuntimeException('Telemetry configuration is unavailable.');
    }
    $config = require $path;
    if (!is_array($config)) {
        throw new RuntimeException('Telemetry configuration is unavailable.');
    }
    return $config;
}

function air_telemetry_db(): PDO
{
    static $database;
    if (!$database) {
        $config = air_telemetry_config();
        $database = new PDO(
            (string)($config['dsn'] ?? ''),
            (string)($config['username'] ?? ''),
            (string)($config['password'] ?? ''),
            [
                PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::ATTR_EMULATE_PREPARES => false,
            ]
        );
        $database->exec("SET time_zone = '+00:00'");
    }
    return $database;
}

function air_telemetry_secret(): string
{
    $secret = (string)(air_telemetry_config()['catalog_secret'] ?? getenv('VOLTURA_AIR_CATALOG_SECRET') ?: '');
    if (strlen($secret) < 32) {
        throw new RuntimeException('Telemetry configuration is unavailable.');
    }
    return $secret;
}

function air_telemetry_hmac(string $domain, string $value): string
{
    return hash_hmac('sha256', $domain . $value, air_telemetry_secret(), true);
}

function air_telemetry_fixed_response(int $status, string $body = ''): never
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    if ($status === 405) {
        header('Allow: POST');
    }
    if ($status === 429) {
        header('Retry-After: 900');
    }
    echo $body;
    exit;
}

function air_telemetry_error_response(int $status): never
{
    $body = match ($status) {
        400 => '{"schemaVersion":1,"status":"invalid"}',
        405 => '{"schemaVersion":1,"status":"method-not-allowed"}',
        413 => '{"schemaVersion":1,"status":"body-too-large"}',
        415 => '{"schemaVersion":1,"status":"unsupported-media-type"}',
        429 => '{"schemaVersion":1,"status":"rate-limited"}',
        default => '{"schemaVersion":1,"status":"unavailable"}',
    };
    air_telemetry_fixed_response($status, $body);
}

function air_telemetry_read_body(): string
{
    $contentLength = $_SERVER['CONTENT_LENGTH'] ?? null;
    if (is_string($contentLength) && preg_match('/^[0-9]+$/D', $contentLength) && (int)$contentLength > AIR_TELEMETRY_MAX_BODY_BYTES) {
        air_telemetry_error_response(413);
    }
    $stream = fopen('php://input', 'rb');
    if ($stream === false) {
        air_telemetry_error_response(400);
    }
    $body = stream_get_contents($stream, AIR_TELEMETRY_MAX_BODY_BYTES + 1);
    fclose($stream);
    if (!is_string($body)) {
        air_telemetry_error_response(400);
    }
    if (strlen($body) > AIR_TELEMETRY_MAX_BODY_BYTES) {
        air_telemetry_error_response(413);
    }
    return $body;
}

function air_telemetry_exact_keys(array $value, array $expected): bool
{
    if (array_is_list($value)) {
        return false;
    }
    $actual = array_keys($value);
    sort($actual, SORT_STRING);
    sort($expected, SORT_STRING);
    return $actual === $expected;
}

function air_telemetry_is_uuid(string $value): bool
{
    return preg_match('/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/D', $value) === 1;
}

function air_telemetry_is_semver(string $value): bool
{
    return strlen($value) <= 32 && preg_match(
        '/^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/D',
        $value
    ) === 1;
}

function air_telemetry_is_count(mixed $value, int $maximum = AIR_TELEMETRY_COUNTER_MAX): bool
{
    return is_int($value) && $value >= 0 && $value <= $maximum;
}

function air_telemetry_validate_batch(mixed $decoded): ?array
{
    if (!is_array($decoded) || !air_telemetry_exact_keys($decoded, [
        'schemaVersion', 'installationId', 'batchId', 'hostVersion', 'hostStarts', 'connections', 'features'
    ])) {
        return null;
    }
    if (($decoded['schemaVersion'] ?? null) !== 1 ||
        !is_string($decoded['installationId'] ?? null) || !air_telemetry_is_uuid($decoded['installationId']) ||
        !is_string($decoded['batchId'] ?? null) || !air_telemetry_is_uuid($decoded['batchId']) ||
        !is_string($decoded['hostVersion'] ?? null) || !air_telemetry_is_semver($decoded['hostVersion']) ||
        !air_telemetry_is_count($decoded['hostStarts'] ?? null, 1)) {
        return null;
    }
    $connections = $decoded['connections'] ?? null;
    $features = $decoded['features'] ?? null;
    if (!is_array($connections) || !air_telemetry_exact_keys($connections, ['standardLocal', 'enhancedDirect', 'relay']) ||
        !is_array($features) || !air_telemetry_exact_keys($features, [
            'trackpad', 'keyboard', 'dictation', 'mediaControls', 'presentation', 'customScreens', 'files',
            'screenViewing', 'phoneWebcam', 'gyroMouse'
        ])) {
        return null;
    }
    $total = (int)$decoded['hostStarts'];
    foreach (['standardLocal', 'enhancedDirect', 'relay'] as $key) {
        if (!air_telemetry_is_count($connections[$key] ?? null)) {
            return null;
        }
        $total += (int)$connections[$key];
    }
    foreach (['trackpad', 'keyboard', 'dictation', 'mediaControls', 'presentation', 'customScreens', 'files', 'screenViewing', 'phoneWebcam', 'gyroMouse'] as $key) {
        if (!air_telemetry_is_count($features[$key] ?? null)) {
            return null;
        }
        $total += (int)$features[$key];
    }
    return $total > 0 ? $decoded : null;
}

function air_telemetry_consume_rate(
    PDO $database,
    string $kind,
    string $hash,
    string $windowStart,
    int $limit): bool
{
    $rateTable = air_telemetry_table('rates');
    $statement = $database->prepare(
        'INSERT INTO ' . $rateTable . ' (bucket_kind, bucket_hash, window_start, request_count) '
        . 'VALUES (:kind, :hash, :window, 1) '
        . 'ON DUPLICATE KEY UPDATE request_count = LEAST(65535, request_count + 1)');
    $statement->bindValue('kind', $kind, PDO::PARAM_STR);
    $statement->bindValue('hash', $hash, PDO::PARAM_LOB);
    $statement->bindValue('window', $windowStart, PDO::PARAM_STR);
    $statement->execute();
    $read = $database->prepare(
        'SELECT request_count FROM ' . $rateTable . ' '
        . 'WHERE bucket_kind = :kind AND bucket_hash = :hash AND window_start = :window FOR UPDATE');
    $read->bindValue('kind', $kind, PDO::PARAM_STR);
    $read->bindValue('hash', $hash, PDO::PARAM_LOB);
    $read->bindValue('window', $windowStart, PDO::PARAM_STR);
    $read->execute();
    return (int)$read->fetchColumn() <= $limit;
}

function air_telemetry_ensure_ingest_day(PDO $database, string $date): void
{
    $statement = $database->prepare(
        'INSERT IGNORE INTO ' . air_telemetry_table('ingest') . ' (activity_date) VALUES (:date)');
    $statement->execute(['date' => $date]);
}

function air_telemetry_request_total(PDO $database, string $date): int
{
    $statement = $database->prepare(
        'SELECT accepted + duplicate + invalid + rate_limited + server_failed FROM '
        . air_telemetry_table('ingest') . ' WHERE activity_date = :date FOR UPDATE');
    $statement->execute(['date' => $date]);
    return (int)$statement->fetchColumn();
}

function air_telemetry_database_clock(PDO $database): array
{
    $clock = $database->query(
        "SELECT DATE_FORMAT(UTC_DATE(), '%Y-%m-%d') AS activity_date, "
        . "DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS hour_start")->fetch();
    if (!is_array($clock) ||
        !is_string($clock['activity_date'] ?? null) ||
        !is_string($clock['hour_start'] ?? null)) {
        throw new RuntimeException('Telemetry database clock is unavailable.');
    }
    return ['activityDate' => $clock['activity_date'], 'hourStart' => $clock['hour_start']];
}

function air_telemetry_increment_health(PDO $database, string $date, string $column): void
{
    $table = air_telemetry_table('ingest');
    $sql = match ($column) {
        'accepted' => 'UPDATE ' . $table . ' SET accepted = IF(accepted < 18446744073709551615, accepted + 1, accepted), last_successful_ingest_at = UTC_TIMESTAMP(6) WHERE activity_date = :date',
        'duplicate' => 'UPDATE ' . $table . ' SET duplicate = IF(duplicate < 18446744073709551615, duplicate + 1, duplicate), last_successful_ingest_at = UTC_TIMESTAMP(6) WHERE activity_date = :date',
        'invalid' => 'UPDATE ' . $table . ' SET invalid = IF(invalid < 18446744073709551615, invalid + 1, invalid) WHERE activity_date = :date',
        'rate_limited' => 'UPDATE ' . $table . ' SET rate_limited = IF(rate_limited < 18446744073709551615, rate_limited + 1, rate_limited) WHERE activity_date = :date',
        'server_failed' => 'UPDATE ' . $table . ' SET server_failed = IF(server_failed < 18446744073709551615, server_failed + 1, server_failed) WHERE activity_date = :date',
        default => throw new InvalidArgumentException('Invalid telemetry health counter.'),
    };
    $database->prepare($sql)->execute(['date' => $date]);
}

function air_telemetry_daily_upsert(PDO $database, array $batch, string $installationHash, string $date): void
{
    $connections = $batch['connections'];
    $features = $batch['features'];
    $sql = 'INSERT INTO ' . air_telemetry_table('daily') . ' ('
        . 'activity_date, installation_hash, host_version, host_starts, connections_standard_local, '
        . 'connections_enhanced_direct, connections_relay, features_trackpad, features_keyboard, '
        . 'features_dictation, features_media_controls, features_presentation, features_custom_screens, '
        . 'features_files, features_screen_viewing, features_phone_webcam, features_gyro_mouse, '
        . 'first_received_at, last_received_at) VALUES ('
        . ':date, :installation, :version, :host_starts, :standard_local, :enhanced_direct, :relay, '
        . ':trackpad, :keyboard, :dictation, :media_controls, :presentation, :custom_screens, :files, '
        . ':screen_viewing, :phone_webcam, :gyro_mouse, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)) '
        . 'ON DUPLICATE KEY UPDATE '
        . 'host_starts = LEAST(65535, host_starts + VALUES(host_starts)), '
        . 'connections_standard_local = LEAST(65535, connections_standard_local + VALUES(connections_standard_local)), '
        . 'connections_enhanced_direct = LEAST(65535, connections_enhanced_direct + VALUES(connections_enhanced_direct)), '
        . 'connections_relay = LEAST(65535, connections_relay + VALUES(connections_relay)), '
        . 'features_trackpad = LEAST(65535, features_trackpad + VALUES(features_trackpad)), '
        . 'features_keyboard = LEAST(65535, features_keyboard + VALUES(features_keyboard)), '
        . 'features_dictation = LEAST(65535, features_dictation + VALUES(features_dictation)), '
        . 'features_media_controls = LEAST(65535, features_media_controls + VALUES(features_media_controls)), '
        . 'features_presentation = LEAST(65535, features_presentation + VALUES(features_presentation)), '
        . 'features_custom_screens = LEAST(65535, features_custom_screens + VALUES(features_custom_screens)), '
        . 'features_files = LEAST(65535, features_files + VALUES(features_files)), '
        . 'features_screen_viewing = LEAST(65535, features_screen_viewing + VALUES(features_screen_viewing)), '
        . 'features_phone_webcam = LEAST(65535, features_phone_webcam + VALUES(features_phone_webcam)), '
        . 'features_gyro_mouse = LEAST(65535, features_gyro_mouse + VALUES(features_gyro_mouse)), '
        . 'last_received_at = UTC_TIMESTAMP(6)';
    $statement = $database->prepare($sql);
    $statement->bindValue('date', $date, PDO::PARAM_STR);
    $statement->bindValue('installation', $installationHash, PDO::PARAM_LOB);
    $statement->bindValue('version', $batch['hostVersion'], PDO::PARAM_STR);
    $values = [
        'host_starts' => $batch['hostStarts'],
        'standard_local' => $connections['standardLocal'],
        'enhanced_direct' => $connections['enhancedDirect'],
        'relay' => $connections['relay'],
        'trackpad' => $features['trackpad'],
        'keyboard' => $features['keyboard'],
        'dictation' => $features['dictation'],
        'media_controls' => $features['mediaControls'],
        'presentation' => $features['presentation'],
        'custom_screens' => $features['customScreens'],
        'files' => $features['files'],
        'screen_viewing' => $features['screenViewing'],
        'phone_webcam' => $features['phoneWebcam'],
        'gyro_mouse' => $features['gyroMouse'],
    ];
    foreach ($values as $name => $value) {
        $statement->bindValue($name, (int)$value, PDO::PARAM_INT);
    }
    $statement->execute();
}

function air_telemetry_ingest(PDO $database, array $batch, string $source): string
{
    $installationHash = air_telemetry_hmac('telemetry-install-v1:', $batch['installationId']);
    $installationRateHash = air_telemetry_hmac('telemetry-install-rate-v1:', $batch['installationId']);
    $sourceRateHash = air_telemetry_hmac('telemetry-source-rate-v1:', $source);
    $legacyBatchId = hex2bin(str_replace('-', '', $batch['batchId']));
    if ($legacyBatchId === false || strlen($legacyBatchId) !== 16) {
        throw new InvalidArgumentException('Invalid batch identifier.');
    }
    $batchId = substr(air_telemetry_hmac('telemetry-batch-v1:', strtolower($batch['batchId'])), 0, 16);
    $database->beginTransaction();
    try {
        air_telemetry_lock_data_writes($database);
        $clock = air_telemetry_database_clock($database);
        $date = $clock['activityDate'];
        air_telemetry_ensure_ingest_day($database, $date);
        if (air_telemetry_request_total($database, $date) >= AIR_TELEMETRY_SERVICE_REQUEST_DAILY_LIMIT) {
            air_telemetry_increment_health($database, $date, 'rate_limited');
            $database->commit();
            return 'rate-limited';
        }
        $sourceAllowed = air_telemetry_consume_rate(
            $database,
            'source_hourly',
            $sourceRateHash,
            $clock['hourStart'],
            AIR_TELEMETRY_SOURCE_HOURLY_LIMIT
        );
        if (!$sourceAllowed) {
            air_telemetry_increment_health($database, $date, 'rate_limited');
            $database->commit();
            return 'rate-limited';
        }

        $health = $database->prepare(
            'SELECT accepted FROM ' . air_telemetry_table('ingest') . ' WHERE activity_date = :date FOR UPDATE');
        $health->execute(['date' => $date]);
        $acceptedToday = (int)$health->fetchColumn();
        $duplicate = $database->prepare(
            'SELECT 1 FROM ' . air_telemetry_table('batches')
            . ' WHERE installation_hash = :installation AND batch_id IN (:batch, :legacy_batch)');
        $duplicate->bindValue('installation', $installationHash, PDO::PARAM_LOB);
        $duplicate->bindValue('batch', $batchId, PDO::PARAM_LOB);
        $duplicate->bindValue('legacy_batch', $legacyBatchId, PDO::PARAM_LOB);
        $duplicate->execute();
        if ($duplicate->fetchColumn() !== false) {
            $installationAllowed = air_telemetry_consume_rate(
                $database,
                'installation_daily',
                $installationRateHash,
                $date . ' 00:00:00',
                AIR_TELEMETRY_INSTALLATION_DAILY_LIMIT
            );
            if (!$installationAllowed) {
                air_telemetry_increment_health($database, $date, 'rate_limited');
                $database->commit();
                return 'rate-limited';
            }
            air_telemetry_increment_health($database, $date, 'duplicate');
            $database->commit();
            return 'accepted';
        }
        if ($acceptedToday >= AIR_TELEMETRY_SERVICE_DAILY_LIMIT) {
            air_telemetry_increment_health($database, $date, 'rate_limited');
            $database->commit();
            return 'rate-limited';
        }

        $installationAllowed = air_telemetry_consume_rate(
            $database,
            'installation_daily',
            $installationRateHash,
            $date . ' 00:00:00',
            AIR_TELEMETRY_INSTALLATION_DAILY_LIMIT
        );
        if (!$installationAllowed) {
            air_telemetry_increment_health($database, $date, 'rate_limited');
            $database->commit();
            return 'rate-limited';
        }
        $insertBatch = $database->prepare(
            'INSERT INTO ' . air_telemetry_table('batches') . ' (installation_hash, batch_id, received_at) '
            . 'VALUES (:installation, :batch, UTC_TIMESTAMP(6))');
        $insertBatch->bindValue('installation', $installationHash, PDO::PARAM_LOB);
        $insertBatch->bindValue('batch', $batchId, PDO::PARAM_LOB);
        $insertBatch->execute();
        air_telemetry_test_failure('after_batch_insert');
        air_telemetry_daily_upsert($database, $batch, $installationHash, $date);
        air_telemetry_test_failure('after_daily_upsert');
        air_telemetry_increment_health($database, $date, 'accepted');
        air_telemetry_test_failure('before_commit');
        $database->commit();
        return 'accepted';
    } catch (Throwable $error) {
        air_telemetry_best_effort_rollback($database, 'ingest_rollback');
        throw $error;
    }
}

function air_telemetry_record_invalid(string $source): ?bool
{
    try {
        $database = air_telemetry_db();
        $sourceHash = air_telemetry_hmac('telemetry-source-rate-v1:', $source);
        $database->beginTransaction();
        air_telemetry_lock_data_writes($database);
        $clock = air_telemetry_database_clock($database);
        $date = $clock['activityDate'];
        air_telemetry_ensure_ingest_day($database, $date);
        if (air_telemetry_request_total($database, $date) >= AIR_TELEMETRY_SERVICE_REQUEST_DAILY_LIMIT) {
            air_telemetry_increment_health($database, $date, 'rate_limited');
            $database->commit();
            try {
                air_telemetry_maybe_cleanup($database);
            } catch (Throwable) {
                error_log('Voltura Air telemetry maintenance failed.');
            }
            return false;
        }
        $allowed = air_telemetry_consume_rate(
            $database,
            'source_hourly',
            $sourceHash,
            $clock['hourStart'],
            AIR_TELEMETRY_SOURCE_HOURLY_LIMIT
        );
        air_telemetry_increment_health($database, $date, $allowed ? 'invalid' : 'rate_limited');
        air_telemetry_test_failure('record_invalid_before_commit');
        $database->commit();
        try {
            air_telemetry_maybe_cleanup($database);
        } catch (Throwable) {
            error_log('Voltura Air telemetry maintenance failed.');
        }
        return $allowed;
    } catch (Throwable) {
        air_telemetry_best_effort_rollback(
            isset($database) && $database instanceof PDO ? $database : null,
            'record_invalid_rollback');
        return null;
    }
}

function air_telemetry_record_server_failure(): void
{
    try {
        $database = air_telemetry_db();
        $database->beginTransaction();
        air_telemetry_lock_data_writes($database);
        $date = air_telemetry_database_clock($database)['activityDate'];
        air_telemetry_ensure_ingest_day($database, $date);
        air_telemetry_increment_health($database, $date, 'server_failed');
        air_telemetry_test_failure('record_server_failure_before_commit');
        $database->commit();
    } catch (Throwable) {
        air_telemetry_best_effort_rollback(
            isset($database) && $database instanceof PDO ? $database : null,
            'record_server_failure_rollback');
    }
}

function air_telemetry_best_effort_rollback(?PDO $database, string $failureBoundary): void
{
    if (!($database instanceof PDO) || !$database->inTransaction()) {
        return;
    }
    try {
        $database->rollBack();
        air_telemetry_test_failure($failureBoundary);
    } catch (Throwable) {
        // The endpoint must preserve its fixed generic response even when the
        // database cannot acknowledge best-effort rollback.
    }
}

function air_telemetry_test_failure(string $boundary): void
{
    $configured = getenv('VOLTURA_AIR_TELEMETRY_FAIL');
    if (getenv('VOLTURA_AIR_SITE_DEV') === '1' && is_string($configured) &&
        in_array($boundary, explode(',', $configured), true)) {
        throw new RuntimeException('Injected telemetry integration failure.');
    }
}

function air_telemetry_test_pause(string $boundary): void
{
    $configured = getenv('VOLTURA_AIR_TELEMETRY_PAUSE');
    if (getenv('VOLTURA_AIR_SITE_DEV') !== '1' || $configured !== $boundary) {
        return;
    }
    echo 'TELEMETRY_TEST_PAUSED:' . $boundary . "\n";
    flush();
    $command = fgets(STDIN);
    if (!is_string($command) || trim($command) !== 'continue') {
        throw new RuntimeException('Telemetry integration pause ended without an explicit continuation.');
    }
}

function air_telemetry_maybe_cleanup(PDO $database): array
{
    air_telemetry_ensure_maintenance($database);
    $database->beginTransaction();
    try {
        air_telemetry_lock_data_writes($database);
        $lease = $database->prepare(
            'UPDATE ' . air_telemetry_table('maintenance') . ' '
            . 'SET next_cleanup_at = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) '
            . 'WHERE singleton_id = 1 AND next_cleanup_at <= UTC_TIMESTAMP(6)');
        $lease->execute();
        $counts = $lease->rowCount() === 1
            ? air_telemetry_retention_cleanup($database, AIR_TELEMETRY_CLEANUP_LIMIT)
            : [];
        $database->commit();
        return $counts;
    } catch (Throwable $error) {
        air_telemetry_best_effort_rollback($database, 'automatic_cleanup_rollback');
        throw $error;
    }
}

function air_telemetry_ensure_maintenance(PDO $database): void
{
    $database->exec(
        'INSERT IGNORE INTO ' . air_telemetry_table('maintenance')
        . ' (singleton_id, next_cleanup_at) VALUES (1, UTC_TIMESTAMP(6))');
}

function air_telemetry_lock_data_writes(PDO $database): void
{
    if (!$database->inTransaction()) {
        throw new LogicException('Telemetry data-write locking requires an active transaction.');
    }
    $maintenance = $database->query(
        'SELECT singleton_id FROM ' . air_telemetry_table('maintenance') .
        ' WHERE singleton_id = 1 FOR UPDATE')->fetchColumn();
    if ((int)$maintenance !== 1) {
        throw new RuntimeException('Telemetry maintenance state is unavailable.');
    }
}

function air_telemetry_retention_cleanup(PDO $database, int $limit): array
{
    if (!$database->inTransaction()) {
        throw new LogicException('Telemetry retention cleanup requires the data-write transaction lock.');
    }
    $bounded = max(1, min(AIR_TELEMETRY_CLEANUP_LIMIT, $limit));
    $queries = [
        'air_telemetry_daily' => 'DELETE FROM ' . air_telemetry_table('daily') . ' WHERE activity_date < UTC_DATE() - INTERVAL 180 DAY ORDER BY activity_date LIMIT ' . $bounded,
        'air_telemetry_ingest_daily' => 'DELETE FROM ' . air_telemetry_table('ingest') . ' WHERE activity_date < UTC_DATE() - INTERVAL 180 DAY ORDER BY activity_date LIMIT ' . $bounded,
        'air_telemetry_batches' => 'DELETE FROM ' . air_telemetry_table('batches') . ' WHERE received_at < UTC_TIMESTAMP(6) - INTERVAL 1 DAY ORDER BY received_at LIMIT ' . $bounded,
        'air_telemetry_rate_buckets' => 'DELETE FROM ' . air_telemetry_table('rates') . ' WHERE window_start < UTC_TIMESTAMP() - INTERVAL 1 DAY ORDER BY window_start LIMIT ' . $bounded,
    ];
    $counts = [];
    foreach ($queries as $table => $sql) {
        $counts[$table] = $database->exec($sql);
    }
    return $counts;
}
