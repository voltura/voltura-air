[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semVerPattern) {
    throw "Version '$Version' is not a supported semantic version. Use a value such as 0.3.0 or 0.3.0-beta.1."
}

$versionCore = ($Version -split '[+-]', 2)[0]
$windowsVersion = "$versionCore.0"
$versionParts = @($versionCore -split '\.')
foreach ($part in $versionParts) {
    if ([int64]$part -gt 65535) {
        throw "Version '$Version' cannot be used for Windows version resources. Each numeric part must be between 0 and 65535."
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$originals = [ordered]@{}
$updates = [ordered]@{}

function Get-RepoPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
}

function Get-RepoText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ($updates.Contains($RelativePath)) {
        return [string]$updates[$RelativePath]
    }

    if (-not $originals.Contains($RelativePath)) {
        $path = Get-RepoPath $RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required release file was not found: $RelativePath"
        }

        $originals[$RelativePath] = [System.IO.File]::ReadAllText($path)
    }

    return [string]$originals[$RelativePath]
}

function Set-RepoText {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $updates[$RelativePath] = $Content
}

function Set-RegexValue {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$NewValue,
        [Parameter(Mandatory = $true)][int]$ExpectedCount,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $text = Get-RepoText $RelativePath
    $regex = [System.Text.RegularExpressions.Regex]::new(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline
    )
    $matches = $regex.Matches($text)

    if ($matches.Count -ne $ExpectedCount) {
        throw "Expected $ExpectedCount $Description value(s) in '$RelativePath', but found $($matches.Count). The release script must be updated for the current file structure."
    }

    $replacement = [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return $match.Groups['prefix'].Value + $NewValue + $match.Groups['suffix'].Value
    }

    Set-RepoText $RelativePath ($regex.Replace($text, $replacement))
}

$rootPackagePath = 'package.json'
$mobilePackagePath = 'apps\mobile-web\package.json'
$packageLockPath = 'package-lock.json'
$hostProjectPath = 'apps\windows-host\VolturaAir.Host.csproj'
$releaseWorkflowPath = '.github\workflows\release-zip.yml'

$rootPackage = Get-RepoText $rootPackagePath | ConvertFrom-Json
$currentVersion = [string]$rootPackage.version
if ([string]::IsNullOrWhiteSpace($currentVersion)) {
    throw "The current version could not be read from package.json."
}

Set-RegexValue `
    -RelativePath $rootPackagePath `
    -Pattern '(?<prefix>^[ \t]*"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,?[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'root package version'

Set-RegexValue `
    -RelativePath $mobilePackagePath `
    -Pattern '(?<prefix>^[ \t]*"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,?[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'mobile package version'

Set-RegexValue `
    -RelativePath $packageLockPath `
    -Pattern '(?<prefix>^(?<indent>[ \t]*)"name"[ \t]*:[ \t]*"voltura-air"[ \t]*,[ \t]*\r?\n\k<indent>"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 2 `
    -Description 'root package-lock version'

Set-RegexValue `
    -RelativePath $packageLockPath `
    -Pattern '(?<prefix>^(?<indent>[ \t]*)"name"[ \t]*:[ \t]*"@voltura-air/mobile-web"[ \t]*,[ \t]*\r?\n\k<indent>"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'mobile package-lock version'

Set-RegexValue `
    -RelativePath $hostProjectPath `
    -Pattern '(?<prefix><Version>)[^<\r\n]+(?<suffix></Version>)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description '.NET host package version'

Set-RegexValue `
    -RelativePath $hostProjectPath `
    -Pattern '(?<prefix><AssemblyVersion>)[^<\r\n]+(?<suffix></AssemblyVersion>)' `
    -NewValue $windowsVersion `
    -ExpectedCount 1 `
    -Description '.NET assembly version'

Set-RegexValue `
    -RelativePath $hostProjectPath `
    -Pattern '(?<prefix><FileVersion>)[^<\r\n]+(?<suffix></FileVersion>)' `
    -NewValue $windowsVersion `
    -ExpectedCount 1 `
    -Description 'Windows file version'

Set-RegexValue `
    -RelativePath $hostProjectPath `
    -Pattern '(?<prefix><InformationalVersion>)[^<\r\n]+(?<suffix></InformationalVersion>)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description '.NET informational version'

