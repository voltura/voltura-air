#requires -Version 7.6 -PSEdition Core

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
$gitJournalRelative = (& git -C $repoRoot rev-parse --git-path 'voltura-air-release-preparation.json').Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitJournalRelative)) {
    throw 'Git could not resolve the release-preparation journal path.'
}
$journalPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $gitJournalRelative))
$releaseOwnedPaths = @(
    'package.json',
    'apps\mobile-web\package.json',
    'services\relay\package.json',
    'package-lock.json',
    'apps\windows-host\VolturaAir.Host.csproj'
)
$originals = [ordered]@{}
$updates = [ordered]@{}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))
}

function Write-FlushedNewFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][byte[]]$Bytes
    )
    $stream = $null
    try {
        $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 16384, [IO.FileOptions]::WriteThrough)
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
    } catch {
        if ($null -ne $stream) { $stream.Dispose(); $stream = $null }
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Remove-ReleaseTransactionArtifacts {
    param([Parameter(Mandatory = $true)]$Journal)
    foreach ($entry in $Journal.entries) {
        Remove-Item -LiteralPath ([string]$entry.stagedPath) -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath ([string]$entry.backupPath) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $journalPath -Force -ErrorAction SilentlyContinue
}

function Complete-ReleaseTransaction {
    param([Parameter(Mandatory = $true)]$Journal)
    foreach ($entry in $Journal.entries) {
        $target = [string]$entry.targetPath
        $staged = [string]$entry.stagedPath
        $backup = [string]$entry.backupPath
        $currentHash = Get-FileSha256 $target
        if ($currentHash -eq [string]$entry.stagedHash) { continue }
        if ($currentHash -ne [string]$entry.originalHash) {
            throw "Release recovery stopped because '$target' contains unexpected content."
        }
        if ((Get-FileSha256 $staged) -ne [string]$entry.stagedHash) {
            throw "Release recovery stopped because its staged file for '$target' is missing or changed."
        }
        [IO.File]::Replace($staged, $target, $backup, $true)
        if ((Get-FileSha256 $target) -ne [string]$entry.stagedHash) {
            throw "Release recovery could not verify '$target' after replacement."
        }
    }
    Remove-ReleaseTransactionArtifacts $Journal
}

function Rollback-ReleaseTransaction {
    param([Parameter(Mandatory = $true)]$Journal)
    foreach ($entry in @($Journal.entries)[-1..-$Journal.entries.Count]) {
        $target = [string]$entry.targetPath
        $backup = [string]$entry.backupPath
        $currentHash = Get-FileSha256 $target
        if ($currentHash -eq [string]$entry.originalHash) { continue }
        if ($currentHash -ne [string]$entry.stagedHash -or (Get-FileSha256 $backup) -ne [string]$entry.originalHash) {
            throw "Release rollback stopped because '$target' or its transaction backup contains unexpected content."
        }
        [IO.File]::Replace($backup, $target, $null, $true)
        if ((Get-FileSha256 $target) -ne [string]$entry.originalHash) {
            throw "Release rollback could not verify '$target'."
        }
    }
    Remove-ReleaseTransactionArtifacts $Journal
}

if (Test-Path -LiteralPath $journalPath -PathType Leaf) {
    try {
        $pendingJournal = [IO.File]::ReadAllText($journalPath) | ConvertFrom-Json -Depth 8
        if ($pendingJournal.kind -ne 'voltura-air-release-preparation' -or -not $pendingJournal.entries) {
            throw 'The release-preparation journal is invalid.'
        }
        if ([string]$pendingJournal.transactionId -notmatch '^[a-f0-9]{32}$') {
            throw 'The release-preparation transaction identifier is invalid.'
        }
        foreach ($entry in $pendingJournal.entries) {
            if ([string]$entry.relativePath -notin $releaseOwnedPaths) {
                throw 'The release-preparation journal contains an unowned target.'
            }
            $expectedTarget = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$entry.relativePath)))
            $suffix = ".voltura-release-$($pendingJournal.transactionId)"
            if ([string]$entry.targetPath -ne $expectedTarget -or
                [string]$entry.stagedPath -ne "$expectedTarget$suffix.staged" -or
                [string]$entry.backupPath -ne "$expectedTarget$suffix.backup" -or
                [string]$entry.originalHash -notmatch '^[A-F0-9]{64}$' -or
                [string]$entry.stagedHash -notmatch '^[A-F0-9]{64}$') {
                throw 'The release-preparation journal contains invalid transaction ownership data.'
            }
        }
        Complete-ReleaseTransaction $pendingJournal
        Write-Host "Recovered release preparation for version $($pendingJournal.version)."
    } catch {
        throw "Release preparation recovery failed. No unexpected content was overwritten. $($_.Exception.Message)"
    }
}

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
  ["mobile workspace entry", lock.packages && lock.packages["apps/mobile-web"] && lock.packages["apps/mobile-web"].version],
  ["relay workspace entry", lock.packages && lock.packages["services/relay"] && lock.packages["services/relay"].version]
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
$relayPackagePath = 'services\relay\package.json'
$packageLockPath = 'package-lock.json'
$hostProjectPath = 'apps\windows-host\VolturaAir.Host.csproj'

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
    -RelativePath $relayPackagePath `
    -Pattern '(?<prefix>^[ \t]*"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,?[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'relay package version'

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
    -RelativePath $packageLockPath `
    -Pattern '(?<prefix>^(?<indent>[ \t]*)"name"[ \t]*:[ \t]*"@voltura-air/relay"[ \t]*,[ \t]*\r?\n\k<indent>"version"[ \t]*:[ \t]*")[^"]+(?<suffix>"[ \t]*,[ \t]*\r?$)' `
    -NewValue $Version `
    -ExpectedCount 1 `
    -Description 'relay package-lock version'

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

$updatedRootPackage = Get-RepoText $rootPackagePath | ConvertFrom-Json
$updatedMobilePackage = Get-RepoText $mobilePackagePath | ConvertFrom-Json
$updatedRelayPackage = Get-RepoText $relayPackagePath | ConvertFrom-Json
$updatedHostProject = [xml](Get-RepoText $hostProjectPath)

if ([string]$updatedRootPackage.version -ne $Version) {
    throw "package.json did not validate with version '$Version'."
}
if ([string]$updatedMobilePackage.version -ne $Version) {
    throw "apps/mobile-web/package.json did not validate with version '$Version'."
}
if ([string]$updatedRelayPackage.version -ne $Version) {
    throw "services/relay/package.json did not validate with version '$Version'."
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

$transactionId = [guid]::NewGuid().ToString('N')
$entries = [Collections.Generic.List[object]]::new()
try {
    foreach ($relativePath in $updates.Keys) {
        $updatedText = [string]$updates[$relativePath]
        $originalText = [string]$originals[$relativePath]
        if ($updatedText -eq $originalText) { continue }
        $targetPath = Get-RepoPath $relativePath
        $stagedPath = "$targetPath.voltura-release-$transactionId.staged"
        $backupPath = "$targetPath.voltura-release-$transactionId.backup"
        Write-FlushedNewFile $stagedPath ($utf8NoBom.GetBytes($updatedText))
        $entries.Add([pscustomobject]@{
            relativePath = $relativePath
            targetPath = $targetPath
            stagedPath = $stagedPath
            backupPath = $backupPath
            originalHash = Get-FileSha256 $targetPath
            stagedHash = Get-FileSha256 $stagedPath
        })
    }
} catch {
    foreach ($entry in $entries) {
        Remove-Item -LiteralPath ([string]$entry.stagedPath) -Force -ErrorAction SilentlyContinue
    }
    throw
}

$writtenPaths = @($entries | ForEach-Object { $_.relativePath })
if ($entries.Count -gt 0) {
    $journal = [pscustomobject]@{
        kind = 'voltura-air-release-preparation'
        version = $Version
        transactionId = $transactionId
        entries = @($entries)
    }
    $journalBytes = $utf8NoBom.GetBytes(($journal | ConvertTo-Json -Depth 8))
    try {
        Write-FlushedNewFile $journalPath $journalBytes
        Complete-ReleaseTransaction $journal
    } catch {
        $commitFailure = $_
        try {
            Rollback-ReleaseTransaction $journal
        } catch {
            throw [AggregateException]::new(
                'Release preparation failed and rollback was incomplete. The verified journal and transaction artifacts were retained for retry.',
                @($commitFailure.Exception, $_.Exception))
        }
        throw $commitFailure
    }
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
