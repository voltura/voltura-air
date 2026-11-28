<?php
declare(strict_types=1);

function runPackageStorageWorker(string $root, array $arguments): void
{
    if (($arguments[1] ?? '') === '--drain-package-jobs') {
        echo air_screen_drain_cleanup_jobs(100);
        exit;
    }
    if (($arguments[1] ?? '') === '--upload-package-fixture') {
        $_SESSION['air_screen_user'] = ['id' => (int)$arguments[2]];
        $_SERVER['REQUEST_METHOD'] = 'POST';
        $_POST = ['csrf' => air_screen_csrf()];
        $_FILES = ['package' => ['error' => UPLOAD_ERR_OK, 'size' => filesize($arguments[3]), 'tmp_name' => $arguments[3]]];
        putenv('VOLTURA_AIR_PACKAGE_STORAGE_FAIL=' . $arguments[4]);
        require $root . '/apps/public-site/screens/upload.php';
        exit;
    }
    if (($arguments[1] ?? '') === '--delete-package-fixture') {
        $_SESSION['air_screen_user'] = ['id' => (int)$arguments[2]];
        $_SERVER['REQUEST_METHOD'] = 'POST';
        $_POST = ['csrf' => air_screen_csrf(), 'id' => $arguments[3]];
        require $root . '/apps/public-site/screens/delete.php';
        exit;
    }
}