Set-RegexValue `
    -RelativePath $releaseWorkflowPath `
    -Pattern '(?<prefix>^      release_tag:[ \t]*\r?\n(?:^        [^\r\n]*\r?\n)*?^        default:[ \t]*)[^\r\n]+(?<suffix>[ \t]*\r?$)' `
    -NewValue "v$Version" `
    -ExpectedCount 1 `
    -Description 'release workflow tag default'

Set-RegexValue `
    -RelativePath $releaseWorkflowPath `
    -Pattern '(?<prefix>^      version:[ \t]*\r?\n(?:^        [^\r\n]*\r?\n)*?^        default:[ \t]*)[^\r\n]+(?<suffix>[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'release workflow version default'

$updatedRootPackage = Get-RepoText $rootPackagePath | ConvertFrom-Json
$updatedMobilePackage = Get-RepoText $mobilePackagePath | ConvertFrom-Json
$updatedPackageLock = Get-RepoText $packageLockPath | ConvertFrom-Json
$updatedHostProject = [xml](Get-RepoText $hostProjectPath)

if ([string]$updatedRootPackage.version -ne $Version) {
    throw "package.json did not validate with version '$Version'."
}
if ([string]$updatedMobilePackage.version -ne $Version) {
    throw "apps/mobile-web/package.json did not validate with version '$Version'."
}
if ([string]$updatedPackageLock.version -ne $Version) {
    throw "package-lock.json top-level version did not validate with version '$Version'."
}
if ([string]$updatedPackageLock.packages.PSObject.Properties[''].Value.version -ne $Version) {
    throw "package-lock.json root package entry did not validate with version '$Version'."
}
if ([string]$updatedPackageLock.packages.'apps/mobile-web'.version -ne $Version) {
    throw "package-lock.json mobile workspace entry did not validate with version '$Version'."
}
if ([string]$updatedHostProject.Project.PropertyGroup.Version -ne $Version) {
    throw "VolturaAir.Host.csproj did not validate with version '$Version'."
}
if ([string]$updatedHostProject.Project.PropertyGroup.AssemblyVersion -ne $windowsVersion) {
    throw "VolturaAir.Host.csproj did not validate with assembly version '$windowsVersion'."
}
if ([string]$updatedHostProject.Project.PropertyGroup.FileVersion -ne $windowsVersion) {
    throw "VolturaAir.Host.csproj did not validate with file version '$windowsVersion'."
}
if ([string]$updatedHostProject.Project.PropertyGroup.InformationalVersion -ne $Version) {
    throw "VolturaAir.Host.csproj did not validate with informational version '$Version'."
}

$updatedWorkflow = Get-RepoText $releaseWorkflowPath
$escapedReleaseTag = [regex]::Escape("v$Version")
$escapedVersion = [regex]::Escape($Version)
if ($updatedWorkflow -notmatch "(?m)^        default: $escapedReleaseTag\s*$") {
    throw "release-zip.yml did not validate with release tag 'v$Version'."
}
if ($updatedWorkflow -notmatch "(?m)^        default: $escapedVersion\s*$") {
    throw "release-zip.yml did not validate with version '$Version'."
}

$writtenPaths = New-Object System.Collections.Generic.List[string]
try {
    foreach ($relativePath in $updates.Keys) {
        $updatedText = [string]$updates[$relativePath]
        $originalText = [string]$originals[$relativePath]
        if ($updatedText -eq $originalText) {
            continue
        }

        $writtenPaths.Add($relativePath)
        [System.IO.File]::WriteAllText((Get-RepoPath $relativePath), $updatedText, $utf8NoBom)
    }
}
catch {
    foreach ($relativePath in $writtenPaths) {
        [System.IO.File]::WriteAllText(
            (Get-RepoPath $relativePath),
            [string]$originals[$relativePath],
            $utf8NoBom
        )
    }

    throw
}

Write-Host "Prepared Voltura Air version $Version (previously $currentVersion)."
if ($writtenPaths.Count -eq 0) {
    Write-Host "All release version files were already synchronized."
}
else {
    Write-Host "Updated:"
    foreach ($relativePath in $writtenPaths) {
        Write-Host "  $relativePath"
    }
}

Write-Host ""
Write-Host "Next verification steps:"
Write-Host "  npm run build"
Write-Host "  npm test"
Write-Host "  npm run package:win"
