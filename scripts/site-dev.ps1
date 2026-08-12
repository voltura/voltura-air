#requires -Version 7.6 -PSEdition Core

param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 8765
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$devRoot = Join-Path $repoRoot '.site-dev'
$configPath = Join-Path $devRoot 'config.php'
$phpIniPath = Join-Path $devRoot 'php.ini'
if (-not (Test-Path -LiteralPath $configPath) -or -not (Test-Path -LiteralPath $phpIniPath)) {
    throw 'Local site development is not initialized. Run npm run site:dev:init first.'
}

$php = Get-Command php.exe -ErrorAction SilentlyContinue
if (-not $php) { $php = Get-Command php -ErrorAction SilentlyContinue }
if (-not $php) {
    $wingetPhp = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\php.exe'
    if (Test-Path -LiteralPath $wingetPhp) { $php = Get-Item -LiteralPath $wingetPhp }
}
if (-not $php) { throw 'PHP could not be found. Run npm run site:dev:init again.' }
$phpPath = if ($php.Source) { $php.Source } else { $php.FullName }

$env:VOLTURA_AIR_SCREENS_CONFIG = $configPath
$env:VOLTURA_AIR_SITE_DEV = '1'
$url = "http://127.0.0.1:$Port/"
Write-Host "Voltura Air site: $url"
Write-Host 'Press Ctrl+C to stop.'
& $phpPath -c $phpIniPath -S "127.0.0.1:$Port" -t (Join-Path $repoRoot 'apps\public-site')
