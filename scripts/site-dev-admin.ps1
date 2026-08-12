#requires -Version 7.6 -PSEdition Core

param(
    [Parameter(Position = 0)]
    [string]$Email
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$devRoot = Join-Path $repoRoot '.site-dev'
$configPath = Join-Path $devRoot 'config.php'
$phpIniPath = Join-Path $devRoot 'php.ini'
if (-not (Test-Path -LiteralPath $configPath) -or -not (Test-Path -LiteralPath $phpIniPath)) {
    throw 'Local site development is not initialized. Run npm run site:dev:init first.'
}

if ([string]::IsNullOrWhiteSpace($Email)) {
    $Email = Read-Host 'Email of the registered local catalog account'
}
try {
    $Email = ([Net.Mail.MailAddress]$Email).Address.ToLowerInvariant()
} catch {
    throw 'Enter a valid email address.'
}

$php = Get-Command php.exe -ErrorAction SilentlyContinue
if (-not $php) { $php = Get-Command php -ErrorAction SilentlyContinue }
if (-not $php) {
    $wingetPhp = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\php.exe'
    if (Test-Path -LiteralPath $wingetPhp) { $php = Get-Item -LiteralPath $wingetPhp }
}
if (-not $php) { throw 'PHP could not be found. Run npm run site:dev:init again.' }
$phpPath = if ($php.Source) { $php.Source } else { $php.FullName }

$env:VOLTURA_AIR_ADMIN_EMAIL = $Email
try {
    & $phpPath -c $phpIniPath (Join-Path $PSScriptRoot 'site-dev-admin.php') $configPath
    if ($LASTEXITCODE -eq 2) { throw "No registered local catalog account uses $Email. Create the account first, then rerun this command." }
    if ($LASTEXITCODE -ne 0) { throw 'The local catalog account could not be promoted.' }
} finally {
    Remove-Item Env:VOLTURA_AIR_ADMIN_EMAIL -ErrorAction SilentlyContinue
}

Write-Host "$Email is now a local catalog administrator. Sign out and sign in again to refresh the session."
