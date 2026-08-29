#requires -Version 7.6 -PSEdition Core

param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [switch]$SkipBuild,
    [switch]$FrameworkDependentOnly,
    [switch]$NoInstallerCompression
)

$ErrorActionPreference = "Stop"

if ($Runtime -cne "win-x64") {
    throw "Runtime '$Runtime' is not supported. Use win-x64."
}

function Assert-LastExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-ScreenWebRtcPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory,
        [switch]$RequireDotNetRuntimeNotices
    )

    $requiredRelativePaths = @(
        "datachannel.dll",
        "ThirdPartyNotices\README.txt",
        "ThirdPartyNotices\managed\Microsoft.Web.WebView2-LICENSE.txt",
        "ThirdPartyNotices\managed\Microsoft.Web.WebView2-NOTICE.txt",
        "ThirdPartyNotices\managed\Markdig-LICENSE.txt",
        "ThirdPartyNotices\managed\QRCoder-LICENSE.txt",
        "ThirdPartyNotices\managed\Vortice-SharpGen-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\libdatachannel-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\libjuice-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\libsrtp-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\nlohmann-json-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\openssl-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\plog-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\usrsctp-LICENSE.txt",
        "ThirdPartyNotices\libdatachannel\SOURCE.txt",
        "PhoneWebcam\VolturaAir.WebcamSetup.exe",
        "PhoneWebcam\MICROSOFT-WINDOWS-CAMERA-LICENSE.txt",
        "wwwroot\third-party-notices.txt"
    )
    if ($RequireDotNetRuntimeNotices) {
        $requiredRelativePaths += @(
            "ThirdPartyNotices\dotnet-LICENSE.txt",
            "ThirdPartyNotices\dotnet-ThirdPartyNotices.txt"
        )
    }
    foreach ($relativePath in $requiredRelativePaths) {
        $requiredPath = Join-Path $PublishDirectory $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Expected Screen WebRTC release file was not found: $requiredPath"
        }
    }
}

function Copy-PhoneWebcamPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NativeOutputDirectory,
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $destination = Join-Path $PublishDirectory "PhoneWebcam"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $NativeOutputDirectory "VolturaAir.WebcamSetup.exe") -Destination $destination -Force
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "apps\windows-host\Features\PhoneWebcam\Native\MICROSOFT-WINDOWS-CAMERA-LICENSE.txt") `
        -Destination $destination `
        -Force
}

function Copy-DotNetRuntimeNotices {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetRoot = Split-Path -Parent $dotnetCommand.Source
    $licensePath = Join-Path $dotnetRoot "LICENSE.txt"
    $noticesPath = Join-Path $dotnetRoot "ThirdPartyNotices.txt"
    foreach ($requiredPath in @($licensePath, $noticesPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Expected .NET redistribution notice was not found: $requiredPath"
        }
    }

    $noticeDirectory = Join-Path $PublishDirectory "ThirdPartyNotices"
    New-Item -ItemType Directory -Force -Path $noticeDirectory | Out-Null
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $noticeDirectory "dotnet-LICENSE.txt") -Force
    Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $noticeDirectory "dotnet-ThirdPartyNotices.txt") -Force
}

