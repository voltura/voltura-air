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

function Assert-PackageLockVersions {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $nodeCommand) {
        throw "Node.js is required to validate package-lock.json. Run this command through npm from a Node.js-enabled shell."
    }

    $temporaryJsonPath = Join-Path ([System.IO.Path]::GetTempPath()) (
        "voltura-air-release-lock-{0}.json" -f [guid]::NewGuid().ToString('N')
    )
    $temporaryScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) (
        "voltura-air-release-lock-validator-{0}.cjs" -f [guid]::NewGuid().ToString('N')
    )

    try {
        [System.IO.File]::WriteAllText($temporaryJsonPath, $Content, $utf8NoBom)

        # Windows PowerShell 5.1 ConvertFrom-Json cannot reliably parse npm lockfiles
        # that contain property names differing only by case. Write a temporary Node
        # script instead of using node -e so PowerShell does not mangle multiline JS.
        $validationScript = @'
const fs = require("fs");
const lockPath = process.argv[2];
const expectedVersion = process.argv[3];

let lock;
try {
  lock = JSON.parse(fs.readFileSync(lockPath, "utf8"));
} catch (error) {
  console.log("package-lock.json is not valid JSON: " + error.message);
  process.exit(1);
}

const checks = [
  ["top-level version", lock.version],
  ["root package entry", lock.packages && lock.packages[""] && lock.packages[""].version],
  ["mobile workspace entry", lock.packages && lock.packages["apps/mobile-web"] && lock.packages["apps/mobile-web"].version]
];

let failed = false;
for (const check of checks) {
  const label = check[0];
  const actual = check[1];
  if (actual !== expectedVersion) {
    const displayActual = actual === undefined ? "<missing>" : actual;
    console.log(label + " is '" + displayActual + "', expected '" + expectedVersion + "'.");
    failed = true;
  }
}

if (failed) {
  process.exit(1);
}
'@
        [System.IO.File]::WriteAllText($temporaryScriptPath, $validationScript, $utf8NoBom)

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $nodeOutput = & $nodeCommand.Source $temporaryScriptPath $temporaryJsonPath $ExpectedVersion 2>&1
            $nodeExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if ($nodeExitCode -ne 0) {
            $details = ($nodeOutput | Out-String).Trim()
            if ([string]::IsNullOrWhiteSpace($details)) {
                $details = "Node exited with code $nodeExitCode."
            }

            throw "package-lock.json validation failed: $details"
        }
    }
    finally {
        Remove-Item -LiteralPath $temporaryJsonPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temporaryScriptPath -Force -ErrorAction SilentlyContinue
    }
}

$rootPackagePath = 'package.json'
$mobilePackagePath = 'apps\mobile-web\package.json'
$packageLockPath = 'package-lock.json'
$hostProjectPath = 'apps\windows-host\VolturaAir.Host.csproj'
$releaseWorkflowPath = '.github\workflows\release.yml'

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
$updatedHostProject = [xml](Get-RepoText $hostProjectPath)

if ([string]$updatedRootPackage.version -ne $Version) {
    throw "package.json did not validate with version '$Version'."
}
if ([string]$updatedMobilePackage.version -ne $Version) {
    throw "apps/mobile-web/package.json did not validate with version '$Version'."
}
Assert-PackageLockVersions `
    -Content (Get-RepoText $packageLockPath) `
    -ExpectedVersion $Version
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
    throw "release.yml did not validate with release tag 'v$Version'."
}
if ($updatedWorkflow -notmatch "(?m)^        default: $escapedVersion\s*$") {
    throw "release.yml did not validate with version '$Version'."
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
