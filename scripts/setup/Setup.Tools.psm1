Import-Module (Join-Path $PSScriptRoot 'Setup.Common.psm1')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SetupVisualStudio {
    param($Manifest)
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }
    $components = @($Manifest.visualStudio.components | Where-Object { $_ -notmatch '\.Workload\.' })
    $arguments = @('-latest', '-products', '*', '-version', $Manifest.visualStudio.range, '-requires') + $components + @('-property', 'installationPath')
    $location = Invoke-SetupCommand $vswhere $arguments -Capture
    if (-not $location) { return $null }
    foreach ($relative in @('MSBuild\Current\Bin\MSBuild.exe', 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe', 'Common7\Tools\VsDevCmd.bat')) {
        if (-not (Test-Path -LiteralPath (Join-Path $location $relative))) { return $null }
    }
    return $location
}

function Get-SetupNpm {
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $cli = Join-Path (Split-Path -Parent $node) 'node_modules\npm\bin\npm-cli.js'
    $manifest = Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'toolchain.json') -Raw | ConvertFrom-Json
    $candidates = @($env:npm_execpath)
    $candidates += @(Get-Command npm.cmd -All -ErrorAction SilentlyContinue | ForEach-Object { Join-Path (Split-Path -Parent $_.Source) 'node_modules\npm\bin\npm-cli.js' })
    $candidates += $cli
    foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $actual = Invoke-SetupCommand $node @($candidate, '--version') -Capture
        if (Test-SetupVersion $actual $manifest.tools.npm) { return @{ Node = $node; Cli = $candidate } }
    }
    if (-not (Test-Path -LiteralPath $cli)) { throw 'The selected Node installation does not contain npm. Repair the Node installation.' }
    return @{ Node = $node; Cli = $cli }
}

function Invoke-SetupNpm {
    param([string[]]$Arguments, [switch]$Capture)
    $npm = Get-SetupNpm
    Invoke-SetupCommand $npm.Node (@($npm.Cli) + $Arguments) -Capture:$Capture
}

function Initialize-SetupTools {
    param([string]$Root, [switch]$CheckOnly)
    $manifest = Get-Content (Join-Path $Root 'scripts\toolchain.json') -Raw | ConvertFrom-Json
    $failures = [Collections.Generic.List[string]]::new()
    $selected = [Collections.Generic.List[object]]::new()
    foreach ($name in @('git', 'powershell', 'node', 'dotnet', 'php', 'nsis', 'github')) {
        $requirement = $manifest.tools.$name
        try {
            $tool = Get-SetupTool $name $requirement
            if (-not $tool -and -not $CheckOnly) {
                Install-SetupPackage $requirement
                $tool = Get-SetupTool $name $requirement
            }
            if (-not $tool) { throw "Missing supported $name ($($requirement.minimum), $($requirement.policy))." }
            Set-SetupToolPath $tool.Path -CheckOnly:$CheckOnly
            $selected.Add($tool)
            Write-Host "$name $($tool.Version): $($tool.Path)"
        } catch { $failures.Add($_.Exception.Message) }
    }
    try {
        $visualStudio = Get-SetupVisualStudio $manifest
        if (-not $visualStudio -and -not $CheckOnly) {
            $installerOptions = '--wait --passive --norestart'
            foreach ($component in $manifest.visualStudio.components) { $installerOptions += " --add $component" }
            $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
            $existing = if (Test-Path -LiteralPath $vswhere) { Invoke-SetupCommand $vswhere @('-latest', '-products', '*', '-version', $manifest.visualStudio.range, '-property', 'installationPath') -Capture } else { '' }
            if ($existing) {
                $arguments = @('modify', '--installPath', $existing, '--passive', '--norestart')
                foreach ($component in $manifest.visualStudio.components) { $arguments += @('--add', $component) }
                Invoke-SetupElevated (Join-Path (Split-Path -Parent $vswhere) 'setup.exe') $arguments
            } else { Install-SetupPackage $manifest.tools.visualStudio @('--override', $installerOptions) }
            $visualStudio = Get-SetupVisualStudio $manifest
        }
        if (-not $visualStudio) { throw 'Visual Studio 2026 C++ tools, v143, Windows SDK 26100, and bundled CMake are required.' }
        $selected.Add(@{ Name = "visualStudio"; Path = $visualStudio })
        Write-Host "Visual Studio: $visualStudio"
    } catch { $failures.Add($_.Exception.Message) }
    try {
        $npmVersion = Invoke-SetupNpm @('--version') -Capture
        if (-not (Test-SetupVersion $npmVersion $manifest.tools.npm)) {
            if ($CheckOnly) { throw 'The selected Node installation needs npm 12.0.x (12.0.2 or newer).' }
            # npm is installed beside the selected Node executable, not into another user's prefix.
            $npm = Get-SetupNpm
            Invoke-SetupElevated $npm.Node @($npm.Cli, 'install', '--global', '--prefix', (Split-Path -Parent $npm.Node), "npm@$($manifest.tools.npm.minimum)", '--no-audit', '--no-fund')
            $npmVersion = Invoke-SetupNpm @('--version') -Capture
            if (-not (Test-SetupVersion $npmVersion $manifest.tools.npm)) { throw 'npm installation did not satisfy the supported version.' }
        }
        $selected.Add(@{ Name = "npm"; Version = $npmVersion; Path = (Get-SetupNpm).Cli })
        Write-Host "npm $(Invoke-SetupNpm @('--version') -Capture)"
    } catch { $failures.Add($_.Exception.Message) }
    try {
        $runtimes = Invoke-SetupCommand 'dotnet.exe' @('--list-runtimes') -Capture
        foreach ($runtime in @('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App', 'Microsoft.WindowsDesktop.App')) {
            $found = @($runtimes -split "`r?`n" | Where-Object { $_.StartsWith("$runtime ") -and (Test-SetupVersion ($_ -split ' ')[1] $manifest.tools.runtime) })
            if (-not $found.Count) {
                if ($CheckOnly) { throw "Missing $runtime $($manifest.tools.runtime.minimum)+ on the supported patch line." }
                $packages = @{ 'Microsoft.NETCore.App' = 'Microsoft.DotNet.Runtime.10'; 'Microsoft.AspNetCore.App' = 'Microsoft.DotNet.AspNetCore.10'; 'Microsoft.WindowsDesktop.App' = 'Microsoft.DotNet.DesktopRuntime.10' }
                Install-SetupPackage ([pscustomobject]@{ minimum = $manifest.tools.runtime.minimum; policy = $manifest.tools.runtime.policy; package = $packages[$runtime] })
                $runtimes = Invoke-SetupCommand 'dotnet.exe' @('--list-runtimes') -Capture
                if (-not @($runtimes -split "`r?`n" | Where-Object { $_.StartsWith("$runtime ") -and (Test-SetupVersion ($_ -split ' ')[1] $manifest.tools.runtime) }).Count) { throw "Installing $runtime did not satisfy the required version." }
            }
        }
    } catch { $failures.Add($_.Exception.Message) }
    if (-not $CheckOnly) { Write-SetupReport tools @{ selected = @($selected.ToArray()); failures = @($failures.ToArray()) } }
    if ($failures.Count) { throw ($failures -join "`n") }
}

Export-ModuleMember -Function Get-SetupVisualStudio, Get-SetupNpm, Invoke-SetupNpm, Initialize-SetupTools
