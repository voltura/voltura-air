#requires -Version 7.6 -PSEdition Core

[CmdletBinding()]
param(
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$nativeRoot = Join-Path $repoRoot "apps\windows-host\Features\PhoneWebcam\Native"
$mediaSourceProject = Join-Path $nativeRoot "VirtualCameraMediaSource\VirtualCameraMediaSource.vcxproj"
$setupProject = Join-Path $nativeRoot "SetupHelper\SetupHelper.vcxproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\native\PhoneWebcam"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$intermediateRoot = Join-Path $repoRoot "artifacts\obj\PhoneWebcam"

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

& $msbuildPath $mediaSourceProject /nologo /t:restore /p:RestorePackagesConfig=true
if ($LASTEXITCODE -ne 0) { throw "Phone webcam native package restore failed with exit code $LASTEXITCODE." }

$common = @(
    "/nologo",
    "/m",
    "/p:Configuration=Release",
    "/p:Platform=x64",
    "/p:OutDir=$OutputDirectory\"
)
& $msbuildPath $mediaSourceProject @common "/p:IntDir=$intermediateRoot\MediaSource\"
if ($LASTEXITCODE -ne 0) { throw "Phone webcam media source build failed with exit code $LASTEXITCODE." }

$mediaSourcePath = Join-Path $OutputDirectory "VirtualCameraMediaSource.dll"
$setupIntermediate = Join-Path $intermediateRoot "SetupHelper"
New-Item -ItemType Directory -Force -Path $setupIntermediate | Out-Null
$resourceFile = Join-Path $setupIntermediate "EmbeddedMediaSource.generated.rc"
$resourceLiteral = $mediaSourcePath.Replace("\", "/")
[System.IO.File]::WriteAllText(
    $resourceFile,
    "#define IDR_MEDIA_SOURCE 101`r`nIDR_MEDIA_SOURCE RCDATA `"$resourceLiteral`"`r`n",
    [System.Text.UTF8Encoding]::new($false))
& $msbuildPath $setupProject @common "/p:IntDir=$setupIntermediate\" "/p:PhoneWebcamResourceFile=$resourceFile"
if ($LASTEXITCODE -ne 0) { throw "Phone webcam setup helper build failed with exit code $LASTEXITCODE." }

$required = @(
    (Join-Path $OutputDirectory "VirtualCameraMediaSource.dll"),
    (Join-Path $OutputDirectory "VolturaAir.WebcamSetup.exe")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected Phone webcam native output was not found: $path"
    }
}

$setupHelper = Join-Path $OutputDirectory "VolturaAir.WebcamSetup.exe"
$replacementBackup = "$mediaSourcePath.security-test-backup"
try {
    [System.IO.File]::Move($mediaSourcePath, $replacementBackup, $true)
    [System.IO.File]::WriteAllBytes($mediaSourcePath, [byte[]](0x4d, 0x5a, 0x00, 0x00))
    & $setupHelper verify-packaged-source $replacementBackup
    if ($LASTEXITCODE -ne 0) {
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

Write-Host "Built Phone webcam native payload: $OutputDirectory"
