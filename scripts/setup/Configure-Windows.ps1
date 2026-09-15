#requires -Version 7.6 -PSEdition Core
[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $PSScriptRoot 'Setup.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Setup.Tools.psm1') -Force
$manifest = Get-Content (Join-Path $root 'scripts\toolchain.json') -Raw | ConvertFrom-Json
$stage = 'analyzer'
$passed = $false
Push-Location $root
try {
    $analyzer = Get-Module -ListAvailable PSScriptAnalyzer | Where-Object { Test-SetupVersion $_.Version.ToString() $manifest.tools.analyzer } | Select-Object -First 1
    if (-not $analyzer) {
        if ($CheckOnly) { throw 'PSScriptAnalyzer 1.25.x is missing.' }
        Install-PSResource PSScriptAnalyzer -Version $manifest.tools.analyzer.minimum -Scope CurrentUser -TrustRepository -Quiet
    }
    if ($CheckOnly) {
        Invoke-SetupNpm @('run', 'tools:check')
        Invoke-SetupNpm @('run', 'site:check')
        Invoke-SetupCommand 'pwsh.exe' @('-NoProfile', '-File', 'scripts/site-dev-init.ps1', '-CheckOnly')
        foreach ($file in @('node_modules/wrangler/bin/wrangler.js', 'node_modules/@playwright/test/cli.js')) {
            if (-not (Test-Path -LiteralPath $file)) { throw "Missing restored dependency: $file" }
        }
        Write-Host 'Local prerequisite inspection passed. Full build, database tests, and packaging have not been run by CheckOnly.'
        exit 0
    }
    $stage = 'locked-restores'
    Invoke-SetupNpm @('ci')
    Invoke-SetupCommand 'node.exe' @('node_modules/@playwright/test/cli.js', 'install', 'chromium')
    Invoke-SetupCommand 'dotnet.exe' @('restore', 'VolturaAir.slnx', '--locked-mode')
    $stage = 'database-initialization'
    Invoke-SetupCommand 'pwsh.exe' @('-NoProfile', '-File', 'scripts/site-dev-init.ps1', '-Automatic')
    $stage = 'host-preflight'
    Write-Host 'Stopping the verified Voltura Air host before build and isolated host tests.'
    Invoke-SetupCommand 'pwsh.exe' @('-NoProfile', '-File', 'scripts/host-preflight.ps1')
    $stage = 'public-build'
    Invoke-SetupNpm @('run', 'build')
    $stage = 'public-tests'
    Invoke-SetupNpm @('test')
    $stage = 'database-integration'
    foreach ($suite in @('test:site-import-integration', 'test:site-catalog-integration', 'test:site-telemetry-integration')) { Invoke-SetupNpm @('run', $suite) }
    $stage = 'windows-packaging'
    Invoke-SetupNpm @('run', 'package:win', '--', '-OutputDirectory', (Join-Path $root 'artifacts/setup-check'))
    $passed = $true
    Write-Host 'LOCAL VERIFICATION PASSED: build, aggregate tests, database integration, ZIP, and both installers.'
} finally {
    Pop-Location
    if (-not $CheckOnly) { Write-SetupReport local @{ passed = $passed; lastStage = $stage } }
}
