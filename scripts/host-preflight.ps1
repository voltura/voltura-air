#requires -Version 7.6 -PSEdition Core

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Voltura Air'))
$hostProcesses = Get-Process -Name 'VolturaAir.Host' -ErrorAction SilentlyContinue

foreach ($hostProcess in @($hostProcesses)) {
    $executablePath = $hostProcess.Path
    if ([string]::IsNullOrWhiteSpace($executablePath)) {
        throw "Could not verify the executable path for VolturaAir.Host process $($hostProcess.Id)."
    }

    $resolvedPath = [IO.Path]::GetFullPath($executablePath)
    $isRepositoryHost = $resolvedPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    $isInstalledHost = $resolvedPath.StartsWith($installedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    if (-not $isRepositoryHost -and -not $isInstalledHost) {
        throw "Refusing to stop VolturaAir.Host process $($hostProcess.Id) from unexpected path '$resolvedPath'."
    }

    Stop-Process -Id $hostProcess.Id -Force
    Wait-Process -Id $hostProcess.Id -Timeout 10 -ErrorAction SilentlyContinue
    if (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue) {
        throw "VolturaAir.Host process $($hostProcess.Id) did not stop within 10 seconds."
    }
}
