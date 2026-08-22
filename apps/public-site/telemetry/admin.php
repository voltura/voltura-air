<?php
declare(strict_types=1);

require_once __DIR__ . '/v1/lib.php';

const AIR_TELEMETRY_ADMIN_DELETE_LIMIT = 1000;
const AIR_TELEMETRY_ADMIN_VERSION_LIMIT = 20;

final class AirTelemetryCleanupScopeChanged extends RuntimeException
{
}

final class AirTelemetryCleanupFailedSafely extends RuntimeException
{
}

final class AirTelemetryCleanupOutcomeUnknown extends RuntimeException
{
}

function air_telemetry_admin_database_state(?callable $connect = null): array
{
    try {
        $database = ($connect ?? static fn(): PDO => air_telemetry_db())();
        if (!$database instanceof PDO) {
            throw new RuntimeException('Telemetry database connector returned an invalid value.');
        }
        return ['database' => $database, 'error' => ''];
    } catch (Throwable) {
        return [
            'database' => null,
            'error' => 'Usage statistics are temporarily unavailable.',
        ];
    }
}

function air_telemetry_admin_date(string $value): ?string
{
    $date = DateTimeImmutable::createFromFormat('!Y-m-d', $value, new DateTimeZone('UTC'));
    return $date && $date->format('Y-m-d') === $value ? $value : null;
}

function air_telemetry_admin_timestamp(string $value): ?string
{
    $date = DateTimeImmutable::createFromFormat('!Y-m-d H:i:s', $value, new DateTimeZone('UTC'));
    return $date && $date->format('Y-m-d H:i:s') === $value ? $value : null;
}

function air_telemetry_admin_filters(array $query): array
{
    $today = new DateTimeImmutable('today', new DateTimeZone('UTC'));
    $preset = isset($query['range']) && is_string($query['range']) ? $query['range'] : '30';
    if (!in_array($preset, ['7', '30', '90', '180'], true)) {
        throw new InvalidArgumentException('Select a supported date range.');
    }
    $days = (int)$preset;
    return [
        'range' => $preset,
        'from' => $today->modify('-' . ($days - 1) . ' days')->format('Y-m-d'),
        'to' => $today->format('Y-m-d'),
    ];
}

