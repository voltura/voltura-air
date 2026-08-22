<?php
declare(strict_types=1);

$root = dirname(__DIR__);
$configPath = getenv('VOLTURA_AIR_SCREENS_CONFIG');
if (!$configPath || !is_file($configPath)) {
    fwrite(STDERR, "Set VOLTURA_AIR_SCREENS_CONFIG to the isolated local MariaDB site configuration.\n");
    exit(2);
}

require $root . '/apps/public-site/telemetry/v1/lib.php';
require $root . '/apps/public-site/telemetry/admin.php';
require __DIR__ . '/site-telemetry-integration-support.php';
$schemaDatabase = air_telemetry_db();
applySchema($schemaDatabase, $root . '/apps/public-site/telemetry/schema.sql');
$database = $schemaDatabase;
$dailyTable = air_telemetry_table('daily');
$batchesTable = air_telemetry_table('batches');
$ratesTable = air_telemetry_table('rates');
$ingestTable = air_telemetry_table('ingest');

$unavailableDashboard = air_telemetry_admin_database_state(
    static function (): PDO {
        throw new PDOException('Injected dashboard connection failure.');
    });
assertSame(null, $unavailableDashboard['database'], 'A failed dashboard connection returned an executable database handle.');
assertTrue(
    str_contains($unavailableDashboard['error'], 'temporarily unavailable'),
    'A failed dashboard connection did not return the generic unavailable state.');

$invalidFilterRejected = false;
try {
    air_telemetry_admin_filters(['range' => 'custom', 'from' => '1900-01-01', 'to' => '1900-01-02']);
} catch (InvalidArgumentException) {
    $invalidFilterRejected = true;
}
    assertTrue($invalidFilterRejected, 'A custom range wholly outside retention was accepted.');
    $retentionRequest = air_telemetry_admin_cleanup_request(['cleanup_action' => 'retention']);
    assertTrue(
        air_telemetry_admin_date((string)$retentionRequest['aggregate_cutoff']) !== null &&
        air_telemetry_admin_timestamp((string)$retentionRequest['short_cutoff']) !== null,
        'Normal retention did not freeze exact reviewed UTC cutoffs.');

    $databaseClock = air_telemetry_database_clock($database);
$today = $databaseClock['activityDate'];
$catalogBefore = catalogCounts($schemaDatabase);
$runToken = bin2hex(random_bytes(12));

