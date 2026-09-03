<?php
declare(strict_types=1);

$root = dirname(__DIR__);
$configPath = getenv('VOLTURA_AIR_SCREENS_CONFIG');
if (getenv('VOLTURA_AIR_SITE_DEV') !== '1' || !$configPath || !is_file($configPath)) {
    fwrite(STDERR, "Use an explicit isolated site-development catalog configuration.\n");
    exit(2);
}
require $root . '/apps/public-site/screens/lib.php';

$database = air_screen_db();
$eligibleQueries = [
    'SELECT COUNT(*) FROM air_screen_reports WHERE created_at < TIMESTAMPADD(DAY, -180, CURRENT_TIMESTAMP)',
    'SELECT COUNT(*) FROM air_screen_rate_buckets WHERE window_started < TIMESTAMPADD(DAY, -1, CURRENT_TIMESTAMP) '
        . 'AND (blocked_until IS NULL OR blocked_until <= CURRENT_TIMESTAMP)',
    'SELECT COUNT(*) FROM air_screen_verification_tokens WHERE expires_at < TIMESTAMPADD(DAY, -7, CURRENT_TIMESTAMP)',
    'SELECT COUNT(*) FROM air_screen_users u WHERE verified_at IS NULL '
        . 'AND created_at < TIMESTAMPADD(DAY, -30, CURRENT_TIMESTAMP) '
        . 'AND NOT EXISTS (SELECT 1 FROM air_screen_verification_tokens t WHERE t.user_id = u.id AND t.expires_at > CURRENT_TIMESTAMP) '
        . 'AND NOT EXISTS (SELECT 1 FROM air_screen_packages p WHERE p.owner_id = u.id)',
    "SELECT COUNT(*) FROM air_screen_packages WHERE status = 'removed' "
        . 'AND removed_at < TIMESTAMPADD(DAY, -30, CURRENT_TIMESTAMP)',
];
foreach ($eligibleQueries as $query) {
    if ((int)$database->query($query)->fetchColumn() !== 0) {
        fwrite(STDERR, "Refusing to run: the catalog contains non-fixture rows eligible for retention.\n");
        exit(2);
    }
}

$token = bin2hex(random_bytes(8));
$ownerId = null;
$pendingUserId = null;
$packageId = air_screen_uuid();
$rateKey = hash('sha256', 'catalog-maintenance-' . $token);
$contents = '{"catalogMaintenance":"' . $token . '"}';
$hash = hash('sha256', $contents);
$basename = $hash . '.volturascreen';
$path = air_screen_package_path($basename);

