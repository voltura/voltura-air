# Shared bootstrap code must run in inbox Windows PowerShell 5.1.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-SetupVersion {
    param([string]$Actual, $Requirement)
    if ($Actual -notmatch '^v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.\d+)?$') { return $false }
    $major = [int]$Matches[1]; $minor = [int]$Matches[2]
    $patch = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
    $version = [version]"$major.$minor.$patch"
    $minimum = [version]$Requirement.minimum
    if ($version -lt $minimum) { return $false }
    switch ($Requirement.policy) {
        'minimum' { return $true }
        'major-line' { return $major -eq $minimum.Major }
        'patch-line' { return $major -eq $minimum.Major -and $minor -eq $minimum.Minor }
        'feature-band' { return $major -eq $minimum.Major -and $minor -eq $minimum.Minor -and [math]::Floor($patch / 100) -eq [math]::Floor($minimum.Build / 100) }
        default { throw 'Unknown toolchain version policy.' }
    }
}

function Update-SetupPath {
    $entries = @($env:Path -split ';') + @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') + @([Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';')
    $env:Path = (@($entries | Where-Object { $_ } | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') } | Select-Object -Unique) -join ';')
}

function Invoke-SetupCommand {
    param([string]$File, [string[]]$Arguments = @(), [switch]$Capture)
    if ($Capture) {
        $output = & $File @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Command failed: $([IO.Path]::GetFileName($File)) (exit $LASTEXITCODE)." }
        return ($output -join "`n").Trim()
    }
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $([IO.Path]::GetFileName($File)) (exit $LASTEXITCODE)." }
}

function Get-SetupTool {
    param([string]$Name, $Requirement)
    $candidates = @((Get-Command $Requirement.command -CommandType Application -All -ErrorAction SilentlyContinue | ForEach-Object Source))
    $candidates += switch ($Name) {
        'node' { "$env:ProgramFiles\nodejs\node.exe" }
        'dotnet' { "$env:ProgramFiles\dotnet\dotnet.exe" }
        'powershell' { "$env:ProgramFiles\PowerShell\7\pwsh.exe" }
        'php' {
            "$env:LOCALAPPDATA\Microsoft\WinGet\Links\php.exe"
            Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\PHP.PHP.8.5_*" -Filter php.exe -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object FullName
        }
        'nsis' { "$env:ProgramFiles\NSIS\makensis.exe"; "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
        'github' { "$env:ProgramFiles\GitHub CLI\gh.exe" }
        'git' { "$env:ProgramFiles\Git\cmd\git.exe" }
        'mariadb' { Get-ChildItem "$env:ProgramFiles\MariaDB *\bin\mariadb.exe" -File -ErrorAction SilentlyContinue | ForEach-Object FullName }
    }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            $output = Invoke-SetupCommand $candidate $Requirement.arguments -Capture
            if ($Name -eq 'git') { $output = $output -replace '\.windows\.\d+', '' }
            $pattern = if ($Name -eq 'mariadb') { 'Distrib (\d+\.\d+\.\d+)' } else { '(?<![\d.])v?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)(?![\w.-])' }
            if ($output -match $pattern -and (Test-SetupVersion $Matches[1] $Requirement)) {
                return [pscustomobject]@{ Name = $Name; Path = $candidate; Version = $Matches[1] }
            }
        } catch { continue }
    }
    return $null
}

function Set-SetupToolPath {
    param([string]$Path, [switch]$CheckOnly)
    $directory = Split-Path -Parent $Path
    $env:Path = "$directory;$env:Path"
    if ($CheckOnly) { return }
    $entries = @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $directory.TrimEnd('\') })
    [Environment]::SetEnvironmentVariable('Path', (@($directory) + $entries -join ';'), 'User')
}

function Get-SetupPackageVersion {
    param($Requirement)
    $output = Invoke-SetupCommand 'winget.exe' @('show', '--id', $Requirement.package, '--exact', '--source', 'winget', '--versions', '--disable-interactivity', '--accept-source-agreements') -Capture
    $versions = @($output -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { Test-SetupVersion $_ $Requirement } | Sort-Object { [version]($_ -replace '^v', '') } -Descending)
    if (-not $versions.Count) { throw "No supported version of $($Requirement.package) is available from winget. Expected $($Requirement.minimum), $($Requirement.policy)." }
    return $versions[0]
}

function Assert-SetupInstallExit {
    param([int]$Code)
    # WinGet's documented reboot results wrap installer codes as HRESULTs.
    if ($Code -in @(3010, 1641, -1978334967, -1978334966, -1978334965)) { throw 'Installation requires a Windows restart. Restart, then rerun the same setup command.' }
    if ($Code -ne 0) { throw "Installation failed or was cancelled (exit $Code). Rerun setup after resolving the installer error." }
}

function Install-SetupPackage {
    param($Requirement, [string[]]$Extra = @())
    if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'Install Microsoft App Installer (WinGet), then rerun setup.' }
    $version = Get-SetupPackageVersion $Requirement
    Write-Host "Installing $($Requirement.package) $version..."
    & winget.exe install --id $Requirement.package --exact --version $version --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements @Extra
    Assert-SetupInstallExit $LASTEXITCODE
    Update-SetupPath
}

function Invoke-SetupElevated {
    param([string]$File, [string[]]$Arguments)
    $nativeArguments = $Arguments | ForEach-Object { '"' + (($_ -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' }
    $quotedFile = "'" + $File.Replace("'", "''") + "'"
    $quotedArguments = "'" + ($nativeArguments -join ' ').Replace("'", "''") + "'"
    # Start-Process -Wait follows a GUI installer's process tree; '& setup.exe' can return before completion.
    $command = '$process = Start-Process -FilePath ' + $quotedFile + ' -ArgumentList ' + $quotedArguments + ' -WindowStyle Hidden -Wait -PassThru; exit $process.ExitCode'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    Assert-SetupInstallExit $process.ExitCode
}

function Write-SetupReport {
    param([string]$Name, $Results)
    if ($Name -notin @('tools', 'local', 'release')) { throw 'Unknown setup report.' }
    $directory = Join-Path $env:LOCALAPPDATA 'Voltura Air\Setup\Reports'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    # Reports contain only stage names, tool paths/versions and status, never process output.
    $report = @{ checkedAt = [DateTime]::UtcNow.ToString('o'); results = $Results }
    [IO.File]::WriteAllText((Join-Path $directory "$Name.json"), ($report | ConvertTo-Json -Depth 6))
}

function Enter-SetupLock {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex = [Threading.Mutex]::new($false, "Local\VolturaAirMachineSetup-$sid")
    try {
        try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another setup is running for this Windows account. Wait for it to finish.' }
        return $mutex
    } catch { $mutex.Dispose(); throw }
}

Export-ModuleMember -Function *-Setup*
