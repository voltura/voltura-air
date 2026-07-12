param(
    [string]$OutputZip = "",
    [string]$Branch = "",
    [switch]$Bare
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Remove-BareSourceAssets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot
    )

    $removedCount = 0
    $docsSiteAssets = Join-Path $SourceRoot "docs\site\assets"
    if (Test-Path $docsSiteAssets) {
        $imageExtensions = @(".bmp", ".gif", ".ico", ".jpeg", ".jpg", ".png", ".svg", ".webp")
        $imageFiles = @(
            Get-ChildItem -Path $docsSiteAssets -Recurse -File |
                Where-Object { $imageExtensions -contains $_.Extension.ToLowerInvariant() }
        )

        foreach ($imageFile in $imageFiles) {
            Remove-Item $imageFile.FullName -Force
            $removedCount++
        }
    }

    $installerWelcomeImage = Join-Path $SourceRoot "installer\assets\installer-welcome.bmp"
    if (Test-Path $installerWelcomeImage) {
        Remove-Item $installerWelcomeImage -Force
        $removedCount++
    }

    return $removedCount
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found. Install Git for Windows, then run this script again."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$repoName = Split-Path $repoRoot -Leaf

if (-not (Test-Path (Join-Path $repoRoot ".git"))) {
    throw "Expected a Git repository at $repoRoot."
}

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Branch)) {
        throw "Could not determine the current Git branch. Pass -Branch explicitly."
    }
}

if ($Branch -eq "HEAD") {
    throw "The repository is in detached HEAD state. Pass -Branch explicitly or check out a branch first."
}

$sourceKind = if ($Bare) { "bare-source" } else { "source" }

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $safeBranch = $Branch -replace '[\\/:*?"<>|]', '_'
    $outputDir = Join-Path $repoRoot "artifacts\source"
    $OutputZip = Join-Path $outputDir "$repoName-$sourceKind-$safeBranch-$timestamp.zip"
}

$OutputZip = [System.IO.Path]::GetFullPath($OutputZip)
$outputParent = Split-Path -Parent $OutputZip
if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
    New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "$repoName-source-export-$([guid]::NewGuid())"
$tempClone = Join-Path $tempRoot $repoName

Write-Host "Repo root: $repoRoot"
Write-Host "Branch:    $Branch"
Write-Host "Mode:      $sourceKind"
Write-Host "Output:    $OutputZip"
Write-Host "Temp dir:  $tempClone"

try {
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    # Clone from Git metadata instead of copying the working directory.
    # This includes committed files from the selected branch and excludes local build outputs,
    # untracked files, node_modules, bin/obj folders, and other working-tree-only content.
    Invoke-Git -Arguments @(
        "clone",
        "--no-hardlinks",
        "--single-branch",
        "--branch", $Branch,
        $repoRoot,
        $tempClone
    )

    $gitDir = Join-Path $tempClone ".git"
    if (Test-Path $gitDir) {
        Remove-Item $gitDir -Recurse -Force
    }

    if ($Bare) {
        $removedCount = Remove-BareSourceAssets -SourceRoot $tempClone
        Write-Host "Bare source exclusions removed $removedCount committed asset file(s)."
    }

    if (Test-Path $OutputZip) {
        Remove-Item $OutputZip -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $tempClone,
        $OutputZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    Write-Host ""
    Write-Host "Created clean repository zip:"
    Write-Host $OutputZip
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}