function air_telemetry_admin_dashboard(PDO $database, array $filters): array
{
    $dailyTable = air_telemetry_table('daily');
    $where = 'activity_date BETWEEN :from AND :to';
    $parameters = ['from' => $filters['from'], 'to' => $filters['to']];

    $summary = air_telemetry_admin_fetch_one(
        $database,
        'SELECT COUNT(DISTINCT installation_hash) AS installations, '
        . 'COALESCE(SUM(connections_standard_local), 0) AS standard_local, '
        . 'COALESCE(SUM(connections_enhanced_direct), 0) AS enhanced_direct, '
        . 'COALESCE(SUM(connections_relay), 0) AS relay '
        . 'FROM ' . $dailyTable . ' WHERE ' . $where,
        $parameters);

    $trend = air_telemetry_admin_fetch_all(
        $database,
        'SELECT activity_date, COUNT(DISTINCT installation_hash) AS installations '
        . 'FROM ' . $dailyTable . ' WHERE ' . $where . ' GROUP BY activity_date ORDER BY activity_date',
        $parameters);
    $versionQuery = 'WITH filtered AS ('
        . 'SELECT activity_date, installation_hash, host_version, last_received_at '
        . 'FROM ' . $dailyTable . ' WHERE ' . $where . '), ranked_versions AS ('
        . 'SELECT installation_hash, host_version, ROW_NUMBER() OVER ('
        . 'PARTITION BY installation_hash ORDER BY last_received_at DESC, activity_date DESC, host_version DESC) AS version_rank '
        . 'FROM filtered) '
        . 'SELECT host_version, COUNT(*) AS installations FROM ranked_versions '
        . 'WHERE version_rank = 1 GROUP BY host_version ORDER BY installations DESC, host_version DESC '
        . 'LIMIT ' . AIR_TELEMETRY_ADMIN_VERSION_LIMIT;
    $versions = air_telemetry_admin_fetch_all(
        $database,
        $versionQuery,
        $parameters);
    $visibleVersionInstallations = array_sum(array_map(
        static fn(array $row): int => (int)$row['installations'],
        $versions));
    $otherInstallations = max(0, (int)$summary['installations'] - $visibleVersionInstallations);
    if ($otherInstallations > 0) {
        $versions[] = ['host_version' => 'Other', 'installations' => $otherInstallations];
    }
    $featureColumns = [
        'trackpad' => 'features_trackpad',
        'keyboard' => 'features_keyboard',
        'dictation' => 'features_dictation',
        'mediaControls' => 'features_media_controls',
        'presentation' => 'features_presentation',
        'customScreens' => 'features_custom_screens',
        'files' => 'features_files',
        'screenViewing' => 'features_screen_viewing',
        'phoneWebcam' => 'features_phone_webcam',
        'gyroMouse' => 'features_gyro_mouse',
    ];
    $featureExpressions = [];
    foreach ($featureColumns as $key => $column) {
        $featureExpressions[] = 'COUNT(DISTINCT CASE WHEN ' . $column . ' > 0 THEN installation_hash END) AS ' . $key . '_installations';
        $featureExpressions[] = 'COALESCE(SUM(' . $column . '), 0) AS ' . $key . '_sessions';
    }
    $featureRow = air_telemetry_admin_fetch_one(
        $database,
        'SELECT ' . implode(', ', $featureExpressions) . ' FROM ' . $dailyTable . ' WHERE ' . $where,
        $parameters);
    $features = [];
    foreach (array_keys($featureColumns) as $key) {
        $features[$key] = [
            'installations' => (int)$featureRow[$key . '_installations'],
            'sessions' => (int)$featureRow[$key . '_sessions'],
        ];
    }

    return [
        'summary' => $summary,
        'trend' => $trend,
        'versions' => $versions,
        'features' => $features,
    ];
}

function air_telemetry_admin_fetch_one(PDO $database, string $sql, array $parameters): array
{
    $statement = $database->prepare($sql);
    $statement->execute($parameters);
    $row = $statement->fetch();
    return is_array($row) ? $row : [];
}

function air_telemetry_admin_fetch_all(PDO $database, string $sql, array $parameters): array
{
    $statement = $database->prepare($sql);
    $statement->execute($parameters);
    return $statement->fetchAll();
}

function air_telemetry_admin_cleanup_request(array $input): array
{
    $action = isset($input['cleanup_action']) && is_string($input['cleanup_action'])
        ? $input['cleanup_action'] : '';
    if (!in_array($action, ['retention', 'before', 'all'], true)) {
        throw new InvalidArgumentException('Select a supported cleanup action.');
    }
    $cutoff = null;
    $aggregateCutoff = null;
    $shortCutoff = null;
    if ($action === 'before') {
        $value = isset($input['cleanup_cutoff']) && is_string($input['cleanup_cutoff'])
            ? $input['cleanup_cutoff'] : '';
        $cutoff = air_telemetry_admin_date($value);
        if ($cutoff === null || $cutoff > gmdate('Y-m-d')) {
            throw new InvalidArgumentException('Choose a valid UTC cleanup cutoff.');
        }
    } elseif ($action === 'retention') {
        $now = new DateTimeImmutable('now', new DateTimeZone('UTC'));
        $aggregateValue = isset($input['cleanup_aggregate_cutoff']) && is_string($input['cleanup_aggregate_cutoff'])
            ? $input['cleanup_aggregate_cutoff'] : $now->modify('-180 days')->format('Y-m-d');
        $shortValue = isset($input['cleanup_short_cutoff']) && is_string($input['cleanup_short_cutoff'])
            ? $input['cleanup_short_cutoff'] : $now->modify('-1 day')->format('Y-m-d H:i:s');
        $aggregateCutoff = air_telemetry_admin_date($aggregateValue);
        $shortCutoff = air_telemetry_admin_timestamp($shortValue);
        if ($aggregateCutoff === null || $aggregateCutoff > $now->format('Y-m-d') ||
            $shortCutoff === null || $shortCutoff > $now->format('Y-m-d H:i:s')) {
            throw new InvalidArgumentException('Preview the current normal-retention cutoff before deletion.');
        }
    }
    return [
        'action' => $action,
        'cutoff' => $cutoff,
        'aggregate_cutoff' => $aggregateCutoff,
        'short_cutoff' => $shortCutoff,
    ];
}

