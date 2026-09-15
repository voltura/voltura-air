#requires -Version 7.6 -PSEdition Core
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $root 'scripts/setup/Setup.Common.psm1') -Force
Import-Module (Join-Path $root 'scripts/setup/Setup.Storage.psm1') -Force
Import-Module (Join-Path $root 'scripts/setup/Setup.DatabaseState.psm1') -Force

Import-Module (Join-Path $root 'scripts/setup/Setup.SiteConfiguration.psm1') -Force

function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Assert-Fails([scriptblock]$Action, [string]$Pattern) {
    try { & $Action; throw 'Expected failure was not raised.' } catch { if ($_.Exception.Message -notmatch $Pattern) { throw } }
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('voltura-setup-tests-' + [Guid]::NewGuid().ToString('N'))
New-SetupPrivateDirectory $temporary
New-SetupPrivateDirectory $temporary
try {
    $requirement = [pscustomobject]@{ minimum = '7.6.6'; policy = 'patch-line'; package = 'fixture' }
    foreach ($version in @('7.6.6', '7.6.9')) { Assert-True (Test-SetupVersion $version $requirement) "Rejected $version" }
    foreach ($version in @('7.6.5', '7.7.0', '8.0.0', '7.6.6-preview.1', '7.6.6.1-preview', 'unknown')) { Assert-True (-not (Test-SetupVersion $version $requirement)) "Accepted $version" }
    $common = Get-Module Setup.Common
    & $common {
        function script:Invoke-SetupCommand { return "Name Version`n-----`n7.7.0`n7.6.8`n7.6.7-preview.1`n7.6.6" }
    }
    Assert-True ((Get-SetupPackageVersion $requirement) -eq '7.6.8') 'Package selection crossed the allowed version line.'
    & $common { function script:Invoke-SetupCommand { return '7.7.0' } }
    Assert-Fails { Get-SetupPackageVersion $requirement } 'No supported version'
    foreach ($code in @(3010,1641,-1978334967,-1978334966,-1978334965)) { Assert-Fails { Assert-SetupInstallExit $code } 'restart' }
    foreach ($code in @(1602,1603,-1,-1978334964)) { Assert-Fails { Assert-SetupInstallExit $code } 'failed or was cancelled' }
    Assert-SetupInstallExit 0
    Import-Module (Join-Path $root 'scripts/setup/Setup.Common.psm1') -Force

    $common = Get-Module Setup.Common
    $oldTool = Join-Path $temporary 'old node.exe'
    $supportedTool = Join-Path $temporary 'supported node.exe'
    [IO.File]::WriteAllText($oldTool, '')
    [IO.File]::WriteAllText($supportedTool, '')
    & $common {
        param($Old, $Supported)
        $script:oldFixture = $Old; $script:supportedFixture = $Supported
        function script:Get-Command { @([pscustomobject]@{ Source = $script:oldFixture }, [pscustomobject]@{ Source = $script:supportedFixture }) }
        function script:Invoke-SetupCommand { param($File) if ($File -eq $script:oldFixture) { 'v22.0.0' } else { 'v24.20.0' } }
    } $oldTool $supportedTool
    $found = Get-SetupTool node ([pscustomobject]@{ command = 'node.exe'; arguments = @('--version'); minimum = '24.20.0'; policy = 'major-line' })
    Assert-True ($found.Path -eq $supportedTool) 'PATH shadowing selected an unsupported executable.'
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $processPath = $env:Path
    Set-SetupToolPath $supportedTool -CheckOnly
    Assert-True ([Environment]::GetEnvironmentVariable('Path', 'User') -ceq $userPath) 'CheckOnly persisted PATH.'
    $env:Path = $processPath
    & $common { function script:Start-Process { throw 'Elevation cancelled by test user.' } }
    Assert-Fails { Invoke-SetupElevated $supportedTool @('with spaces') } 'Elevation cancelled'
    Import-Module (Join-Path $root 'scripts/setup/Setup.Common.psm1') -Force

    $configuration = Join-Path $temporary 'config with spaces.php'
    Write-SetupAtomicFile $configuration 'original'
    $locked = [IO.File]::Open($configuration, 'Open', 'Read', 'None')
    try { Assert-Fails { Write-SetupAtomicFile $configuration 'replacement' } 'being used|access|process' } finally { $locked.Dispose() }
    Assert-True ((Get-Content $configuration -Raw) -eq 'original') 'Failed commit changed the original.'
    Assert-True (-not (Test-Path "$configuration.setup-pending")) 'Failed commit left a pending file.'
    Write-SetupAtomicFile $configuration 'replacement'
    Assert-True ((Get-Content $configuration -Raw) -eq 'replacement') 'Atomic replacement failed.'
    [IO.File]::WriteAllText("$configuration.setup-pending", 'interrupted write')
    Assert-Fails { Write-SetupAtomicFile $configuration 'unexpected' } 'Unfinished configuration'
    Assert-True ((Get-Content $configuration -Raw) -eq 'replacement') 'Interrupted-write recovery replaced the destination.'
    [IO.File]::Delete("$configuration.setup-pending")

    $savedConfig = "<?php return ['dsn' => 'mysql:host=127.0.0.1;port=3307;dbname=voltura_air_dev;charset=utf8mb4', 'username' => 'voltura_air_dev', 'password' => 'custom\'password\\value'];"
    Write-SetupAtomicFile $configuration $savedConfig
    $saved = Read-SetupSiteConfiguration $configuration 3306 $false
    Assert-True ($saved.Port -eq 3307 -and $saved.Password -ceq "custom'password\value") 'Existing port or password was not preserved.'
    Assert-Fails { Read-SetupSiteConfiguration $configuration 3306 $true } 'requested port differs'
    Assert-True ((Get-Content $configuration -Raw) -ceq $savedConfig) 'Configuration validation changed existing credentials.'

    $installRoot = Join-Path $temporary 'Managed Database'
    $canonicalIni = "[mysqld]`nbasedir=$($installRoot.Replace('\','/'))`ndatadir=$($installRoot.Replace('\','/'))/data`nbind-address=127.0.0.1`nport=3306`n"
    Assert-SetupDatabaseConfiguration $canonicalIni $installRoot
    Assert-SetupDatabaseConfiguration ($canonicalIni + "init-file=$($installRoot.Replace('\','/'))/data/setup-init.sql`n") $installRoot
    foreach ($unsafeIni in @(
        $canonicalIni + "bind-address=0.0.0.0`n",
        $canonicalIni + "!include=other.ini`n",
        $canonicalIni + "[server]`nbind-address=0.0.0.0`n",
        $canonicalIni.Replace('port=3306', 'port=3307'),
        $canonicalIni.Replace('/data', '/other-data')
    )) { Assert-Fails { Assert-SetupDatabaseConfiguration $unsafeIni $installRoot } 'configuration changed' }
    $databaseModule = Get-Module Setup.DatabaseState
    & $databaseModule {
        $script:listenerFixture = @([pscustomobject]@{ LocalAddress = '127.0.0.1'; LocalPort = 3306 })
        $script:stopCount = 0; $script:stopFails = $false; $script:authenticationFails = $false
        function script:Get-NetTCPConnection { $script:listenerFixture }
        function script:Start-Service { }
        function script:Get-Service { [pscustomobject]@{} | Add-Member ScriptMethod WaitForStatus {} -PassThru }
        function script:Get-CimInstance { [pscustomobject]@{ ProcessId = 42 } }
        function script:Invoke-SetupSql { [pscustomobject]@{ ExitCode = [int]$script:authenticationFails } }
        function script:Stop-Service { $script:stopCount++; if ($script:stopFails) { throw 'fixture stop failure' } }
    }
    Start-SetupDatabaseService 'fixture' 'fixture-secret' $false
    Assert-True ((& $databaseModule { $script:stopCount }) -eq 0) 'Successful readiness stopped the service.'
    & $databaseModule { $script:listenerFixture[0].LocalAddress = '0.0.0.0' }
    Assert-Fails { Start-SetupDatabaseService 'fixture' 'fixture-secret' $false } 'Database readiness failed'
    Assert-True ((& $databaseModule { $script:stopCount }) -eq 1) 'Listener mismatch did not stop the newly started service.'
    Assert-Fails { Start-SetupDatabaseService 'fixture' 'fixture-secret' $true } 'Database readiness failed'
    Assert-True ((& $databaseModule { $script:stopCount }) -eq 1) 'Inspection stopped a previously running service.'
    & $databaseModule { $script:stopFails = $true }
    Assert-Fails { Start-SetupDatabaseService 'fixture' 'fixture-secret' $false } 'cleanup could not stop'
    & $databaseModule { $script:stopFails = $false; $script:listenerFixture = @() }
    Assert-Fails { Assert-SetupDatabaseListener 42 } 'must listen only'
    & $databaseModule { $script:listenerFixture = @([pscustomobject]@{ LocalAddress = '127.0.0.1'; LocalPort = 3306 }); $script:authenticationFails = $true }
    Assert-Fails { Start-SetupDatabaseService 'fixture' 'fixture-secret' $false } 'Database readiness failed'
    Import-Module (Join-Path $root 'scripts/setup/Setup.DatabaseState.psm1') -Force
    $statePath = Join-Path $temporary 'state.json'
    $state = Get-SetupDatabaseState $statePath $installRoot 'test-user'
    Assert-True ($state.phase -eq 'installing') 'Missing durable installation stage.'
    [IO.Directory]::CreateDirectory($installRoot) | Out-Null
    $resumed = Get-SetupDatabaseState $statePath $installRoot 'test-user'
    Assert-True ($resumed.phase -eq 'installing') 'MSI reboot stage was not resumable.'
    Assert-Fails { Get-SetupDatabaseState $statePath $installRoot 'other-user' } 'unrecognized'
    Save-SetupDatabasePhase $state $statePath 'initializing'
    $data = Join-Path $installRoot 'data'
    [IO.Directory]::CreateDirectory($data) | Out-Null
    [IO.File]::WriteAllText((Join-Path $data 'sentinel'), 'retain me')
    Repair-SetupPartialDatabase $state $statePath
    Assert-True ($state.phase -eq 'binaries') 'Partial initialization did not resume.'
    $backup = Join-Path $installRoot $state.partial
    Assert-True ((Get-Content (Join-Path $backup 'sentinel') -Raw) -eq 'retain me') 'Partial database data was lost.'
    Assert-True (-not (Test-Path $data)) 'Partial database was not moved out of initialization path.'
    # Retry after moving data but before the phase commit.
    Save-SetupDatabasePhase $state $statePath 'preserving'
    Repair-SetupPartialDatabase $state $statePath
    Assert-True (Test-Path $backup) 'Retry removed preserved data.'
    # Ambiguous rollback/restore state preserves both copies.
    Save-SetupDatabasePhase $state $statePath 'preserving'
    [IO.Directory]::CreateDirectory($data) | Out-Null
    Assert-Fails { Repair-SetupPartialDatabase $state $statePath } 'Both partial'
    Assert-True ((Test-Path $data) -and (Test-Path $backup)) 'Ambiguous recovery deleted data.'
    $unknown = Join-Path $temporary 'unowned'
    [IO.Directory]::CreateDirectory($unknown) | Out-Null
    Assert-Fails { Get-SetupDatabaseState (Join-Path $temporary 'other-state.json') $unknown 'test-user' } 'unowned'
    $acl = Get-Acl $temporary
    Assert-True $acl.AreAccessRulesProtected 'Credential directory inherits broad access.'
    Assert-True (-not @($acl.Access | Where-Object { $_.IdentityReference -match 'Everyone|BUILTIN\\Users' }).Count) 'Credential directory permits ordinary other users.'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Assert-Fails { Invoke-SetupSql 'node.exe' @('-e', 'setInterval(()=>{},1000)') ('x' * 2000000) 'fixture-secret' 150 } 'timed out'
    Assert-True ($clock.ElapsedMilliseconds -lt 5000) 'Stalled SQL input escaped the operation timeout.'
    $failure = Invoke-SetupSql 'node.exe' @('-e', 'process.stderr.write("Access denied: fixture-secret");process.exit(1)') '' 'fixture-secret'
    Assert-True ($failure.Error -eq 'Access denied') 'SQL error handling returned secret-bearing diagnostic text.'
    $success = Invoke-SetupSql 'node.exe' @('-e', 'process.stdin.resume();process.stdin.on("end",()=>process.stdout.write("1"))') 'SELECT 1;' 'fixture-secret'
    Assert-True ($success.ExitCode -eq 0 -and $success.Output -eq '1') 'SQL subprocess success contract failed.'
    Write-Host 'Setup boundary tests passed: version selection, installer exits, atomic writes, interrupted initialization, recovery conflicts, and ACLs.'
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved) -match '^voltura-setup-tests-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
