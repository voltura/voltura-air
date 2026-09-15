Import-Module (Join-Path $PSScriptRoot 'Setup.Storage.psm1')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SetupDatabaseConfiguration {
    param([string]$Text, [string]$InstallRoot)
    $data = Join-Path $InstallRoot 'data'
    $expected = @('[mysqld]', "basedir=$($InstallRoot.Replace('\','/'))", "datadir=$($data.Replace('\','/'))", 'bind-address=127.0.0.1', 'port=3306')
    $lines = @($Text -split '\r?\n' | Where-Object { $_ -ne '' })
    $init = 'init-file=' + (Join-Path $data 'setup-init.sql').Replace('\','/')
    if ($lines.Count -eq 6 -and $lines[5] -ceq $init) { $expected += $init }
    if (($lines -join "`n") -cne ($expected -join "`n")) { throw 'Managed MariaDB configuration changed. Preserve and inspect my.ini before starting the service.' }
}

function Assert-SetupDatabaseListener {
    param([int]$ProcessId)
    $listeners = @(Get-NetTCPConnection -State Listen -OwningProcess $ProcessId -ErrorAction Stop)
    if ($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1' -or $listeners[0].LocalPort -ne 3306) { throw 'Managed MariaDB must listen only on 127.0.0.1:3306.' }
}

function Start-SetupDatabaseService {
    param([string]$Client, [string]$Password, [bool]$AlreadyRunning)
    try {
        Start-Service VolturaAirDev
        (Get-Service VolturaAirDev).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $result = Invoke-SetupSql $Client @('--host=127.0.0.1', '--port=3306', '--user=root', '--batch', '--skip-column-names') 'SELECT 1;' $Password
        if ($result.ExitCode -ne 0) { throw 'Database authentication failed.' }
        $running = Get-CimInstance Win32_Service -Filter "Name='VolturaAirDev'"
        Assert-SetupDatabaseListener $running.ProcessId
    } catch {
        if (-not $AlreadyRunning) {
            try { Stop-Service VolturaAirDev -ErrorAction Stop }
            catch { throw 'Database readiness failed and cleanup could not stop VolturaAirDev. Stop it in Windows Services and inspect my.ini.' }
        }
        throw 'Database readiness failed. Preserve credentials and data; inspect service configuration and connectivity.'
    }
}

function Get-SetupDatabaseState {
    param([string]$Path, [string]$InstallRoot, [string]$UserSid)
    if (-not (Test-Path -LiteralPath $Path)) {
        if (Test-Path -LiteralPath $InstallRoot) { throw 'An unowned MariaDB installation already exists. Setup will not overwrite it.' }
        $state = [pscustomobject]@{ owner = 'voltura-air-local-database-v1'; root = $InstallRoot; user = $UserSid; phase = 'installing'; partial = '' }
        Write-SetupAtomicFile $Path ($state | ConvertTo-Json)
        return $state
    }
    $state = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($state.owner -ne 'voltura-air-local-database-v1' -or $state.root -ine $InstallRoot -or $state.user -ne $UserSid -or $state.phase -notin @('installing', 'binaries', 'initializing', 'preserving', 'initialized', 'registering', 'ready')) { throw 'Database setup state is unrecognized. Preserve it for inspection.' }
    if ($state.partial -and $state.partial -notmatch '^data\.partial-[a-f0-9]{32}$') { throw 'Unexpected partial database path.' }
    return $state
}

function Save-SetupDatabasePhase {
    param($State, [string]$Path, [string]$Phase)
    $State.phase = $Phase
    Write-SetupAtomicFile $Path ($State | ConvertTo-Json)
}

function Repair-SetupPartialDatabase {
    param($State, [string]$StatePath)
    $data = Join-Path $State.root 'data'
    if ($State.phase -eq 'initializing') {
        $State.partial = 'data.partial-' + [Guid]::NewGuid().ToString('N')
        Save-SetupDatabasePhase $State $StatePath 'preserving'
    }
    if ($State.phase -eq 'preserving') {
        $backup = Join-Path $State.root $State.partial
        Assert-SetupPlainDirectory $data
        Assert-SetupPlainDirectory $backup
        if ((Test-Path -LiteralPath $data) -and (Test-Path -LiteralPath $backup)) { throw 'Both partial database locations exist. Preserve both for inspection.' }
        if (Test-Path -LiteralPath $data) { [IO.Directory]::Move($data, $backup) }
        # Partial initialization is retained under the installation root, never deleted automatically.
        Save-SetupDatabasePhase $State $StatePath 'binaries'
    }
}

Export-ModuleMember -Function *-Setup*
