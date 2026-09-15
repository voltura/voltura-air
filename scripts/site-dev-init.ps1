#requires -Version 7.6 -PSEdition Core

param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 3306,
    [string]$RootUser = 'root',
    [switch]$Automatic,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$devRoot = Join-Path $repoRoot '.site-dev'
$configPath = Join-Path $devRoot 'config.php'
$phpIniPath = Join-Path $devRoot 'php.ini'
$storagePath = Join-Path $devRoot 'screen-packages'
$databaseName = 'voltura_air_dev'
$databaseUser = 'voltura_air_dev'

function Find-Executable([string[]]$Names, [string[]]$Patterns) {
    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    foreach ($pattern in $Patterns) {
        $match = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($match) { return $match.FullName }
    }
    return $null
}

function Refresh-ProcessPath {
    $entries = @(
        [Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';'
        [Environment]::GetEnvironmentVariable('Path', 'User') -split ';'
        $env:Path -split ';'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $deduplicated = [Collections.Generic.List[string]]::new()
    foreach ($entry in $entries) {
        $expanded = [Environment]::ExpandEnvironmentVariables($entry).Trim().TrimEnd('\')
        if (-not [string]::IsNullOrWhiteSpace($expanded) -and
            -not ($deduplicated | Where-Object { $_ -ieq $expanded })) {
            $deduplicated.Add($expanded)
        }
    }

    $wingetLinks = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links'
    if ((Test-Path -LiteralPath $wingetLinks -PathType Container) -and
        -not ($deduplicated | Where-Object { $_ -ieq $wingetLinks })) {
        $deduplicated.Insert(0, $wingetLinks)
    }

    $env:Path = $deduplicated -join ';'
}

function Find-WingetPortableExecutable([string]$PackageId, [string]$ExecutableName) {
    $packagesRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (-not (Test-Path -LiteralPath $packagesRoot -PathType Container)) { return $null }

    $packageDirectories = Get-ChildItem -Path (Join-Path $packagesRoot "$PackageId`_*") -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending
    foreach ($packageDirectory in $packageDirectories) {
        $match = Get-ChildItem -LiteralPath $packageDirectory.FullName -Filter $ExecutableName -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($match) { return $match.FullName }
    }
    return $null
}

function Find-PhpExecutable {
    $php = Find-Executable @('php.exe', 'php') @(
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links\php.exe",
        "$env:ProgramFiles\PHP\php.exe"
    )
    if ($php) { return $php }
    return Find-WingetPortableExecutable 'PHP.PHP.8.5' 'php.exe'
}

function Wait-ForPhpExecutable {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Refresh-ProcessPath
        $php = Find-PhpExecutable
        if ($php) { return $php }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Install-WingetPackage([string]$Id, [switch]$Interactive) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Windows Package Manager (winget) is required to install $Id."
    }
    $arguments = @('install', '--id', $Id, '--exact', '--source', 'winget', '--accept-package-agreements', '--accept-source-agreements')
    $arguments += if ($Interactive) { '--interactive' } else { '--silent' }
    & winget @arguments
    if ($LASTEXITCODE -ne 0) { throw "winget could not install $Id." }
}

function ConvertFrom-SecureValue([Security.SecureString]$Value) {
    return [Net.NetworkCredential]::new('', $Value).Password
}

function Escape-PhpSingleQuoted([string]$Value) {
    return $Value.Replace('\', '\\').Replace("'", "\'")
}

function New-DevelopmentPassword {
    $bytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    } finally {
        $generator.Dispose()
    }
    return [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}

Import-Module (Join-Path $PSScriptRoot 'setup/Setup.Storage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'setup/Setup.Database.psm1') -Force

function Invoke-MariaDb([string]$Executable, [string[]]$Arguments, [string]$Sql) {
    Invoke-SetupSql $Executable $Arguments $Sql $env:MYSQL_PWD
}

Import-Module (Join-Path $PSScriptRoot 'setup/Setup.SiteConfiguration.psm1')
$existingConfiguration = Read-SetupSiteConfiguration $configPath $Port $PSBoundParameters.ContainsKey('Port')
$existingConfig = $null -ne $existingConfiguration
if ($existingConfig) { $Port = $existingConfiguration.Port; $devPassword = $existingConfiguration.Password }

if ($CheckOnly) {
    $php = Find-PhpExecutable
    if (-not $php -or -not (Test-Path -LiteralPath $configPath)) { throw 'Local PHP/database configuration is missing. Run setup.' }
    & $php -c $phpIniPath (Join-Path $PSScriptRoot 'setup/check-site-database.php') $configPath $Port
    if ($LASTEXITCODE -ne 0) { throw 'Local database inspection failed. Run site:dev:init to configure it.' }
    exit 0
}
New-SetupPrivateDirectory $devRoot
$php = Find-PhpExecutable
if (-not $php) {
    Write-Host 'Installing PHP 8.5...'
    Install-WingetPackage 'PHP.PHP.8.5'
    $php = Wait-ForPhpExecutable
}
if (-not $php) {
    throw 'PHP was installed but php.exe could not be resolved from the refreshed PATH, WinGet links, or WinGet package directory.'
}

$mariaPatterns = @(
    "$env:ProgramFiles\VolturaAirDev-MariaDB\bin\mariadb.exe",
    "$env:ProgramFiles\MariaDB *\bin\mariadb.exe",
    "$env:ProgramFiles\MariaDB *\bin\mysql.exe",
    "${env:ProgramFiles(x86)}\MariaDB *\bin\mariadb.exe"
)
$maria = Find-Executable @('mariadb.exe', 'mariadb', 'mysql.exe', 'mysql') $mariaPatterns
$managedPassword = $null
if ($Automatic -and ((Get-Service VolturaAirDev -ErrorAction SilentlyContinue) -or (Test-Path (Join-Path $env:LOCALAPPDATA 'Voltura Air/Setup/MariaDB/state.json')) -or -not $maria)) {
    $managedPassword = Initialize-SetupDatabase $repoRoot $Port
    $maria = Find-Executable @('mariadb.exe', 'mariadb', 'mysql.exe', 'mysql') $mariaPatterns
} elseif (-not $maria) {
    Write-Host 'Installing MariaDB. Choose a local root password in the installer.'
    Install-WingetPackage 'MariaDB.Server' -Interactive
    Refresh-ProcessPath
    $maria = Find-Executable @('mariadb.exe', 'mariadb', 'mysql.exe', 'mysql') $mariaPatterns
}
if (-not $maria) { throw 'MariaDB was installed but its command-line client could not be found.' }

$mariaService = Get-Service -Name 'MariaDB' -ErrorAction SilentlyContinue
if ($mariaService -and $mariaService.Status -ne 'Running') {
    try {
        Start-Service -Name $mariaService.Name
        $mariaService.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
    } catch {
        throw 'The MariaDB service is installed but not running. Start it from Windows Services, then rerun this command.'
    }
}

New-Item -ItemType Directory -Force -Path $devRoot, $storagePath | Out-Null
$phpDirectory = Split-Path -Parent $php
$extensionDirectory = Join-Path $phpDirectory 'ext'
$pdoMysql = Join-Path $extensionDirectory 'php_pdo_mysql.dll'
if (-not (Test-Path -LiteralPath $pdoMysql)) { throw "PHP's PDO MySQL extension was not found at $pdoMysql." }
$zipExtension = Join-Path $extensionDirectory 'php_zip.dll'
if (-not (Test-Path -LiteralPath $zipExtension)) { throw "PHP's ZIP extension was not found at $zipExtension." }
$phpExtensionPath = Escape-PhpSingleQuoted ($extensionDirectory.Replace('\', '/'))
$phpIni = @"
extension_dir='$phpExtensionPath'
extension=pdo_mysql
extension=zip
file_uploads=On
upload_max_filesize=8M
post_max_size=9M
session.use_strict_mode=1
"@
if (-not (Test-Path -LiteralPath $phpIniPath)) { Write-SetupAtomicFile $phpIniPath $phpIni }
# Add only missing required extensions; preserve the existing development INI.
foreach ($extension in @('pdo_mysql', 'zip')) {
    & $php -c $phpIniPath -r "exit(extension_loaded('$extension') ? 0 : 1);"
    if ($LASTEXITCODE -ne 0) {
        $existingIni = Get-Content -LiteralPath $phpIniPath -Raw
        Write-SetupAtomicFile $phpIniPath ($existingIni.TrimEnd() + "`r`nextension=$extension`r`n")
    }
}
& $php -c $phpIniPath -r "exit(in_array('mysql', PDO::getAvailableDrivers(), true) && extension_loaded('zip') ? 0 : 1);"
if ($LASTEXITCODE -ne 0) { throw 'PHP could not load the PDO MySQL and ZIP extensions.' }

$rootPassword = $managedPassword
if (-not $existingConfig) {
$devPassword = New-DevelopmentPassword
$catalogSecret = (New-DevelopmentPassword) + (New-DevelopmentPassword)

$escapedStorage = Escape-PhpSingleQuoted ($storagePath.Replace('\', '/'))
$config = @"
<?php
return [
    'dsn' => 'mysql:host=127.0.0.1;port=$Port;dbname=$databaseName;charset=utf8mb4',
    'username' => '$databaseUser',
    'password' => '$devPassword',
    'storage_path' => '$escapedStorage',
    'catalog_secret' => '$catalogSecret',
];
"@
Write-SetupAtomicFile $configPath $config
}


$clientArguments = @('--host=127.0.0.1', '--protocol=TCP', "--port=$Port", "--user=$RootUser", '--batch', '--skip-column-names')
$previousPassword = $env:MYSQL_PWD
try {
    $devArguments = @('--host=127.0.0.1', '--protocol=TCP', "--port=$Port", "--user=$databaseUser", '--batch', '--skip-column-names', $databaseName)
    $probe = Invoke-SetupSql $maria $devArguments 'SELECT 1;' $devPassword
    if ($probe.ExitCode -eq 0) {
        $clientArguments = @('--host=127.0.0.1', '--protocol=TCP', "--port=$Port", "--user=$databaseUser", '--batch', '--skip-column-names')
        $env:MYSQL_PWD = $devPassword
    } else {
        if (-not $rootPassword) { $rootPassword = ConvertFrom-SecureValue (Read-Host 'Existing local MariaDB administrator password' -AsSecureString) }
        $env:MYSQL_PWD = $rootPassword
    $sqlPassword = $devPassword.Replace("'", "''")
    $bootstrap = "SET SESSION sql_mode='NO_BACKSLASH_ESCAPES'; CREATE DATABASE IF NOT EXISTS ``$databaseName`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; CREATE USER IF NOT EXISTS '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$sqlPassword'; ALTER USER '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$sqlPassword'; GRANT ALL PRIVILEGES ON ``$databaseName``.* TO '$databaseUser'@'127.0.0.1'; FLUSH PRIVILEGES;"
    $bootstrapResult = Invoke-MariaDb $maria $clientArguments $bootstrap
    if ($bootstrapResult.ExitCode -ne 0) {
        if ($bootstrapResult.Error -match 'Access denied') {
            throw "MariaDB is running on port $Port, but it rejected the root password. Verify it with 'mariadb -u root -p' and rerun this command."
        }
        throw "MariaDB could not be reached through its local client connection on port $Port. Confirm that its Windows service is running and that the port matches the installer."
    }

    }
    $databaseArguments = $clientArguments + @($databaseName)
    $tableResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = "air_screen_users";'
    if ($tableResult.ExitCode -ne 0) { throw 'Could not inspect the development database.' }
    if ([int]$tableResult.Output.Trim() -eq 0 -or (Test-Path -LiteralPath (Join-Path $devRoot 'catalog-bootstrap.pending'))) {
        if (-not (Test-Path -LiteralPath (Join-Path $devRoot 'catalog-bootstrap.pending'))) { Write-SetupAtomicFile (Join-Path $devRoot 'catalog-bootstrap.pending') 'Fresh catalog creation in progress; retain until every table exists.' }
        $schema = Get-Content -LiteralPath (Join-Path $repoRoot 'apps\public-site\screens\schema.sql') -Raw
        $schema = $schema -replace '(?im)^CREATE TABLE ', 'CREATE TABLE IF NOT EXISTS ' -replace '(?im)^INSERT INTO ', 'INSERT IGNORE INTO '
        $schemaResult = Invoke-MariaDb $maria $databaseArguments $schema
        if ($schemaResult.ExitCode -ne 0) { throw 'Could not create the development catalog tables.' }
    }

    $catalogUpgrade = Get-Content -LiteralPath (Join-Path $repoRoot 'apps\public-site\screens\schema-upgrade.sql') -Raw
    $catalogUpgradeResult = Invoke-MariaDb $maria $databaseArguments $catalogUpgrade
    if ($catalogUpgradeResult.ExitCode -ne 0) { throw 'Could not apply the additive development catalog schema upgrade.' }

    $currentSchemaResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ("air_screen_users", "air_screen_packages", "air_screen_reports", "air_screen_ratings", "air_screen_verification_tokens", "air_screen_rate_buckets", "air_screen_cleanup_jobs", "air_screen_maintenance");'
    if ($currentSchemaResult.ExitCode -ne 0) { throw 'Could not inspect the development catalog schema.' }
    if ([int]$currentSchemaResult.Output.Trim() -ne 8) {
        throw 'The development catalog uses a superseded schema. Clear the development database explicitly, then rerun site:dev:init.'
    }

    if (Test-Path -LiteralPath (Join-Path $devRoot 'catalog-bootstrap.pending')) { [IO.File]::Delete((Join-Path $devRoot 'catalog-bootstrap.pending')) }

    # Telemetry is an additive schema with its own idempotent lifecycle. It is
    # deliberately independent from the catalog's fresh-schema-only contract.
    $telemetrySchema = Get-Content -LiteralPath (Join-Path $repoRoot 'apps\public-site\telemetry\schema.sql') -Raw
    $telemetrySchemaResult = Invoke-MariaDb $maria $databaseArguments $telemetrySchema
    if ($telemetrySchemaResult.ExitCode -ne 0) { throw 'Could not apply the additive development telemetry schema.' }
    $telemetryTablesResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ("air_telemetry_daily", "air_telemetry_batches", "air_telemetry_rate_buckets", "air_telemetry_ingest_daily", "air_telemetry_maintenance");'
    if ($telemetryTablesResult.ExitCode -ne 0 -or [int]$telemetryTablesResult.Output.Trim() -ne 5) {
        throw 'The additive development telemetry schema is incomplete.'
    }
    $telemetryColumnsResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND ((table_name = "air_telemetry_daily" AND column_name IN ("activity_date", "installation_hash", "host_version", "host_starts", "connections_standard_local", "connections_enhanced_direct", "connections_relay", "features_trackpad", "features_keyboard", "features_dictation", "features_media_controls", "features_presentation", "features_custom_screens", "features_files", "features_screen_viewing", "features_phone_webcam", "features_gyro_mouse", "first_received_at", "last_received_at")) OR (table_name = "air_telemetry_maintenance" AND column_name IN ("singleton_id", "next_cleanup_at")));'
    if ($telemetryColumnsResult.ExitCode -ne 0 -or [int]$telemetryColumnsResult.Output.Trim() -ne 21) {
        throw 'The additive development telemetry schema has missing required columns.'
    }

} finally {
    $env:MYSQL_PWD = $previousPassword
    $rootPassword = $null
    $managedPassword = $null
}

Write-Host 'Local site development is ready.'
Write-Host 'Run: npm run site:dev'
