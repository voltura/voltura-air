#requires -Version 7.6 -PSEdition Core
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Storage, [string]$Package, [Parameter(Mandatory)][string]$UserSid)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Setup.Storage.psm1')
Import-Module (Join-Path $PSScriptRoot 'Setup.DatabaseState.psm1')
$operation = 'preflight'
try {
    if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $UserSid) { throw 'Elevate using the Windows account that started setup.' }
    $expectedStorage = Join-Path $env:LOCALAPPDATA 'Voltura Air\Setup\MariaDB'
    if ([IO.Path]::GetFullPath($Storage) -ine [IO.Path]::GetFullPath($expectedStorage)) { throw 'Unexpected database setup storage.' }
    Assert-SetupPlainDirectory $Storage
    $installRoot = Join-Path $env:ProgramFiles 'VolturaAirDev-MariaDB'
    Assert-SetupPlainDirectory $installRoot
    $statePath = Join-Path $Storage 'state.json'
    $state = Get-SetupDatabaseState $statePath $installRoot $UserSid
    $service = Get-CimInstance Win32_Service -Filter "Name='VolturaAirDev'"
    $data = Join-Path $installRoot 'data'
    $config = Join-Path $data 'my.ini'
    $server = Join-Path $installRoot 'bin\mysqld.exe'
    $client = Join-Path $installRoot 'bin\mariadb.exe'
    $expectedServicePath = '"' + $server + '" "--defaults-file=' + $config + '" "VolturaAirDev"'
    if ($service -and $service.PathName -ine $expectedServicePath) { throw 'VolturaAirDev belongs to a different installation or configuration.' }
    if ($state.phase -eq 'installing') {
        if ($service -or (Test-Path -LiteralPath $data)) { throw 'Unexpected database state during binary installation. Preserve it for inspection.' }
        if (-not $Package -or -not [IO.Path]::GetFullPath($Package).StartsWith("$Storage\download-", [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected MariaDB installer path.' }
        if ((Get-AuthenticodeSignature -LiteralPath $Package).Status -ne 'Valid') { throw 'MariaDB installer signature is not valid.' }
        # No DBInstance and no PASSWORD: credentials never enter MSI or child command lines.
        $msi = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $Package + '"'), '/qn', '/norestart', 'ADDLOCAL=MYSQLSERVER,Client,SharedLibraries', ('INSTALLDIR="' + $installRoot + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($msi.ExitCode -in @(3010, 1641)) { exit 3010 }
        if ($msi.ExitCode -ne 0) { throw 'MariaDB binary installation failed.' }
        foreach ($binary in @($server, $client, (Join-Path $installRoot 'bin\mysql_install_db.exe'))) { if (-not (Test-Path -LiteralPath $binary)) { throw 'MariaDB binaries are incomplete.' } }
        Save-SetupDatabasePhase $state $statePath 'binaries'
    }
    if (-not $service) { Repair-SetupPartialDatabase $state $statePath }
    $password = [Net.NetworkCredential]::new('', (Get-Content (Join-Path $Storage 'root.dpapi') -Raw | ConvertTo-SecureString)).Password
    if ($password -notmatch '^[a-f0-9]{48}$') { throw 'Unrecognized managed database credential.' }
    if ($state.phase -eq 'binaries') {
        if ($service -or (Test-Path -LiteralPath $data)) { throw 'Unexpected database data before initialization.' }
        Save-SetupDatabasePhase $state $statePath 'initializing'
        New-SetupPrivateDirectory $data
        & (Join-Path $installRoot 'bin\mysql_install_db.exe') "--datadir=$data" --port=3306 --skip-networking | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Offline MariaDB initialization failed; partial data will be preserved on retry.' }
        Save-SetupDatabasePhase $state $statePath 'initialized'
    }
    if ($state.phase -eq 'initialized') {
        $init = Join-Path $data 'setup-init.sql'
        Write-SetupAtomicFile $init "ALTER USER 'root'@'localhost' IDENTIFIED BY '$password';"
        $ini = "[mysqld]`r`nbasedir=$($installRoot.Replace('\','/'))`r`ndatadir=$($data.Replace('\','/'))`r`nbind-address=127.0.0.1`r`nport=3306`r`ninit-file=$($init.Replace('\','/'))`r`n"
        Write-SetupAtomicFile $config $ini
        Save-SetupDatabasePhase $state $statePath 'registering'
    }
    if ($state.phase -eq 'registering' -and -not $service) {
        & $server --install VolturaAirDev "--defaults-file=$config" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'MariaDB service registration failed; rerun setup.' }
        $service = Get-CimInstance Win32_Service -Filter "Name='VolturaAirDev'"
        if (-not $service -or $service.PathName -ine $expectedServicePath) { throw 'The registered database service has an unexpected command.' }
    }
    if ($state.phase -notin @('registering', 'ready') -or -not $service) { throw 'Managed database service state is incomplete.' }
    $currentConfig = Get-Content -LiteralPath $config -Raw
    $operation = 'configuration validation'
    Assert-SetupDatabaseConfiguration $currentConfig $installRoot
    $operation = 'service readiness'
    Start-SetupDatabaseService $client $password ($service.State -eq 'Running')
    $operation = 'initialization file cleanup'
    # Password was proven before removing its startup file. Either cleanup step is repeatable.
    $init = Join-Path $data 'setup-init.sql'
    if (Test-Path -LiteralPath $init) {
        Write-SetupAtomicFile $config ([regex]::Replace($currentConfig, '(?m)^init-file=.*\r?\n?', ''))
        [IO.File]::Delete($init)
    }
    Save-SetupDatabasePhase $state $statePath 'ready'
    exit 0
} catch {
    if ($operation -eq 'service readiness') { [Console]::Error.WriteLine($_.Exception.Message) }
    [Console]::Error.WriteLine('Managed MariaDB setup failed. Preserve data and DPAPI credentials. Last stage: ' + $(if ($state) { $state.phase } else { 'preflight' }) + '. Operation: ' + $operation)
    exit 1
} finally { $password = $null }
