<?php
declare(strict_types=1);

$root = dirname(__DIR__);
require $root . '/apps/public-site/telemetry/v1/lib.php';
require $root . '/apps/public-site/telemetry/admin.php';
require __DIR__ . '/site-telemetry-integration-support.php';

$action = $argv[1] ?? '';
if ($action === 'hold' && count($argv) === 2) {
    holdTelemetryTestTables();
    exit(0);
}
if ($action === 'cleanup' && count($argv) === 2) {
    cleanupTelemetryTestTables();
    exit(0);
}
if ($action === 'cleanup-race' && count($argv) === 2) {
    runPausedCleanupRace();
    exit(0);
}
if ($action === 'ingest' && count($argv) === 4) {
    ingestTelemetryFixture($argv[2], $argv[3]);
    exit(0);
}
if ($action === 'verify' && count($argv) === 3) {
    assertTelemetryTestMode();
    $database = air_telemetry_db();
    $installationHash = air_telemetry_hmac('telemetry-install-v1:', $argv[2]);
    assertSame(1, countRowsForHash($database, 'air_telemetry_daily', $installationHash), 'HTTP validation created an unexpected aggregate row count.');
    assertSame(1, countRowsForHash($database, 'air_telemetry_batches', $installationHash), 'HTTP deduplication created an unexpected batch row count.');
    echo "Telemetry HTTP fixtures verified.\n";
    exit(0);
}

fwrite(STDERR, "Usage: site-telemetry-http-fixture.php hold|cleanup|cleanup-race|ingest <installation-id> <batch-id>|verify <installation-id>\n");
exit(2);

function runPausedCleanupRace(): void
{
    assertTelemetryTestMode();
    $database = air_telemetry_db();
    $seed = makeBatch(randomTelemetryUuid(), randomTelemetryUuid());
    assertSame(
        'accepted',
        air_telemetry_ingest($database, $seed, 'telemetry-cleanup-race-seed'),
        'The cleanup-race seed was not accepted.');
    $request = air_telemetry_admin_cleanup_request(['cleanup_action' => 'all']);
    $expected = air_telemetry_admin_cleanup_preview($database, $request);
    air_telemetry_admin_cleanup_chunk($database, $request, $expected);
    echo "TELEMETRY_CLEANUP_RACE_DONE\n";
}

function ingestTelemetryFixture(string $installationId, string $batchId): void
{
    assertTelemetryTestMode();
    $batch = makeBatch($installationId, $batchId);
    assertSame(
        'accepted',
        air_telemetry_ingest(air_telemetry_db(), $batch, 'telemetry-cleanup-race-writer'),
        'The cleanup-race writer was not accepted.');
    echo "TELEMETRY_WRITER_ACCEPTED\n";
}

function cleanupTelemetryTestTables(): void
{
    assertTelemetryTestMode();
    $database = air_telemetry_db();
    $lockName = 'voltura-air-telemetry-integration-v1';
    $lock = $database->prepare('SELECT GET_LOCK(:name, 5)');
    $lock->execute(['name' => $lockName]);
    if ((int)$lock->fetchColumn() !== 1) {
        throw new RuntimeException('The telemetry integration lock remained owned during fallback cleanup.');
    }

    $primaryError = null;
    try {
        dropTelemetryTestTables($database);
    } catch (Throwable $error) {
        $primaryError = $error;
    }
    try {
        $release = $database->prepare('SELECT RELEASE_LOCK(:name)');
        $release->execute(['name' => $lockName]);
        if ((int)$release->fetchColumn() !== 1) {
            throw new RuntimeException('The telemetry integration fallback lock was not released.');
        }
    } catch (Throwable $releaseError) {
        if ($primaryError !== null) {
            throw new RuntimeException(
                'Telemetry fallback cleanup and lock release both failed.',
                0,
                $releaseError);
        }
        throw $releaseError;
    }
    if ($primaryError !== null) {
        throw $primaryError;
    }
    echo "TELEMETRY_TEST_TABLES_REMOVED\n";
}

