#requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckOnly, [switch]$PrerequisitesOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'setup\Setup.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'setup\Setup.Tools.psm1') -Force
$setupLock = $null
try {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Setup requires Windows 11 x64 and a 64-bit PowerShell process.' }
    $build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if ($build -lt 22000) { throw 'Setup requires Windows 11.' }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $CheckOnly -and $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Start setup from a normal, non-administrator terminal. Setup requests installation elevation separately and keeps credentials under your account.'
    }
    if (-not $CheckOnly) { $setupLock = Enter-SetupLock }
    Push-Location $root
    try {
        Initialize-SetupTools $root -CheckOnly:$CheckOnly
        if ($PrerequisitesOnly) { Write-Host 'Machine prerequisites ready.'; exit 0 }
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'setup\Configure-Windows.ps1'))
        if ($CheckOnly) { $arguments += '-CheckOnly' }
        Invoke-SetupCommand 'pwsh.exe' $arguments
    } finally { Pop-Location }
} catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
} finally { if ($setupLock) { $setupLock.ReleaseMutex(); $setupLock.Dispose() } }