function Write-InstallerPayloadManifest {
    param([Parameter(Mandatory = $true)][string]$PublishDirectory)
    $manifestPath = Join-Path $PublishDirectory 'installer-payload.sha256'
    $lines = Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($PublishDirectory, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash *$relative"
        }
    [IO.File]::WriteAllLines($manifestPath, $lines, [Text.UTF8Encoding]::new($false))
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageJsonPath = Join-Path $repoRoot "package.json"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$publishDir = Join-Path $publishRoot "VolturaAir-$Runtime"
$frameworkDependentPublishDir = Join-Path $publishRoot "VolturaAir-$Runtime-framework-dependent"
$zipPath = Join-Path $publishRoot "VolturaAir-$Version-$Runtime.zip"
$installerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime-full.exe"
$frameworkDependentInstallerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime.exe"
$phoneWebcamNativeOutput = Join-Path $repoRoot "artifacts\native\PhoneWebcam"
$nsisScript = Join-Path $repoRoot "installer\VolturaAir.nsi"
$nsisCompressionArguments = @()
if ($NoInstallerCompression) {
    $nsisCompressionArguments = @("/DTEST_NO_INSTALLER_COMPRESSION")
    $testOutputRoot = Join-Path $repoRoot "artifacts\test"
    $installerPath = Join-Path $testOutputRoot "VolturaAir-Setup-$Version-$Runtime-full-test-uncompressed.exe"
    $frameworkDependentInstallerPath = Join-Path $testOutputRoot "VolturaAir-Setup-$Version-$Runtime-test-uncompressed.exe"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content $packageJsonPath -Raw | ConvertFrom-Json).version
    $zipPath = Join-Path $publishRoot "VolturaAir-$Version-$Runtime.zip"
    $installerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime-full.exe"
    $frameworkDependentInstallerPath = Join-Path $publishRoot "VolturaAir-Setup-$Version-$Runtime.exe"
    if ($NoInstallerCompression) {
        $testOutputRoot = Join-Path $repoRoot "artifacts\test"
        $installerPath = Join-Path $testOutputRoot "VolturaAir-Setup-$Version-$Runtime-full-test-uncompressed.exe"
        $frameworkDependentInstallerPath = Join-Path $testOutputRoot "VolturaAir-Setup-$Version-$Runtime-test-uncompressed.exe"
    }
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
New-Item -ItemType Directory -Force -Path (Split-Path $installerPath -Parent) | Out-Null

if (-not $SkipBuild) {
    if (-not $FrameworkDependentOnly -and (Test-Path $publishDir)) {
        Remove-Item $publishDir -Recurse -Force
    }
    if (Test-Path $frameworkDependentPublishDir) {
        Remove-Item $frameworkDependentPublishDir -Recurse -Force
    }

    Push-Location $repoRoot
    try {
        npm run build --workspace apps/mobile-web
        Assert-LastExitCode "Mobile web build"

        if (-not $FrameworkDependentOnly) {
            dotnet publish apps/windows-host/VolturaAir.Host.csproj `
                -c Release `
                -r $Runtime `
                -p:RestoreLockedMode=true `
                -p:NuGetLockFilePath=packages.self-contained.lock.json `
                --self-contained true `
                -p:PublishSingleFile=true `
                -p:IncludeNativeLibrariesForSelfExtract=true `
                -o $publishDir
            Assert-LastExitCode "Self-contained host publish"

            & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File scripts/build-cursor-watchdog.ps1 `
                -OutputPath (Join-Path $publishDir "VolturaAir.CursorWatchdog.exe")
            Assert-LastExitCode "Self-contained cursor watchdog build"
            Copy-DotNetRuntimeNotices -PublishDirectory $publishDir
        }

        dotnet publish apps/windows-host/VolturaAir.Host.csproj `
            -c Release `
            -r $Runtime `
            -p:RestoreLockedMode=true `
            -p:NuGetLockFilePath=packages.framework-dependent.lock.json `
            --self-contained false `
            -p:PublishSingleFile=false `
            -o $frameworkDependentPublishDir
        Assert-LastExitCode "Framework-dependent host publish"

        $frameworkDependentWatchdogPath = Join-Path $frameworkDependentPublishDir "VolturaAir.CursorWatchdog.exe"
        if ($FrameworkDependentOnly) {
            & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File scripts/build-cursor-watchdog.ps1 `
                -OutputPath $frameworkDependentWatchdogPath
            Assert-LastExitCode "Framework-dependent cursor watchdog build"
        }
        else {
            Copy-Item `
                -Path (Join-Path $publishDir "VolturaAir.CursorWatchdog.exe") `
                -Destination $frameworkDependentWatchdogPath `
                -Force
        }

        & (Join-Path $repoRoot "scripts\build-phone-webcam-native.ps1") -OutputDirectory $phoneWebcamNativeOutput
        Assert-LastExitCode "Phone webcam native build"
        Copy-PhoneWebcamPayload `
            -NativeOutputDirectory $phoneWebcamNativeOutput `
            -PublishDirectory $frameworkDependentPublishDir
        if (-not $FrameworkDependentOnly) {
            Copy-PhoneWebcamPayload `
                -NativeOutputDirectory $phoneWebcamNativeOutput `
                -PublishDirectory $publishDir
        }
    }
    finally {
        Pop-Location
    }
}

if (-not $FrameworkDependentOnly) {
    $publishedExe = Join-Path $publishDir "VolturaAir.Host.exe"
    if (-not (Test-Path $publishedExe)) {
        throw "Expected published executable was not found: $publishedExe"
    }

    $publishedWatchdogExe = Join-Path $publishDir "VolturaAir.CursorWatchdog.exe"
    if (-not (Test-Path $publishedWatchdogExe)) {
        throw "Expected published cursor watchdog executable was not found: $publishedWatchdogExe"
    }
    Assert-ScreenWebRtcPayload -PublishDirectory $publishDir -RequireDotNetRuntimeNotices
}

$frameworkDependentHostExe = Join-Path $frameworkDependentPublishDir "VolturaAir.Host.exe"
if (-not (Test-Path $frameworkDependentHostExe)) {
    throw "Expected framework-dependent host executable was not found: $frameworkDependentHostExe"
}

$frameworkDependentWatchdogExe = Join-Path $frameworkDependentPublishDir "VolturaAir.CursorWatchdog.exe"
if (-not (Test-Path $frameworkDependentWatchdogExe)) {
    throw "Expected framework-dependent cursor watchdog executable was not found: $frameworkDependentWatchdogExe"
}
Assert-ScreenWebRtcPayload -PublishDirectory $frameworkDependentPublishDir
Write-InstallerPayloadManifest -PublishDirectory $frameworkDependentPublishDir
if (-not $FrameworkDependentOnly) {
    Write-InstallerPayloadManifest -PublishDirectory $publishDir
}

$frameworkDependentInstalledSizeBytes = (Get-ChildItem $frameworkDependentPublishDir -Recurse -File | Measure-Object Length -Sum).Sum
$frameworkDependentInstalledSizeKb = [int][math]::Ceiling($frameworkDependentInstalledSizeBytes / 1KB)

if (-not $FrameworkDependentOnly) {
    $installedSizeBytes = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum
    $installedSizeKb = [int][math]::Ceiling($installedSizeBytes / 1KB)

    if (-not $NoInstallerCompression -and (Test-Path $zipPath)) {
        Remove-Item $zipPath -Force
    }
    if (Test-Path $installerPath) {
        Remove-Item $installerPath -Force
    }
}
if (Test-Path $frameworkDependentInstallerPath) {
    Remove-Item $frameworkDependentInstallerPath -Force
}

if (-not $FrameworkDependentOnly) {
    if (-not $NoInstallerCompression) {
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    }

    & $makensisPath `
        "/WX" `
        "/DAPP_VERSION=$Version" `
        "/DAPP_VERSION_QUAD=$appVersionQuad" `
        "/DAPP_ESTIMATED_SIZE_KB=$installedSizeKb" `
        "/DRUNTIME=$Runtime" `
        "/DPUBLISH_DIR=$publishDir" `
        "/DOUTPUT_FILE=$installerPath" `
        @nsisCompressionArguments `
        $nsisScript

    if ($LASTEXITCODE -ne 0) {
        throw "makensis failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $installerPath)) {
        throw "Expected installer was not created: $installerPath"
    }
}

& $makensisPath `
    "/WX" `
    "/DAPP_VERSION=$Version" `
    "/DAPP_VERSION_QUAD=$appVersionQuad" `
    "/DAPP_ESTIMATED_SIZE_KB=$frameworkDependentInstalledSizeKb" `
    "/DRUNTIME=$Runtime" `
    "/DPUBLISH_DIR=$frameworkDependentPublishDir" `
    "/DOUTPUT_FILE=$frameworkDependentInstallerPath" `
    "/DFRAMEWORK_DEPENDENT" `
    @nsisCompressionArguments `
    $nsisScript

if ($LASTEXITCODE -ne 0) {
    throw "Framework-dependent installer compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $frameworkDependentInstallerPath)) {
    throw "Expected framework-dependent installer was not created: $frameworkDependentInstallerPath"
}

$verifyVersionScript = Join-Path $repoRoot "scripts\verify-windows-version.ps1"
if (-not $FrameworkDependentOnly) {
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
}

$frameworkDependentVerifyVersionArguments = @{
    Version = $Version
    Runtime = $Runtime
    PublishDir = $frameworkDependentPublishDir
    InstallerPath = $frameworkDependentInstallerPath
}
if (-not $SkipBuild) {
    $frameworkDependentVerifyVersionArguments.RequireHostDll = $true
}

& $verifyVersionScript @frameworkDependentVerifyVersionArguments

if (-not $FrameworkDependentOnly) {
    if (-not $NoInstallerCompression) {
        Write-Host "Created portable zip: $zipPath"
    }
    Write-Host "Created full installer: $installerPath"
}
Write-Host "Created framework-dependent installer: $frameworkDependentInstallerPath"
