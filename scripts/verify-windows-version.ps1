[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$PublishDir,
    [string]$InstallerPath,
    [switch]$RequireHostDll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semVerPattern) {
    throw "Version '$Version' is not a supported semantic version."
}

$versionCore = ($Version -split '[+-]', 2)[0]
$windowsVersion = "$versionCore.0"
foreach ($part in @($versionCore -split '\.')) {
    if ([int64]$part -gt 65535) {
        throw "Version '$Version' cannot be used for Windows version resources. Each numeric part must be between 0 and 65535."
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Resolve-ReleasePath {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)][string]$DefaultRelativePath
    )

    $resolvedPath = $Path
    if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
        $resolvedPath = $DefaultRelativePath
    }

    if (-not [System.IO.Path]::IsPathRooted($resolvedPath)) {
        $resolvedPath = Join-Path $repoRoot $resolvedPath
    }

    return [System.IO.Path]::GetFullPath($resolvedPath)
}

function Get-FixedFileVersion {
    param([Parameter(Mandatory = $true)][System.Diagnostics.FileVersionInfo]$VersionInfo)

    return "$($VersionInfo.FileMajorPart).$($VersionInfo.FileMinorPart).$($VersionInfo.FileBuildPart).$($VersionInfo.FilePrivatePart)"
}

function Assert-VersionMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ExpectedFileVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedDescription,
        [string]$ExpectedInternalName,
        [string]$ExpectedOriginalFilename
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $fixedFileVersion = Get-FixedFileVersion $versionInfo
    $actualFileVersion = ([string]$versionInfo.FileVersion).Trim()
    $actualProductVersion = ([string]$versionInfo.ProductVersion).Trim()
    $actualProductName = ([string]$versionInfo.ProductName).Trim()
    $actualCompanyName = ([string]$versionInfo.CompanyName).Trim()
    $actualDescription = ([string]$versionInfo.FileDescription).Trim()
    $actualInternalName = ([string]$versionInfo.InternalName).Trim()
    $actualOriginalFilename = ([string]$versionInfo.OriginalFilename).Trim()

    if ($fixedFileVersion -ne $windowsVersion) {
        throw "$Label fixed Windows version '$fixedFileVersion' does not match '$windowsVersion': $Path"
    }
    if ($actualFileVersion -ne $ExpectedFileVersion) {
        throw "$Label file version '$actualFileVersion' does not match '$ExpectedFileVersion': $Path"
    }
    if ($actualProductVersion -ne $Version) {
        throw "$Label product version '$actualProductVersion' does not match '$Version': $Path"
    }
    if ($actualProductName -ne 'Voltura Air') {
        throw "$Label product name '$actualProductName' does not match 'Voltura Air': $Path"
    }
    if ($actualCompanyName -ne 'Voltura AB') {
        throw "$Label company '$actualCompanyName' does not match 'Voltura AB': $Path"
    }
    if ($actualDescription -ne $ExpectedDescription) {
        throw "$Label description '$actualDescription' does not match '$ExpectedDescription': $Path"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedInternalName) -and $actualInternalName -ne $ExpectedInternalName) {
        throw "$Label internal name '$actualInternalName' does not match '$ExpectedInternalName': $Path"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedOriginalFilename) -and $actualOriginalFilename -ne $ExpectedOriginalFilename) {
        throw "$Label original filename '$actualOriginalFilename' does not match '$ExpectedOriginalFilename': $Path"
    }

    Write-Host "Validated $Label metadata: $Path"
}

$resolvedPublishDir = Resolve-ReleasePath `
    -Path $PublishDir `
    -DefaultRelativePath "artifacts\publish\VolturaAir-$Runtime"
$resolvedInstallerPath = Resolve-ReleasePath `
    -Path $InstallerPath `
    -DefaultRelativePath "artifacts\publish\VolturaAir-Setup-$Version-$Runtime.exe"

$hostExePath = Join-Path $resolvedPublishDir 'VolturaAir.Host.exe'
Assert-VersionMetadata `
    -Path $hostExePath `
    -Label 'published host EXE' `
    -ExpectedFileVersion $windowsVersion `
    -ExpectedDescription 'Voltura Air'

$watchdogExePath = Join-Path $resolvedPublishDir 'VolturaAir.CursorWatchdog.exe'
Assert-VersionMetadata `
    -Path $watchdogExePath `
    -Label 'native cursor watchdog EXE' `
    -ExpectedFileVersion $windowsVersion `
    -ExpectedDescription 'Voltura Air' `
    -ExpectedInternalName 'VolturaAir.CursorWatchdog' `
    -ExpectedOriginalFilename 'VolturaAir.CursorWatchdog.exe'

$hostDllCandidates = @(
    (Join-Path $repoRoot "apps\windows-host\bin\Release\net10.0-windows\$Runtime\VolturaAir.Host.dll"),
    (Join-Path $repoRoot "apps\windows-host\bin\Release\net10.0-windows\VolturaAir.Host.dll"),
    (Join-Path $resolvedPublishDir 'VolturaAir.Host.dll')
) | Select-Object -Unique

$hostDllPath = $hostDllCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($hostDllPath)) {
    $fallbackDll = Get-ChildItem `
        -LiteralPath (Join-Path $repoRoot 'apps\windows-host\bin\Release') `
        -Filter 'VolturaAir.Host.dll' `
        -File `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -ne $fallbackDll) {
        $hostDllPath = $fallbackDll.FullName
    }
}

if ([string]::IsNullOrWhiteSpace($hostDllPath)) {
    if ($RequireHostDll) {
        throw "The Release build output VolturaAir.Host.dll was not found for metadata validation."
    }

    Write-Warning "VolturaAir.Host.dll was not found; DLL metadata validation was skipped."
}
else {
    Assert-VersionMetadata `
        -Path $hostDllPath `
        -Label 'host DLL' `
        -ExpectedFileVersion $windowsVersion `
        -ExpectedDescription 'Voltura Air'
}

Assert-VersionMetadata `
    -Path $resolvedInstallerPath `
    -Label 'NSIS installer EXE' `
    -ExpectedFileVersion $Version `
    -ExpectedDescription 'Voltura Air Installer'
