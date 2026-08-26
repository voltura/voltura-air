#requires -Version 7.6 -PSEdition Core

[CmdletBinding()]
param(
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-WindowlessSetupHelper {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [string[]]$Arguments = @()
    )

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $FilePath
    foreach ($argument in $Arguments) { $process.StartInfo.ArgumentList.Add($argument) }
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    try {
        if (-not $process.Start()) {
            throw "The windowless Phone webcam setup helper could not be started for verification."
        }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $output.GetAwaiter().GetResult()
            ErrorOutput = $errorOutput.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$nativeRoot = Join-Path $repoRoot "apps\windows-host\Features\PhoneWebcam\Native"
$mediaSourceProject = Join-Path $nativeRoot "VirtualCameraMediaSource\VirtualCameraMediaSource.vcxproj"
$setupProject = Join-Path $nativeRoot "SetupHelper\SetupHelper.vcxproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\native\PhoneWebcam"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$intermediateRoot = Join-Path $repoRoot "artifacts\obj\PhoneWebcam"
$mediaSourceOutput = Join-Path $intermediateRoot "MediaSourceOutput"
$setupOutput = Join-Path $intermediateRoot "SetupOutput"

$msbuildCandidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
$msbuildPath = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuildPath)) {
    throw "Visual Studio 2022 MSBuild with the C++ desktop workload is required to build Phone webcam."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $intermediateRoot | Out-Null
New-Item -ItemType Directory -Force -Path $mediaSourceOutput | Out-Null
New-Item -ItemType Directory -Force -Path $setupOutput | Out-Null

& $msbuildPath $mediaSourceProject /nologo /t:restore /p:RestorePackagesConfig=true
if ($LASTEXITCODE -ne 0) { throw "Phone webcam native package restore failed with exit code $LASTEXITCODE." }

$common = @(
    "/nologo",
    "/m",
    "/p:Configuration=Release",
    "/p:Platform=x64"
)
& $msbuildPath $mediaSourceProject @common "/p:OutDir=$mediaSourceOutput\" "/p:IntDir=$intermediateRoot\MediaSource\"
if ($LASTEXITCODE -ne 0) { throw "Phone webcam media source build failed with exit code $LASTEXITCODE." }

$mediaSourcePath = Join-Path $mediaSourceOutput "VirtualCameraMediaSource.dll"
$setupIntermediate = Join-Path $intermediateRoot "SetupHelper"
New-Item -ItemType Directory -Force -Path $setupIntermediate | Out-Null
$componentRevision = & (Join-Path $PSScriptRoot "get-phone-webcam-component-revision.ps1") `
    -NativeRoot $nativeRoot `
    -BuildScriptPath $PSCommandPath
if ($LASTEXITCODE -ne 0 -or $componentRevision -notmatch '^[0-9a-f]{64}$') {
    throw "The Phone webcam component revision could not be computed."
}
$revisionFile = Join-Path $setupIntermediate "ComponentRevision.generated.txt"
[System.IO.File]::WriteAllText(
    $revisionFile,
    $componentRevision,
    [System.Text.UTF8Encoding]::new($false))
$resourceFile = Join-Path $setupIntermediate "EmbeddedMediaSource.generated.rc"
$resourceLiteral = $mediaSourcePath.Replace("\", "/")
$revisionLiteral = $revisionFile.Replace("\", "/")
[System.IO.File]::WriteAllText(
    $resourceFile,
    "#define IDR_MEDIA_SOURCE 101`r`n#define IDR_COMPONENT_REVISION 102`r`nIDR_MEDIA_SOURCE RCDATA `"$resourceLiteral`"`r`nIDR_COMPONENT_REVISION RCDATA `"$revisionLiteral`"`r`n",
    [System.Text.UTF8Encoding]::new($false))
& $msbuildPath $setupProject @common "/p:OutDir=$setupOutput\" "/p:IntDir=$setupIntermediate\" "/p:PhoneWebcamResourceFile=$resourceFile"
if ($LASTEXITCODE -ne 0) { throw "Phone webcam setup helper build failed with exit code $LASTEXITCODE." }

$builtSetupHelper = Join-Path $setupOutput "VolturaAir.WebcamSetup.exe"
$required = @($mediaSourcePath, $builtSetupHelper)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected Phone webcam native output was not found: $path"
    }
}

$setupHelper = Join-Path $OutputDirectory "VolturaAir.WebcamSetup.exe"
[System.IO.File]::Copy($builtSetupHelper, $setupHelper, $true)
$setupBytes = [System.IO.File]::ReadAllBytes($setupHelper)
$peOffset = [BitConverter]::ToInt32($setupBytes, 0x3c)
$subsystem = [BitConverter]::ToUInt16($setupBytes, $peOffset + 24 + 68)
if ($subsystem -ne 2) {
    throw "The Phone webcam setup helper must use the Windows GUI subsystem so setup and uninstall never open a terminal window."
}
$status = Invoke-WindowlessSetupHelper -FilePath $setupHelper -Arguments @("status")
if ($status.ExitCode -notin 0, 1 -or $status.Output -notmatch '^\{"installed":') {
    throw "The windowless Phone webcam setup helper did not preserve its status output contract."
}
$revision = Invoke-WindowlessSetupHelper -FilePath $setupHelper -Arguments @("revision")
if ($revision.ExitCode -ne 0 -or $revision.Output.Trim() -ne $componentRevision) {
    throw "The Phone webcam setup helper did not preserve its deterministic component revision."
}
$replacementBackup = Join-Path $setupIntermediate "VirtualCameraMediaSource.security-test-backup.dll"
try {
    [System.IO.File]::Move($mediaSourcePath, $replacementBackup, $true)
    [System.IO.File]::WriteAllBytes($mediaSourcePath, [byte[]](0x4d, 0x5a, 0x00, 0x00))
    $verification = Invoke-WindowlessSetupHelper `
        -FilePath $setupHelper `
        -Arguments @("verify-packaged-source", $replacementBackup)
    if ($verification.ExitCode -ne 0) {
        throw "The embedded Phone webcam payload changed after the replaceable sibling DLL was substituted."
    }
}
finally {
    if (Test-Path -LiteralPath $mediaSourcePath -PathType Leaf) {
        [System.IO.File]::Delete($mediaSourcePath)
    }
    if (Test-Path -LiteralPath $replacementBackup -PathType Leaf) {
        [System.IO.File]::Move($replacementBackup, $mediaSourcePath, $true)
    }
}

$legacyExternalPayload = Join-Path $OutputDirectory "VirtualCameraMediaSource.dll"
if (Test-Path -LiteralPath $legacyExternalPayload -PathType Leaf) {
    [System.IO.File]::Delete($legacyExternalPayload)
}

Write-Host "Built Phone webcam native payload: $OutputDirectory"
