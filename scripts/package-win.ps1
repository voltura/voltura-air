param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageJsonPath = Join-Path $repoRoot "package.json"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$publishDir = Join-Path $publishRoot "VolturaAir-$Runtime"
$zipPath = Join-Path $publishRoot "VolturaAir-$Version-$Runtime.zip"
$installerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime.exe"
$nsisScript = Join-Path $repoRoot "installer\VolturaAir.nsi"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content $packageJsonPath -Raw | ConvertFrom-Json).version
    $zipPath = Join-Path $publishRoot "VolturaAir-$Version-$Runtime.zip"
    $installerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime.exe"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version was not provided and could not be read from package.json."
}

$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semVerPattern) {
    throw "Version '$Version' is not a supported semantic version. Use a value such as 0.3.0 or 0.3.0-beta.1."
}

$versionCore = ($Version -split "[+-]", 2)[0]
$versionSegments = @($versionCore -split "\.")
foreach ($segment in $versionSegments) {
    if ([int64]$segment -gt 65535) {
        throw "Version '$Version' cannot be used for Windows version resources. Each numeric part must be between 0 and 65535."
    }
}
$appVersionQuad = "$versionCore.0"

$makensis = Get-Command makensis -ErrorAction SilentlyContinue
$makensisPath = $null
if ($null -ne $makensis) {
    $makensisPath = $makensis.Source
}
if ([string]::IsNullOrWhiteSpace($makensisPath)) {
    $makensisCandidates = @(
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "$env:ProgramFiles\NSIS\makensis.exe"
    )
    $makensisPath = $makensisCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($makensisPath)) {
    throw "makensis was not found. Install NSIS 3.12 or later, then run this command again."
}

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

if (-not $SkipBuild) {
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    Push-Location $repoRoot
    try {
        npm run build --workspace apps/mobile-web

        dotnet publish apps/windows-host/VolturaAir.Host.csproj `
            -c Release `
            -r $Runtime `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $publishDir
    }
    finally {
        Pop-Location
    }
}

$publishedExe = Join-Path $publishDir "VolturaAir.Host.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Expected published executable was not found: $publishedExe"
}

$installedSizeBytes = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum
$installedSizeKb = [int][math]::Ceiling($installedSizeBytes / 1KB)

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
if (Test-Path $installerPath) {
    Remove-Item $installerPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

& $makensisPath `
    "/DAPP_VERSION=$Version" `
    "/DAPP_VERSION_QUAD=$appVersionQuad" `
    "/DAPP_ESTIMATED_SIZE_KB=$installedSizeKb" `
    "/DRUNTIME=$Runtime" `
    "/DPUBLISH_DIR=$publishDir" `
    "/DOUTPUT_FILE=$installerPath" `
    $nsisScript

if ($LASTEXITCODE -ne 0) {
    throw "makensis failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $installerPath)) {
    throw "Expected installer was not created: $installerPath"
}

$verifyVersionScript = Join-Path $repoRoot "scripts\verify-windows-version.ps1"
$verifyVersionArguments = @{
    Version = $Version
    Runtime = $Runtime
    PublishDir = $publishDir
    InstallerPath = $installerPath
}
if (-not $SkipBuild) {
    $verifyVersionArguments.RequireHostDll = $true
}

& $verifyVersionScript @verifyVersionArguments

Write-Host "Created portable zip: $zipPath"
Write-Host "Created installer: $installerPath"