try {
    $userInsert = $database->prepare(
        "INSERT INTO air_screen_users (email, password_hash, display_name, verified_at, created_at) "
        . "VALUES (:email, :password, :name, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
    $userInsert->execute([
        'email' => 'catalog-maintenance-owner-' . $token . '@example.invalid',
        'password' => password_hash($token, PASSWORD_DEFAULT),
        'name' => 'Catalog maintenance owner',
    ]);
    $ownerId = (int)$database->lastInsertId();

    $pendingInsert = $database->prepare(
        "INSERT INTO air_screen_users (email, password_hash, display_name, created_at) "
        . "VALUES (:email, :password, :name, '2000-01-01 00:00:00')");
    $pendingInsert->execute([
        'email' => 'catalog-maintenance-pending-' . $token . '@example.invalid',
        'password' => password_hash($token, PASSWORD_DEFAULT),
        'name' => 'Catalog maintenance pending',
    ]);
    $pendingUserId = (int)$database->lastInsertId();
    $database->prepare(
        "INSERT INTO air_screen_verification_tokens (user_id, token_hash, expires_at, created_at) "
        . "VALUES (:user, :hash, '2000-01-02 00:00:00', '2000-01-01 00:00:00')")
        ->execute(['user' => $pendingUserId, 'hash' => hash('sha256', 'verify-' . $token)]);

    file_put_contents($path, $contents, LOCK_EX);
    $database->prepare(
        "INSERT INTO air_screen_packages (id, owner_id, name, description, tags, package_version, screen_json, "
        . "storage_basename, status, screen_id, created_at, removed_at) VALUES "
        . "(:id, :owner, 'Maintenance fixture', '', '', 1, '{}', :basename, 'removed', :screen_id, "
        . "'2000-01-01 00:00:00', '2000-01-01 00:00:00')")
        ->execute([
            'id' => $packageId,
            'owner' => $ownerId,
            'basename' => $basename,
            'screen_id' => 'maintenance.' . $token,
        ]);
    $database->prepare(
        "INSERT INTO air_screen_reports (package_id, reporter_email, reason, created_at) "
        . "VALUES (:package, :email, 'Maintenance fixture', '2000-01-01 00:00:00')")
        ->execute(['package' => $packageId, 'email' => 'reporter-' . $token . '@example.invalid']);
    $database->prepare(
        "INSERT INTO air_screen_rate_buckets (scope, bucket_key, window_started, attempts) "
        . "VALUES ('maintenance_test', :key, '2000-01-01 00:00:00', 1)")
        ->execute(['key' => $rateKey]);

    putenv('VOLTURA_AIR_CATALOG_MAINTENANCE_FAIL=after_delete');
    try {
        air_screen_maybe_maintain_catalog(10, true);
        throw new RuntimeException('The maintenance rollback injection did not run.');
    } catch (RuntimeException $error) {
        assertSame(
            'Injected catalog maintenance failure at after_delete.',
            $error->getMessage(),
            'The maintenance failure was not the requested injection.');
    } finally {
        putenv('VOLTURA_AIR_CATALOG_MAINTENANCE_FAIL');
    }
    assertSame(1, fixtureCount($database, 'air_screen_packages', 'id', $packageId), 'A rolled-back package deletion persisted.');
    assertSame(1, fixtureCount($database, 'air_screen_users', 'id', (string)$pendingUserId), 'A rolled-back account deletion persisted.');
    assertTrue(is_file($path), 'A rolled-back cleanup deleted the package file.');

    $counts = air_screen_maybe_maintain_catalog(10, true);
    foreach (['air_screen_reports', 'air_screen_rate_buckets', 'air_screen_verification_tokens', 'air_screen_users', 'air_screen_packages', 'air_screen_cleanup_jobs'] as $owner) {
        assertSame(1, (int)($counts[$owner] ?? 0), "Catalog maintenance did not complete {$owner}.");
    }
    assertSame(0, fixtureCount($database, 'air_screen_packages', 'id', $packageId), 'The removed package survived retention.');
    assertSame(0, fixtureCount($database, 'air_screen_users', 'id', (string)$pendingUserId), 'The never-verified account survived retention.');
    assertTrue(!is_file($path), 'The durable cleanup queue did not remove the unreferenced package file.');
    echo "Catalog maintenance PHP/MariaDB integration passed.\n";
} finally {
    putenv('VOLTURA_AIR_CATALOG_MAINTENANCE_FAIL');
    $database->prepare('DELETE FROM air_screen_reports WHERE package_id = :id')->execute(['id' => $packageId]);
    $database->prepare('DELETE FROM air_screen_packages WHERE id = :id')->execute(['id' => $packageId]);
    $database->prepare("DELETE FROM air_screen_rate_buckets WHERE scope = 'maintenance_test' AND bucket_key = :key")
        ->execute(['key' => $rateKey]);
    $database->prepare('DELETE FROM air_screen_cleanup_jobs WHERE storage_basename = :basename')
        ->execute(['basename' => $basename]);
    if ($pendingUserId !== null) {
        $database->prepare('DELETE FROM air_screen_users WHERE id = :id')->execute(['id' => $pendingUserId]);
    }
    if ($ownerId !== null) {
        $database->prepare('DELETE FROM air_screen_users WHERE id = :id')->execute(['id' => $ownerId]);
    }
    if (is_file($path)) {
        unlink($path);
    }
}

function fixtureCount(PDO $database, string $table, string $column, string $value): int
{
    $allowed = [
        'air_screen_packages' => ['id'],
        'air_screen_users' => ['id'],
    ];
    if (!in_array($column, $allowed[$table] ?? [], true)) {
        throw new InvalidArgumentException('Invalid fixture count owner.');
    }
    $statement = $database->prepare("SELECT COUNT(*) FROM {$table} WHERE {$column} = :value");
    $statement->execute(['value' => $value]);
    return (int)$statement->fetchColumn();
}

function assertSame(mixed $expected, mixed $actual, string $message): void
{
    if ($expected !== $actual) {
        throw new RuntimeException($message);
    }
}

function assertTrue(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}
