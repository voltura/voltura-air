Import-Module (Join-Path $PSScriptRoot 'Setup.Common.psm1')
Import-Module (Join-Path $PSScriptRoot 'Setup.Storage.psm1')
Import-Module (Join-Path $PSScriptRoot 'Setup.DatabaseState.psm1')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Initialize-SetupDatabase {
    param([string]$Root, [int]$Port = 3306)
    if ($Port -ne 3306) { throw 'Automatic MariaDB installation uses port 3306. Configure an existing instance explicitly for another port.' }
    $storage = Join-Path $env:LOCALAPPDATA 'Voltura Air\Setup\MariaDB'
    New-SetupPrivateDirectory $storage
    $credential = Join-Path $storage 'root.dpapi'
    $manifest = Get-Content (Join-Path $Root 'scripts\toolchain.json') -Raw | ConvertFrom-Json
    $service = Get-Service VolturaAirDev -ErrorAction SilentlyContinue
    if ($service -and -not (Test-Path -LiteralPath $credential)) { throw "VolturaAirDev exists without this account's setup credential. Restore its credential; setup will not replace the database." }
    if ($service -and $service.Status -eq 'Running' -and (Test-Path -LiteralPath (Join-Path $storage 'state.json'))) {
        $state = Get-Content (Join-Path $storage 'state.json') -Raw | ConvertFrom-Json
        if ($state.owner -eq 'voltura-air-local-database-v1' -and $state.phase -eq 'ready' -and $state.user -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) {
            $installRoot = Join-Path $env:ProgramFiles 'VolturaAirDev-MariaDB'
            Assert-SetupPlainDirectory $installRoot
            if ($state.root -ine $installRoot) { throw 'Managed database ownership path changed.' }
            $config = Join-Path $installRoot 'data\my.ini'
            $expected = '"' + (Join-Path $installRoot 'bin\mysqld.exe') + '" "--defaults-file=' + $config + '" "VolturaAirDev"'
            $actual = Get-CimInstance Win32_Service -Filter "Name='VolturaAirDev'"
            if ($actual.PathName -ine $expected) { throw 'Managed database service command changed.' }
            $ini = Get-Content -LiteralPath $config -Raw
            Assert-SetupDatabaseConfiguration $ini $installRoot
            Assert-SetupDatabaseListener $actual.ProcessId
            return [Net.NetworkCredential]::new('', (Get-Content -LiteralPath $credential -Raw | ConvertTo-SecureString)).Password
        }
    }
    if (-not $service) {
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'Port 3306 is occupied. Configure the existing local database; setup will not replace it or switch ports.' }
        if (-not (Test-Path -LiteralPath $credential)) {
            $password = New-SetupPassword
            $secure = [Security.SecureString]::new()
            foreach ($character in $password.ToCharArray()) { $secure.AppendChar($character) }
            Write-SetupAtomicFile $credential (ConvertFrom-SecureString $secure)
            $password = $null
        }
        $version = Get-SetupPackageVersion $manifest.tools.mariadb
        $download = Join-Path $storage "download-$version"
        New-SetupPrivateDirectory $download
        & winget.exe download --id $manifest.tools.mariadb.package --exact --version $version --source winget --architecture x64 --download-directory $download --skip-dependencies --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Host
        Assert-SetupInstallExit $LASTEXITCODE
        $packages = @(Get-ChildItem -LiteralPath $download -Filter '*.msi' -File)
        if ($packages.Count -ne 1) { throw 'Expected exactly one verified MariaDB MSI download.' }
        $package = $packages[0].FullName
    } else { $package = '' }
    # Only paths cross elevation. DPAPI is decrypted only by the initiating identity.
    $helper = Join-Path $PSScriptRoot 'Install-Database.ps1'
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $values = @($helper, $storage, $package, $sid) | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }
    $command = "& $($values[0]) -Storage $($values[1]) -Package $($values[2]) -UserSid $($values[3])"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    Assert-SetupInstallExit $process.ExitCode
    $secure = Get-Content -LiteralPath $credential -Raw | ConvertTo-SecureString
    return [Net.NetworkCredential]::new('', $secure).Password
}

Export-ModuleMember -Function Initialize-SetupDatabase
