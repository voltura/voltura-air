#requires -Version 7.6 -PSEdition Core

param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 3306,
    [string]$RootUser = 'root'
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

function Invoke-MariaDb([string]$Executable, [string[]]$Arguments, [string]$Sql) {
    $operationId = [Guid]::NewGuid().ToString('N')
    $inputPath = Join-Path $devRoot "$operationId.sql"
    $outputPath = Join-Path $devRoot "$operationId.stdout"
    $errorPath = Join-Path $devRoot "$operationId.stderr"
    try {
        [IO.File]::WriteAllText($inputPath, $Sql, [Text.UTF8Encoding]::new($false))
        $process = Start-Process -FilePath $Executable `
            -ArgumentList $Arguments `
            -NoNewWindow `
            -Wait `
            -PassThru `
            -RedirectStandardInput $inputPath `
            -RedirectStandardOutput $outputPath `
            -RedirectStandardError $errorPath
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = if (Test-Path -LiteralPath $outputPath) { Get-Content -LiteralPath $outputPath -Raw } else { '' }
            Error = if (Test-Path -LiteralPath $errorPath) { Get-Content -LiteralPath $errorPath -Raw } else { '' }
        }
    } finally {
        Remove-Item -LiteralPath $inputPath, $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

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
    "$env:ProgramFiles\MariaDB *\bin\mariadb.exe",
    "$env:ProgramFiles\MariaDB *\bin\mysql.exe",
    "${env:ProgramFiles(x86)}\MariaDB *\bin\mariadb.exe"
)
$maria = Find-Executable @('mariadb.exe', 'mariadb', 'mysql.exe', 'mysql') $mariaPatterns
if (-not $maria) {
    Write-Host 'Installing MariaDB. Keep the default local database instance enabled and choose a root password in the installer.'
    Install-WingetPackage 'MariaDB.Server' -Interactive
    Read-Host 'Finish the MariaDB installer completely, including its root-password setup, then press Enter here'
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
[IO.File]::WriteAllText($phpIniPath, $phpIni, [Text.UTF8Encoding]::new($false))
& $php -c $phpIniPath -r "exit(in_array('mysql', PDO::getAvailableDrivers(), true) && extension_loaded('zip') ? 0 : 1);"
if ($LASTEXITCODE -ne 0) { throw 'PHP could not load the PDO MySQL and ZIP extensions.' }

$rootPassword = ConvertFrom-SecureValue (Read-Host "MariaDB root password selected in the installer" -AsSecureString)
$devPassword = if (Test-Path -LiteralPath $configPath) {
    $existing = Get-Content -LiteralPath $configPath -Raw
    $match = [regex]::Match($existing, "'password'\s*=>\s*'([a-f0-9]{48})'")
    if ($match.Success) { $match.Groups[1].Value } else { New-DevelopmentPassword }
} else {
    New-DevelopmentPassword
}

$clientArguments = @("--port=$Port", "--user=$RootUser", '--batch', '--skip-column-names')
$previousPassword = $env:MYSQL_PWD
try {
    $env:MYSQL_PWD = $rootPassword
    $bootstrap = "CREATE DATABASE IF NOT EXISTS ``$databaseName`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; CREATE USER IF NOT EXISTS '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$devPassword'; ALTER USER '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$devPassword'; GRANT ALL PRIVILEGES ON ``$databaseName``.* TO '$databaseUser'@'127.0.0.1'; FLUSH PRIVILEGES;"
    $bootstrapResult = Invoke-MariaDb $maria $clientArguments $bootstrap
    if ($bootstrapResult.ExitCode -ne 0) {
        if ($bootstrapResult.Error -match 'Access denied') {
            throw "MariaDB is running on port $Port, but it rejected the root password. Verify it with 'mariadb -u root -p' and rerun this command."
        }
        throw "MariaDB could not be reached through its local client connection on port $Port. Confirm that its Windows service is running and that the port matches the installer."
    }

    $databaseArguments = $clientArguments + @($databaseName)
    $tableResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = "air_screen_users";'
    if ($tableResult.ExitCode -ne 0) { throw 'Could not inspect the development database.' }
    if ([int]$tableResult.Output.Trim() -eq 0) {
        $schema = Get-Content -LiteralPath (Join-Path $repoRoot 'apps\public-site\screens\schema.sql') -Raw
        $schemaResult = Invoke-MariaDb $maria $databaseArguments $schema
        if ($schemaResult.ExitCode -ne 0) { throw 'Could not create the development catalog tables.' }
    }

    $ratingResult = Invoke-MariaDb $maria $databaseArguments 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = "air_screen_ratings";'
    if ($ratingResult.ExitCode -ne 0) { throw 'Could not inspect the ratings migration state.' }
    if ([int]$ratingResult.Output.Trim() -eq 0) {
        $migration = Get-Content -LiteralPath (Join-Path $repoRoot 'apps\public-site\screens\migration-002-ratings.sql') -Raw
        $migrationResult = Invoke-MariaDb $maria $databaseArguments $migration
        if ($migrationResult.ExitCode -ne 0) { throw 'Could not apply the ratings migration.' }
    }

} finally {
    $env:MYSQL_PWD = $previousPassword
    $rootPassword = $null
}

$escapedStorage = Escape-PhpSingleQuoted ($storagePath.Replace('\', '/'))
$config = @"
<?php
return [
    'dsn' => 'mysql:host=127.0.0.1;port=$Port;dbname=$databaseName;charset=utf8mb4',
    'username' => '$databaseUser',
    'password' => '$devPassword',
    'storage_path' => '$escapedStorage',
];
"@
[IO.File]::WriteAllText($configPath, $config, [Text.UTF8Encoding]::new($false))

Write-Host 'Local site development is ready.'
Write-Host 'Run: npm run site:dev'
