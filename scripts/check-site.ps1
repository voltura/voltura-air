#requires -Version 7.6 -PSEdition Core

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$php = (Get-Command php -ErrorAction Stop).Source
$phpIni = Join-Path $repoRoot '.site-dev\php.ini'
$phpArguments = if (Test-Path -LiteralPath $phpIni -PathType Leaf) { @('-c', $phpIni) } else { @() }
$minimumPhp = [version](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json).tools.php.minimum
$version = & $php @phpArguments -r 'echo PHP_VERSION;'
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$' -or
    ([version]$version).Major -ne $minimumPhp.Major -or ([version]$version).Minor -ne $minimumPhp.Minor -or
    [version]$version -lt $minimumPhp) {
    throw "PHP $minimumPhp or a newer patch on the same line is required; found '$version'."
}

& $php @phpArguments -r 'foreach (["PDO", "pdo_mysql", "session", "zip"] as $extension) { if (!extension_loaded($extension)) { fwrite(STDERR, "Missing PHP extension: $extension\n"); exit(1); } }'
if ($LASTEXITCODE -ne 0) { throw 'Required PHP extensions are unavailable.' }

$phpFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'apps\public-site') -Recurse -Filter '*.php' -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Recurse -Filter '*.php' -File
    Get-Item -LiteralPath (Join-Path $repoRoot 'scripts\site-dev-admin.php')
)
foreach ($file in $phpFiles) {
    & $php @phpArguments -l $file.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "PHP syntax validation failed: $($file.FullName)" }
}

Write-Host "Site check passed: PHP $version and $($phpFiles.Count) PHP files."