function air_telemetry_admin_cleanup_plan(array $request): array
{
    return match ($request['action']) {
        'retention' => [
            ['table' => 'air_telemetry_daily', 'where' => 'activity_date < :cutoff', 'parameters' => ['cutoff' => $request['aggregate_cutoff']]],
            ['table' => 'air_telemetry_ingest_daily', 'where' => 'activity_date < :cutoff', 'parameters' => ['cutoff' => $request['aggregate_cutoff']]],
            ['table' => 'air_telemetry_batches', 'where' => 'received_at < :cutoff', 'parameters' => ['cutoff' => $request['short_cutoff']]],
            ['table' => 'air_telemetry_rate_buckets', 'where' => 'window_start < :cutoff', 'parameters' => ['cutoff' => $request['short_cutoff']]],
        ],
        'before' => [
            ['table' => 'air_telemetry_daily', 'where' => 'activity_date < :cutoff', 'parameters' => ['cutoff' => $request['cutoff']]],
            ['table' => 'air_telemetry_ingest_daily', 'where' => 'activity_date < :cutoff', 'parameters' => ['cutoff' => $request['cutoff']]],
            ['table' => 'air_telemetry_batches', 'where' => 'received_at < :cutoff', 'parameters' => ['cutoff' => $request['cutoff'] . ' 00:00:00']],
            ['table' => 'air_telemetry_rate_buckets', 'where' => 'window_start < :cutoff', 'parameters' => ['cutoff' => $request['cutoff'] . ' 00:00:00']],
        ],
        'all' => [
            ['table' => 'air_telemetry_daily', 'where' => '1 = 1', 'parameters' => []],
            ['table' => 'air_telemetry_batches', 'where' => '1 = 1', 'parameters' => []],
            ['table' => 'air_telemetry_rate_buckets', 'where' => '1 = 1', 'parameters' => []],
            ['table' => 'air_telemetry_ingest_daily', 'where' => '1 = 1', 'parameters' => []],
        ],
    };
}

function air_telemetry_admin_cleanup_preview(PDO $database, array $request): array
{
    return air_telemetry_admin_cleanup_counts($database, $request);
}

function air_telemetry_admin_create_cleanup_authorization(array $request, array $counts): array
{
    return [
        'request' => $request,
        'counts' => $counts,
        'token' => bin2hex(random_bytes(16)),
    ];
}

function air_telemetry_admin_consume_cleanup_authorization(
    array &$session,
    string $sessionKey,
    array $request,
    string $submittedToken): array
{
    $stored = $session[$sessionKey] ?? null;
    unset($session[$sessionKey]);
    if (!is_array($stored) || ($stored['request'] ?? null) !== $request ||
        !is_array($stored['counts'] ?? null) || !is_string($stored['token'] ?? null) ||
        $submittedToken === '' || !hash_equals($stored['token'], $submittedToken)) {
        throw new InvalidArgumentException('Preview this exact cleanup scope before deletion.');
    }

    return $stored['counts'];
}

function air_telemetry_admin_cleanup_counts(PDO $database, array $request): array
{
    $counts = [];
    foreach (air_telemetry_admin_cleanup_plan($request) as $entry) {
        $statement = $database->prepare(
            'SELECT COUNT(*) FROM ' . air_telemetry_admin_physical_table($entry['table']) .
            ' WHERE ' . $entry['where']);
        $statement->execute($entry['parameters']);
        $counts[$entry['table']] = (int)$statement->fetchColumn();
    }
    return $counts;
}