function assertPackageStorageOwnership(PDO $database, int $ownerId, string $token): void
{
    $officialCatalog = dirname(__DIR__) . '/artifacts/custom-screens/official/';
    $catalog = json_decode(file_get_contents($officialCatalog . 'catalog.json'), true, 32, JSON_THROW_ON_ERROR);
    $package = json_decode(file_get_contents($officialCatalog . $catalog['screens'][0]['packageFilename']), true, 32, JSON_THROW_ON_ERROR);
    $screenId = 'storage.integration.' . $token;
    $package['screen']['id'] = $screenId;
    $input = json_encode($package, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
    $contents = json_encode(air_screen_validate_package($input), JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
    $hash = hash('sha256', $contents);
    $basename = $hash . '.volturascreen';
    $path = air_screen_package_path($basename);
    $partialPath = $path . '.part';
    $source = tempnam(sys_get_temp_dir(), 'air-upload-integration-');
    if ($source === false) {
        throw new RuntimeException('Could not create the upload package fixture.');
    }
    $deletePackage = $database->prepare('DELETE FROM air_screen_packages WHERE owner_id = :owner AND screen_id = :id');
    $packageRows = $database->prepare('SELECT COUNT(*) FROM air_screen_packages WHERE owner_id = :owner AND screen_id = :id');
    $packageParams = ['owner' => $ownerId, 'id' => $screenId];
    $deleteJob = $database->prepare('DELETE FROM air_screen_cleanup_jobs WHERE storage_basename = :basename');
    $job = $database->prepare('SELECT attempts FROM air_screen_cleanup_jobs WHERE storage_basename = :basename');
    $jobParams = ['basename' => $basename];

    try {
        $lock = air_screen_try_acquire_package_storage_lock($database, 0)
            ?? throw new RuntimeException('Could not hold the storage lock for the concurrency fixture.');
        try {
            air_screen_enqueue_cleanup($database, $basename, $hash);
            assertSame('0', runPackageStorageChild(['--drain-package-jobs']), 'Cleanup retired a locked job before staging.');
            $job->execute($jobParams);
            $attempts = $job->fetchColumn();
            assertTrue($attempts !== false && (int)$attempts === 0, 'Cleanup modified the locked new job.');

            assertTrue(file_put_contents($partialPath, 'interrupted write') !== false, 'Could not stage partial fixture bytes.');
            assertSame('0', runPackageStorageChild(['--drain-package-jobs']), 'Cleanup retired a locked staging file.');
            clearstatcache(true, $partialPath);
            assertTrue(is_file($partialPath), 'Concurrent cleanup removed the in-progress file.');

            air_screen_write_content_file($path, $contents, $hash);
            assertSame('0', runPackageStorageChild(['--drain-package-jobs']), 'Cleanup retired the locked final file.');
            assertSame($contents, file_get_contents($path), 'Concurrent cleanup removed the installed file before commit.');
        } finally {
            air_screen_release_advisory_lock($database, $lock);
        }
        assertSame('1', runPackageStorageChild(['--drain-package-jobs']), 'Cleanup did not resume after storage lock release.');

        assertTrue(file_put_contents($source, $input) !== false, 'Could not write the upload package fixture.');
        foreach ([
            'journal_commit' => null,
            'stage_write' => $partialPath,
            'install_rename' => $partialPath,
            'db_insert' => $path,
            'db_commit' => $path,
            'db_commit,db_rollback' => $path,
        ] as $failure => $recoveryFile) {
            $body = runPackageStorageChild(['--upload-package-fixture', (string)$ownerId, $source, $failure]);
            assertTrue(str_contains($body, 'The screen could not be submitted.'), "Upload did not report the {$failure} failure.");
            $packageRows->execute($packageParams);
            assertSame(0, (int)$packageRows->fetchColumn(), "Upload committed a row after {$failure}.");
            $job->execute($jobParams);
            assertSame($recoveryFile !== null, $job->fetchColumn() !== false, "Cleanup ownership changed after {$failure}.");
            if ($recoveryFile !== null) {
                clearstatcache(true, $recoveryFile);
                assertTrue(is_file($recoveryFile), "Upload lost the recovery file after {$failure}.");
                assertSame('1', runPackageStorageChild(['--drain-package-jobs']), "Cleanup could not recover after {$failure}.");
            }
            clearstatcache();
            assertTrue(!is_file($path) && !is_file($partialPath), "Unowned bytes survived {$failure}.");
        }

        runPackageStorageChild(['--upload-package-fixture', (string)$ownerId, $source, 'db_committed']);
        $packageRows->execute($packageParams);
        assertSame(1, (int)$packageRows->fetchColumn(), 'The upload lost its committed row after leaving the mutation.');
        $job->execute($jobParams);
        assertSame(false, $job->fetchColumn(), 'A committed row retained its staging journal.');
        air_screen_enqueue_cleanup($database, $basename, $hash);
        assertSame('0', runPackageStorageChild(['--drain-package-jobs']), 'Cleanup removed a referenced package.');
        clearstatcache(true, $path);
        assertSame($contents, file_get_contents($path), 'The committed package bytes did not survive cleanup.');

        $published = $database->prepare('SELECT id FROM air_screen_packages WHERE owner_id = :owner AND screen_id = :id');
        $published->execute($packageParams);
        $packageId = (string)$published->fetchColumn();
        $database->prepare("UPDATE air_screen_packages SET status = 'approved' WHERE id = :id")
            ->execute(['id' => $packageId]);
        $database->prepare("UPDATE air_screen_users SET role = 'admin' WHERE id = :id")
            ->execute(['id' => $ownerId]);
        try {
            runPackageStorageChild(['--delete-package-fixture', (string)$ownerId, $packageId]);
        } finally {
            $database->prepare("UPDATE air_screen_users SET role = 'user' WHERE id = :id")
                ->execute(['id' => $ownerId]);
        }
        $packageRows->execute($packageParams);
        assertSame(0, (int)$packageRows->fetchColumn(), 'Deleting the referenced package did not remove its row.');
        clearstatcache(true, $path);
        assertTrue(!is_file($path), 'Deleting the referenced package did not reclaim its file.');
    } finally {
        $deletePackage->execute($packageParams);
        $deleteJob->execute($jobParams);
        foreach ([$path, $partialPath, $source] as $file) {
            clearstatcache(true, $file);
            if (is_file($file)) {
                unlink($file);
            }
        }
    }
}

function runPackageStorageChild(array $arguments): string
{
    $command = [PHP_BINARY, '-c', php_ini_loaded_file() ?: '', __DIR__ . '/site-catalog-maintenance-integration.php', ...$arguments];
    $process = proc_open($command, [1 => ['pipe', 'w'], 2 => ['pipe', 'w']], $pipes);
    if (!is_resource($process)) {
        throw new RuntimeException('Could not start the package storage fixture process.');
    }
    $output = stream_get_contents($pipes[1]);
    $error = stream_get_contents($pipes[2]);
    fclose($pipes[1]);
    fclose($pipes[2]);
    if (proc_close($process) !== 0) {
        throw new RuntimeException('The package storage fixture failed: ' . $error);
    }
    return trim($output);
}