function holdTelemetryTestTables(): void
{
    assertTelemetryTestMode();
    $database = air_telemetry_db();
    $lockName = 'voltura-air-telemetry-integration-v1';
    $lock = $database->prepare('SELECT GET_LOCK(:name, 0)');
    $lock->execute(['name' => $lockName]);
    if ((int)$lock->fetchColumn() !== 1) {
        throw new RuntimeException('Another telemetry integration suite owns the local test tables.');
    }

    $primaryError = null;
    $cleanupError = null;
    try {
        recreateTelemetryTestTables($database);
        echo "TELEMETRY_TEST_TABLES_READY\n";
        fflush(STDOUT);
        $command = fgets(STDIN);
        if (!is_string($command) || trim($command) !== 'stop') {
            throw new RuntimeException('Telemetry test-table holder stopped without an explicit cleanup request.');
        }
        if (getenv('VOLTURA_AIR_SITE_DEV') === '1' &&
            getenv('VOLTURA_AIR_TELEMETRY_TEST_HANG_ON_STOP') === '1') {
            while (true) {
                usleep(250000);
            }
        }
    } catch (Throwable $error) {
        $primaryError = $error;
    }

    try {
        dropTelemetryTestTables($database);
    } catch (Throwable $error) {
        $cleanupError = $error;
    }
    try {
        $release = $database->prepare('SELECT RELEASE_LOCK(:name)');
        $release->execute(['name' => $lockName]);
        if ((int)$release->fetchColumn() !== 1 && $cleanupError === null) {
            $cleanupError = new RuntimeException('The telemetry integration lock was not released.');
        }
    } catch (Throwable $error) {
        if ($cleanupError === null) {
            $cleanupError = $error;
        }
    }

    if ($primaryError !== null && $cleanupError !== null) {
        throw new RuntimeException(
            'Telemetry integration setup and test-table cleanup both failed.',
            0,
            $cleanupError);
    }
    if ($cleanupError !== null) {
        throw $cleanupError;
    }
    if ($primaryError !== null) {
        throw $primaryError;
    }
    echo "TELEMETRY_TEST_TABLES_REMOVED\n";
}

function recreateTelemetryTestTables(PDO $database): void
{
    dropTelemetryTestTables($database);
    foreach (telemetryTestTablePairs() as $production => $test) {
        $database->exec('CREATE TABLE `' . $test . '` LIKE `' . $production . '`');
    }
    $database->exec(
        'INSERT INTO `' . air_telemetry_table('maintenance') .
        '` (singleton_id, next_cleanup_at) VALUES (1, UTC_TIMESTAMP(6))');
}

function dropTelemetryTestTables(PDO $database): void
{
    foreach (array_reverse(array_values(telemetryTestTablePairs())) as $table) {
        $database->exec('DROP TABLE IF EXISTS `' . $table . '`');
    }
}

function telemetryTestTablePairs(): array
{
    return [
        'air_telemetry_daily' => 'air_telemetry_test_daily',
        'air_telemetry_batches' => 'air_telemetry_test_batches',
        'air_telemetry_rate_buckets' => 'air_telemetry_test_rate_buckets',
        'air_telemetry_ingest_daily' => 'air_telemetry_test_ingest_daily',
        'air_telemetry_maintenance' => 'air_telemetry_test_maintenance',
    ];
}

function assertTelemetryTestMode(): void
{
    if (getenv('VOLTURA_AIR_SITE_DEV') !== '1' ||
        getenv('VOLTURA_AIR_TELEMETRY_TEST_TABLES') !== '1') {
        throw new RuntimeException('Refusing to manage telemetry test tables outside explicit site-dev test mode.');
    }
    $expected = array_values(telemetryTestTablePairs());
    $actual = [
        air_telemetry_table('daily'),
        air_telemetry_table('batches'),
        air_telemetry_table('rates'),
        air_telemetry_table('ingest'),
        air_telemetry_table('maintenance'),
    ];
    if ($actual !== $expected) {
        throw new RuntimeException('The telemetry test-table mapping is not the closed expected set.');
    }
}