function air_telemetry_admin_cleanup_chunk(PDO $database, array $request, ?array $expected = null): array
{
    $deleted = [
        'air_telemetry_daily' => 0,
        'air_telemetry_batches' => 0,
        'air_telemetry_rate_buckets' => 0,
        'air_telemetry_ingest_daily' => 0,
    ];
    $transactionStarted = false;
    $commitAttempted = false;
    try {
        $database->beginTransaction();
        $transactionStarted = true;
        air_telemetry_lock_data_writes($database);
        $current = air_telemetry_admin_cleanup_counts($database, $request);
        if ($expected !== null && !air_telemetry_admin_cleanup_scope_matches($current, $expected)) {
            throw new AirTelemetryCleanupScopeChanged('Telemetry cleanup scope changed after preview.');
        }
        air_telemetry_test_pause('admin_cleanup_after_scope_check');
        foreach (air_telemetry_admin_cleanup_plan($request) as $entry) {
            if (($current[$entry['table']] ?? 0) === 0) {
                continue;
            }
            $statement = $database->prepare(
                'DELETE FROM ' . air_telemetry_admin_physical_table($entry['table']) .
                ' WHERE ' . $entry['where'] . ' LIMIT ' . AIR_TELEMETRY_ADMIN_DELETE_LIMIT);
            $statement->execute($entry['parameters']);
            $deleted[$entry['table']] = $statement->rowCount();
            air_telemetry_test_failure('admin_cleanup_after_delete');
            break;
        }
        $remaining = air_telemetry_admin_cleanup_preview($database, $request);
        if ($request['action'] === 'all' && array_sum($remaining) === 0) {
            $database->exec(
                'INSERT INTO ' . air_telemetry_table('maintenance') .
                ' (singleton_id, next_cleanup_at) VALUES (1, UTC_TIMESTAMP(6)) '
                . 'ON DUPLICATE KEY UPDATE next_cleanup_at = VALUES(next_cleanup_at)');
        }
        air_telemetry_test_failure('admin_cleanup_before_commit');
        $commitAttempted = true;
        $committed = $database->commit();
        air_telemetry_test_failure('admin_cleanup_commit');
        if (!$committed) {
            throw new RuntimeException('Telemetry cleanup commit was not acknowledged.');
        }
        return ['deleted' => $deleted, 'remaining' => $remaining];
    } catch (Throwable $error) {
        if (!$transactionStarted) {
            throw new AirTelemetryCleanupFailedSafely(
                'Telemetry cleanup did not start a transaction.', 0, $error);
        }
        if ($commitAttempted || !$database->inTransaction()) {
            throw new AirTelemetryCleanupOutcomeUnknown(
                'Telemetry cleanup transaction outcome is unknown.', 0, $error);
        }
        try {
            $rolledBack = $database->rollBack();
            air_telemetry_test_failure('admin_cleanup_rollback');
        } catch (Throwable $rollbackError) {
            throw new AirTelemetryCleanupOutcomeUnknown(
                'Telemetry cleanup rollback could not be confirmed.', 0, $rollbackError);
        }
        if (!$rolledBack || $database->inTransaction()) {
            throw new AirTelemetryCleanupOutcomeUnknown(
                'Telemetry cleanup rollback could not be confirmed.', 0, $error);
        }
        if ($error instanceof AirTelemetryCleanupScopeChanged) {
            throw $error;
        }
        throw new AirTelemetryCleanupFailedSafely(
            'Telemetry cleanup failed before commit and was rolled back.', 0, $error);
    }
}

function air_telemetry_admin_physical_table(string $logicalTable): string
{
    return match ($logicalTable) {
        'air_telemetry_daily' => air_telemetry_table('daily'),
        'air_telemetry_batches' => air_telemetry_table('batches'),
        'air_telemetry_rate_buckets' => air_telemetry_table('rates'),
        'air_telemetry_ingest_daily' => air_telemetry_table('ingest'),
        default => throw new InvalidArgumentException('Invalid telemetry cleanup table.'),
    };
}

function air_telemetry_admin_cleanup_scope_matches(array $current, array $expected): bool
{
    ksort($current, SORT_STRING);
    ksort($expected, SORT_STRING);
    return $current === $expected;
}