try {
    air_telemetry_ensure_ingest_day($database, $today);
    $database->prepare(
        'UPDATE ' . $ingestTable .
        ' SET invalid = 18446744073709551615 WHERE activity_date = :date')
        ->execute(['date' => $today]);
    air_telemetry_increment_health($database, $today, 'invalid');
    $saturatedHealth = $database->prepare(
        'SELECT CAST(invalid AS CHAR) FROM ' . $ingestTable . ' WHERE activity_date = :date');
    $saturatedHealth->execute(['date' => $today]);
    assertSame(
        '18446744073709551615',
        (string)$saturatedHealth->fetchColumn(),
        'A delivery-health total wrapped instead of saturating.');
    $database->prepare('UPDATE ' . $ingestTable . ' SET invalid = 0 WHERE activity_date = :date')
        ->execute(['date' => $today]);

    $batch = makeBatch(
        installationId: randomTelemetryUuid(),
        batchId: randomTelemetryUuid(),
        trackpad: 2,
        dictation: 1
    );
    assertTrue(air_telemetry_validate_batch($batch) !== null, 'The valid closed batch was rejected.');
    assertSame(null, air_telemetry_validate_batch($batch + ['unknown' => 1]), 'An unknown batch field was accepted.');
    $invalidContext = $batch;
    $invalidContext['features']['trackpad'] = '2';
    assertSame(null, air_telemetry_validate_batch($invalidContext), 'A non-integer count was accepted.');
    $zero = makeBatch(
        installationId: randomTelemetryUuid(),
        batchId: randomTelemetryUuid(),
        hostStarts: 0
    );
    assertSame(null, air_telemetry_validate_batch($zero), 'An all-zero batch was accepted.');

    $source = 'telemetry-integration-source-' . $runToken;
    assertSame('accepted', air_telemetry_ingest($database, $batch, $source), 'A valid batch was not accepted.');
    $installationHash = air_telemetry_hmac('telemetry-install-v1:', $batch['installationId']);
    $row = fetchDaily($database, $installationHash, '1.0.5');
    assertSame(1, (int)$row['host_starts'], 'Host start was not aggregated.');
    assertSame(2, (int)$row['features_trackpad'], 'Trackpad sessions were not aggregated.');
    assertSame(1, (int)$row['features_dictation'], 'Dictation sessions were not aggregated.');
    assertSame(
        bin2hex(air_telemetry_hmac('telemetry-install-v1:', $batch['installationId'])),
        strtolower((string)$row['installation_hex']),
        'The stored installation pseudonym does not use the required domain-separated HMAC.'
    );

    assertSame('accepted', air_telemetry_ingest($database, $batch, $source), 'A duplicate batch was not idempotently accepted.');
    $duplicate = fetchDaily($database, $installationHash, '1.0.5');
    assertSame(2, (int)$duplicate['features_trackpad'], 'A duplicate batch changed daily counters.');

    $database->prepare(
        'UPDATE ' . $dailyTable .
        ' SET features_trackpad = 65535 WHERE activity_date = :date AND installation_hash = :hash AND host_version = :version')
        ->execute(['date' => $today, 'hash' => $installationHash, 'version' => '1.0.5']);
    $saturationBatch = makeBatch(
        installationId: $batch['installationId'],
        batchId: randomTelemetryUuid(),
        trackpad: 65535
    );
    assertSame('accepted', air_telemetry_ingest($database, $saturationBatch, $source), 'The saturation batch failed.');
    assertSame(65535, (int)fetchDaily($database, $installationHash, '1.0.5')['features_trackpad'], 'A daily counter did not saturate.');

    $failureBatch = makeBatch(
        installationId: randomTelemetryUuid(),
        batchId: randomTelemetryUuid()
    );
    $failureSource = 'telemetry-integration-failure-' . $runToken;
    putenv('VOLTURA_AIR_TELEMETRY_FAIL=after_daily_upsert');
    $failed = false;
    try {
        air_telemetry_ingest($database, $failureBatch, $failureSource);
    } catch (RuntimeException) {
        $failed = true;
    } finally {
        putenv('VOLTURA_AIR_TELEMETRY_FAIL');
    }
    assertTrue($failed, 'The injected transaction failure did not run.');
    $failureHash = air_telemetry_hmac('telemetry-install-v1:', $failureBatch['installationId']);
    assertSame(0, countRowsForHash($database, 'air_telemetry_batches', $failureHash), 'A rolled-back batch row survived.');
    assertSame(0, countRowsForHash($database, 'air_telemetry_daily', $failureHash), 'A rolled-back daily row survived.');

    $limitedInstallation = randomTelemetryUuid();
    $limitedSource = 'telemetry-integration-limit-' . $runToken;
    for ($index = 1; $index <= AIR_TELEMETRY_INSTALLATION_DAILY_LIMIT + 1; $index++) {
        $limited = makeBatch(
            installationId: $limitedInstallation,
            batchId: randomTelemetryUuid()
        );
        $result = air_telemetry_ingest($database, $limited, $limitedSource);
        assertSame(
            $index <= AIR_TELEMETRY_INSTALLATION_DAILY_LIMIT ? 'accepted' : 'rate-limited',
            $result,
            'The installation daily rate boundary was wrong.'
        );
    }

    $capInstallation = randomTelemetryUuid();
    $capSource = 'telemetry-integration-cap-' . $runToken;
    air_telemetry_ensure_ingest_day($database, $today);
    $database->prepare('UPDATE ' . $ingestTable . ' SET accepted = :cap WHERE activity_date = :date')
        ->execute(['cap' => AIR_TELEMETRY_SERVICE_DAILY_LIMIT, 'date' => $today]);
    $capBatch = makeBatch(
        installationId: $capInstallation,
        batchId: randomTelemetryUuid()
    );
    assertSame('rate-limited', air_telemetry_ingest($database, $capBatch, $capSource), 'The service-wide daily cap did not fail closed.');
    assertSame(
        0,
        countRowsForHash($database, 'air_telemetry_batches', air_telemetry_hmac('telemetry-install-v1:', $capInstallation)),
        'A service-capped batch was stored.'
    );
    $capRateRows = $database->prepare(
        "SELECT COUNT(*) FROM {$ratesTable} WHERE bucket_kind = 'installation_daily' AND bucket_hash = :hash");
    $capRateRows->bindValue(
        'hash',
        air_telemetry_hmac('telemetry-install-rate-v1:', $capInstallation),
        PDO::PARAM_LOB
    );
    $capRateRows->execute();
    assertSame(0, (int)$capRateRows->fetchColumn(), 'A service-capped request created an installation rate bucket.');
    assertSame(
        'accepted',
        air_telemetry_ingest($database, $batch, $capSource),
        'A known duplicate was rejected after the accepted-new-batch cap was reached.'
    );

    $sourceLimitValue = 'telemetry-integration-source-boundary-' . $runToken;
    $sourceLimitHash = air_telemetry_hmac('telemetry-source-rate-v1:', $sourceLimitValue);
    for ($index = 1; $index <= AIR_TELEMETRY_SOURCE_HOURLY_LIMIT + 1; $index++) {
        $database->beginTransaction();
        try {
            $allowed = air_telemetry_consume_rate(
                $database,
                'source_hourly',
                $sourceLimitHash,
                $databaseClock['hourStart'],
                AIR_TELEMETRY_SOURCE_HOURLY_LIMIT);
            $database->commit();
        } catch (Throwable $error) {
            if ($database->inTransaction()) {
                $database->rollBack();
            }
            throw $error;
        }
        assertSame($index <= AIR_TELEMETRY_SOURCE_HOURLY_LIMIT, $allowed, 'The source hourly rate boundary was wrong.');
    }

    $invalidLimitSource = 'telemetry-integration-invalid-boundary-' . $runToken;
    $invalidLimitHash = air_telemetry_hmac('telemetry-source-rate-v1:', $invalidLimitSource);
    $database->prepare(
        "INSERT INTO {$ratesTable} (bucket_kind, bucket_hash, window_start, request_count) "
        . "VALUES ('source_hourly', :hash, :window, :count) "
        . 'ON DUPLICATE KEY UPDATE request_count = VALUES(request_count)')
        ->execute([
            'hash' => $invalidLimitHash,
            'window' => $databaseClock['hourStart'],
            'count' => AIR_TELEMETRY_SOURCE_HOURLY_LIMIT,
        ]);
    assertSame(false, air_telemetry_record_invalid($invalidLimitSource), 'Invalid requests bypassed the source hourly limit.');

    $sourceRejectedInstallation = randomTelemetryUuid();
    $sourceRejectedBatch = makeBatch(
        installationId: $sourceRejectedInstallation,
        batchId: randomTelemetryUuid()
    );
    assertSame(
        'rate-limited',
        air_telemetry_ingest($database, $sourceRejectedBatch, $invalidLimitSource),
        'A source above its hourly limit was not rejected.'
    );
    $sourceRejectedRateHash = air_telemetry_hmac(
        'telemetry-install-rate-v1:',
        $sourceRejectedInstallation
    );
    $sourceRejectedRateRows = $database->prepare(
        "SELECT COUNT(*) FROM {$ratesTable} WHERE bucket_kind = 'installation_daily' AND bucket_hash = :hash");
    $sourceRejectedRateRows->bindValue('hash', $sourceRejectedRateHash, PDO::PARAM_LOB);
    $sourceRejectedRateRows->execute();
    assertSame(
        0,
        (int)$sourceRejectedRateRows->fetchColumn(),
        'A rejected source created an installation rate-bucket row.'
    );

    $oldHash = air_telemetry_hmac('telemetry-install-v1:', randomTelemetryUuid());
    $insertOld = $database->prepare(
        "INSERT INTO {$batchesTable} (installation_hash, batch_id, received_at) VALUES (:hash, :batch, '2000-01-01 00:00:00')");
    for ($index = 0; $index < 501; $index++) {
        $insertOld->bindValue('hash', $oldHash, PDO::PARAM_LOB);
        $insertOld->bindValue('batch', pack('N4', 0x70000000, 0, 0, $index), PDO::PARAM_LOB);
        $insertOld->execute();
    }
    $firstCleanup = runLockedRetentionCleanup($database, 500);
    assertSame(500, (int)$firstCleanup['air_telemetry_batches'], 'Retention exceeded or missed its 500-row bound.');
    assertSame(1, countRowsForHash($database, 'air_telemetry_batches', $oldHash), 'Retention did not leave exactly one owned row for the next pass.');
    runLockedRetentionCleanup($database, 500);
    assertSame(0, countRowsForHash($database, 'air_telemetry_batches', $oldHash), 'The next retention pass did not finish owned cleanup.');

    $insertVersionFixture = $database->prepare(
        'INSERT INTO ' . $dailyTable . ' '
        . '(activity_date, installation_hash, host_version, host_starts, first_received_at, last_received_at) '
        . 'VALUES (:date, :hash, :version, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))');
    for ($index = 0; $index < AIR_TELEMETRY_ADMIN_VERSION_LIMIT + 5; $index++) {
        $versionHash = air_telemetry_hmac(
            'telemetry-install-v1:',
            'dashboard-cardinality-' . $runToken . '-' . $index);
        $insertVersionFixture->bindValue('date', $today, PDO::PARAM_STR);
        $insertVersionFixture->bindValue('hash', $versionHash, PDO::PARAM_LOB);
        $insertVersionFixture->bindValue('version', '90.0.' . $index, PDO::PARAM_STR);
        $insertVersionFixture->execute();
    }
    $dashboard = air_telemetry_admin_dashboard($database, air_telemetry_admin_filters([]));
    assertTrue((int)$dashboard['summary']['installations'] >= 1, 'The dashboard omitted an opted-in fixture installation.');
    assertSame(10, count($dashboard['features']), 'The dashboard did not return the closed feature catalog.');
    assertTrue(count($dashboard['versions']) <= AIR_TELEMETRY_ADMIN_VERSION_LIMIT + 1, 'The dashboard returned unbounded version groups.');
    assertTrue(
        in_array('Other', array_column($dashboard['versions'], 'host_version'), true),
        'The dashboard did not collapse excess version cardinality into Other.');

    $authorizationSession = [];
    $authorizationRequest = ['action' => 'all', 'cutoff' => null, 'aggregate_cutoff' => null, 'short_cutoff' => null];
    $authorization = air_telemetry_admin_create_cleanup_authorization(
        $authorizationRequest,
        ['air_telemetry_daily' => 1]);
    $authorizationSession['preview'] = $authorization;
    assertSame(
        ['air_telemetry_daily' => 1],
        air_telemetry_admin_consume_cleanup_authorization(
            $authorizationSession,
            'preview',
            $authorizationRequest,
            $authorization['token']),
        'The cleanup authorization did not return its exact preview.');
    assertTrue(!isset($authorizationSession['preview']), 'The cleanup authorization was not consumed.');
    try {
        air_telemetry_admin_consume_cleanup_authorization(
            $authorizationSession,
            'preview',
            $authorizationRequest,
            $authorization['token']);
        throw new RuntimeException('A replayed cleanup authorization was accepted.');
    } catch (InvalidArgumentException) {
        // Expected: identical POST authorization cannot delete a second chunk.
    }

    $adminHash = air_telemetry_hmac('telemetry-install-v1:', randomTelemetryUuid());
    $adminRateHash = air_telemetry_hmac(
        'telemetry-source-rate-v1:',
        'telemetry-admin-cleanup-' . $runToken);
    $database->prepare(
        "INSERT INTO {$dailyTable} (activity_date, installation_hash, host_version, host_starts, first_received_at, last_received_at) "
        . "VALUES ('1900-01-01', :hash, '1.0.5', 1, '1900-01-01 00:00:00', '1900-01-01 00:00:00')")
        ->execute(['hash' => $adminHash]);
    $database->prepare(
        "INSERT INTO {$batchesTable} (installation_hash, batch_id, received_at) VALUES (:hash, :batch, '1900-01-01 00:00:00')")
        ->execute(['hash' => $adminHash, 'batch' => hex2bin('80000000000040008000000000000001')]);
    $database->prepare(
        "INSERT INTO {$ratesTable} (bucket_kind, bucket_hash, window_start, request_count) VALUES ('source_hourly', :hash, '1900-01-01 00:00:00', 1)")
        ->execute(['hash' => $adminRateHash]);
    $database->exec(
        "INSERT INTO {$ingestTable} (activity_date, accepted) VALUES ('1900-01-01', 1)");

    $adminCleanup = air_telemetry_admin_cleanup_request([
        'cleanup_action' => 'before',
        'cleanup_cutoff' => '1901-01-01',
    ]);
    $expectedPreview = [
        'air_telemetry_daily' => 1,
        'air_telemetry_ingest_daily' => 1,
        'air_telemetry_batches' => 1,
        'air_telemetry_rate_buckets' => 1,
    ];
    assertSame($expectedPreview, air_telemetry_admin_cleanup_preview($database, $adminCleanup), 'The cleanup preview did not match its exact SQL scope.');
    assertTrue(
        air_telemetry_admin_cleanup_scope_matches(
            ['air_telemetry_batches' => 1, 'air_telemetry_daily' => 2],
            ['air_telemetry_daily' => 2, 'air_telemetry_batches' => 1]),
        'Cleanup scope comparison depended on associative-key order.');
    assertTrue(
        !air_telemetry_admin_cleanup_scope_matches(
            ['air_telemetry_batches' => 1, 'air_telemetry_daily' => 2],
            ['air_telemetry_daily' => 3, 'air_telemetry_batches' => 1]),
        'Cleanup scope comparison accepted changed row counts.');

    $database->prepare(
        "INSERT INTO {$batchesTable} (installation_hash, batch_id, received_at) VALUES (:hash, :batch, '1900-01-01 00:00:00')")
        ->execute(['hash' => $adminHash, 'batch' => hex2bin('80000000000040008000000000000002')]);
    $changedScopeRejected = false;
    try {
        air_telemetry_admin_cleanup_chunk($database, $adminCleanup, $expectedPreview);
    } catch (AirTelemetryCleanupScopeChanged) {
        $changedScopeRejected = true;
    }
    assertTrue($changedScopeRejected, 'Cleanup executed after its reviewed preview scope changed.');
    $database->prepare(
        'DELETE FROM ' . $batchesTable . ' WHERE installation_hash = :hash AND batch_id = :batch')
        ->execute(['hash' => $adminHash, 'batch' => hex2bin('80000000000040008000000000000002')]);
    assertSame($expectedPreview, air_telemetry_admin_cleanup_preview($database, $adminCleanup), 'The scope-change rejection altered telemetry rows.');

    putenv('VOLTURA_AIR_TELEMETRY_FAIL=admin_cleanup_after_delete');
    $adminCleanupFailed = false;
    try {
        air_telemetry_admin_cleanup_chunk($database, $adminCleanup, $expectedPreview);
    } catch (AirTelemetryCleanupFailedSafely) {
        $adminCleanupFailed = true;
    } finally {
        putenv('VOLTURA_AIR_TELEMETRY_FAIL');
    }
    assertTrue($adminCleanupFailed, 'The administrator cleanup failure injection did not run.');
    assertSame($expectedPreview, air_telemetry_admin_cleanup_preview($database, $adminCleanup), 'A failed cleanup transaction deleted telemetry rows.');

    putenv('VOLTURA_AIR_TELEMETRY_FAIL=admin_cleanup_commit');
    $commitOutcomeUnknown = false;
    try {
        air_telemetry_admin_cleanup_chunk($database, $adminCleanup, $expectedPreview);
    } catch (AirTelemetryCleanupOutcomeUnknown) {
        $commitOutcomeUnknown = true;
    } finally {
        putenv('VOLTURA_AIR_TELEMETRY_FAIL');
    }
    assertTrue($commitOutcomeUnknown, 'An unacknowledged cleanup commit was reported as safely rolled back.');
    $afterAmbiguousCommit = air_telemetry_admin_cleanup_preview($database, $adminCleanup);
    assertSame(0, $afterAmbiguousCommit['air_telemetry_daily'], 'The commit ambiguity fixture did not exercise a committed deletion.');
    $database->prepare(
        "INSERT INTO {$dailyTable} (activity_date, installation_hash, host_version, host_starts, first_received_at, last_received_at) "
        . "VALUES ('1900-01-01', :hash, '1.0.5', 1, '1900-01-01 00:00:00', '1900-01-01 00:00:00')")
        ->execute(['hash' => $adminHash]);
    assertSame($expectedPreview, air_telemetry_admin_cleanup_preview($database, $adminCleanup), 'The commit ambiguity fixture was not restored.');

    putenv('VOLTURA_AIR_TELEMETRY_FAIL=admin_cleanup_before_commit,admin_cleanup_rollback');
    $rollbackOutcomeUnknown = false;
    try {
        air_telemetry_admin_cleanup_chunk($database, $adminCleanup, $expectedPreview);
    } catch (AirTelemetryCleanupOutcomeUnknown) {
        $rollbackOutcomeUnknown = true;
    } finally {
        putenv('VOLTURA_AIR_TELEMETRY_FAIL');
    }
    assertTrue($rollbackOutcomeUnknown, 'An unacknowledged cleanup rollback was reported as confirmed.');
    assertSame($expectedPreview, air_telemetry_admin_cleanup_preview($database, $adminCleanup), 'The rollback ambiguity fixture changed committed telemetry rows.');

    for ($index = 0; $index < 4; $index++) {
        $beforeChunk = air_telemetry_admin_cleanup_preview($database, $adminCleanup);
        $chunk = air_telemetry_admin_cleanup_chunk($database, $adminCleanup, $beforeChunk);
        assertTrue(array_sum($chunk['deleted']) <= AIR_TELEMETRY_ADMIN_DELETE_LIMIT, 'A manual cleanup request exceeded its 1,000-row bound.');
    }
    assertSame(0, array_sum(air_telemetry_admin_cleanup_preview($database, $adminCleanup)), 'Bounded cleanup did not finish after explicit continuation.');

    $indexNames = $schemaDatabase->query(
        "SELECT DISTINCT index_name FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name IN ('air_telemetry_daily', 'air_telemetry_batches', 'air_telemetry_rate_buckets')")
        ->fetchAll(PDO::FETCH_COLUMN);
    foreach (['air_telemetry_daily_version_date_installation', 'air_telemetry_daily_date_version', 'air_telemetry_batches_received', 'air_telemetry_rate_window'] as $requiredIndex) {
        assertTrue(in_array($requiredIndex, $indexNames, true), "Missing telemetry index {$requiredIndex}.");
    }

    assertSame($catalogBefore, catalogCounts($schemaDatabase), 'Telemetry integration changed Custom Screens catalog data.');
    echo "Telemetry PHP/MariaDB integration passed.\n";
} finally {
    putenv('VOLTURA_AIR_TELEMETRY_FAIL');
    $database = null;
}
