#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Recover', 'PrepareUninstall', 'Verify', 'Promote', 'Commit', 'Rollback', 'StopHost', 'StageRemoval', 'CompleteRemoval')]
    [string]$Action,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [string]$StagingDirectory,
    [string]$JournalPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifestName = 'installer-payload.sha256'

function Get-Full([string]$Path) { return [IO.Path]::GetFullPath($Path) }
function Get-Hash([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}
function Assert-PlainDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Directory is missing: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Directory is a reparse point: $Path" }
}
function Test-Manifest([string]$Root) {
    Assert-PlainDirectory $Root
    $manifest = Join-Path $Root $manifestName
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { return $false }
    $expected = @{}
    foreach ($line in [IO.File]::ReadAllLines($manifest)) {
        if ($line -notmatch '^([a-f0-9]{64}) \*(.+)$') { return $false }
        $relative = $Matches[2]
        if ($relative -match '(^|/|\\)\.\.($|/|\\)' -or [IO.Path]::IsPathRooted($relative) -or $expected.ContainsKey($relative)) { return $false }
        $expected[$relative] = $Matches[1]
    }
    if ($expected.Count -eq 0) { return $false }
    foreach ($entry in $expected.GetEnumerator()) {
        $candidate = Join-Path $Root ($entry.Key.Replace('/', '\'))
        $full = Get-Full $candidate
        $prefix = (Get-Full $Root).TrimEnd('\') + '\'
        if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Get-Hash $full) -ne $entry.Value) { return $false }
    }
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force -Recurse) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
        if ($item.PSIsContainer) { continue }
        $rootPrefix = (Get-Full $Root).TrimEnd('\') + '\'
        $relative = $item.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        if ($relative -ne $manifestName -and $relative -ne 'Uninstall.exe' -and -not $expected.ContainsKey($relative)) { return $false }
    }
    return $true
}
function Write-Journal($Value) {
    $parent = Split-Path -Parent $JournalPath
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = "$JournalPath.$([guid]::NewGuid().ToString('N')).tmp"
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 6))
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 16384, [IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    [IO.File]::Move($temporary, $JournalPath)
}
function Read-Journal {
    if (-not (Test-Path -LiteralPath $JournalPath -PathType Leaf)) { return $null }
    $journal = [IO.File]::ReadAllText($JournalPath) | ConvertFrom-Json
    if ($journal.kind -ne 'voltura-air-installer' -or $journal.installDirectory -ne (Get-Full $InstallDirectory)) { throw 'Installer journal ownership is invalid.' }
    return $journal
}
function Assert-OwnedSibling([string]$Path, [string]$Kind) {
    $parent = Split-Path -Parent (Get-Full $InstallDirectory)
    $name = Split-Path -Leaf $Path
    $installName = Split-Path -Leaf (Get-Full $InstallDirectory)
    $suffixPattern = if ($Kind -eq 'staging') { '[0-9]{1,10}' } else { '[a-f0-9]{8,32}' }
    if ((Split-Path -Parent (Get-Full $Path)) -ne $parent -or $name -notmatch ('^' + [regex]::Escape($installName) + '\.' + $Kind + '-' + $suffixPattern + '$')) {
        throw 'Installer journal references an unowned sibling directory.'
    }
}
function Get-OwnedHostProcesses {
    $prefix = (Get-Full $InstallDirectory).TrimEnd('\') + '\'
    return @(Get-Process -Name 'VolturaAir.Host' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and (Get-Full $_.Path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
}
function Recover-Transaction {
    $journal = Read-Journal
    if ($null -eq $journal) { return }
    if ($journal.mode -eq 'remove') {
        Assert-OwnedSibling $journal.removalDirectory 'removal'
        if (Test-Path -LiteralPath $InstallDirectory) { throw 'Uninstall recovery found unexpected content at the install path.' }
        if (Test-Path -LiteralPath $journal.removalDirectory) { throw 'Rerun uninstall to finish its owned removal.' }
        Remove-Item -LiteralPath $JournalPath -Force
        return
    }
    if ($journal.mode -ne 'upgrade') { throw 'Installer journal mode is invalid.' }
    Assert-OwnedSibling $journal.stagingDirectory 'staging'
    Assert-OwnedSibling $journal.backupDirectory 'backup'
    $currentValid = (Test-Path -LiteralPath $InstallDirectory) -and (Test-Manifest $InstallDirectory)
    $stagingValid = (Test-Path -LiteralPath $journal.stagingDirectory) -and (Test-Manifest $journal.stagingDirectory)
    $backupValid = (Test-Path -LiteralPath $journal.backupDirectory) -and (Test-Manifest $journal.backupDirectory)
    $currentHash = if ($currentValid) { Get-Hash (Join-Path $InstallDirectory $manifestName) } else { $null }
    $stagingHash = if ($stagingValid) { Get-Hash (Join-Path $journal.stagingDirectory $manifestName) } else { $null }
    if ($currentHash -eq $journal.newManifestHash) {
        if (Test-Path -LiteralPath $journal.backupDirectory) { Remove-Item -LiteralPath $journal.backupDirectory -Recurse -Force }
        if (Test-Path -LiteralPath $journal.stagingDirectory) { Remove-Item -LiteralPath $journal.stagingDirectory -Recurse -Force }
        Remove-Item -LiteralPath $JournalPath -Force
        return
    }
    if ($currentHash -eq $journal.oldManifestHash -and $stagingHash -eq $journal.newManifestHash -and
        -not (Test-Path -LiteralPath $journal.backupDirectory)) {
        [IO.Directory]::Move($InstallDirectory, $journal.backupDirectory)
        [IO.Directory]::Move($journal.stagingDirectory, $InstallDirectory)
        Remove-Item -LiteralPath $journal.backupDirectory -Recurse -Force
        Remove-Item -LiteralPath $JournalPath -Force
        return
    }
    if (Test-Path -LiteralPath $InstallDirectory) { throw 'Installer recovery will not overwrite an unexpected installation directory.' }
    if ($stagingHash -eq $journal.newManifestHash) {
        [IO.Directory]::Move($journal.stagingDirectory, $InstallDirectory)
        if (Test-Path -LiteralPath $journal.backupDirectory) { Remove-Item -LiteralPath $journal.backupDirectory -Recurse -Force }
        Remove-Item -LiteralPath $JournalPath -Force
        return
    }
    if ($backupValid -and (Get-Hash (Join-Path $journal.backupDirectory $manifestName)) -eq $journal.oldManifestHash) {
        [IO.Directory]::Move($journal.backupDirectory, $InstallDirectory)
        Remove-Item -LiteralPath $JournalPath -Force
        return
    }
    throw 'Installer recovery could not verify staging or backup content.'
}

$InstallDirectory = Get-Full $InstallDirectory
if ($JournalPath) { $JournalPath = Get-Full $JournalPath }
if ($StagingDirectory) { $StagingDirectory = Get-Full $StagingDirectory }

switch ($Action) {
    'Verify' { if (-not (Test-Manifest $StagingDirectory)) { throw 'Installer payload verification failed.' }; break }
    'StopHost' {
        $processes = @(Get-OwnedHostProcesses)
        foreach ($process in $processes) { $process.Kill() }
        foreach ($process in $processes) { if (-not $process.WaitForExit(10000)) { throw 'The installed Voltura Air host did not stop.' } }
        break
    }
    'Recover' { Recover-Transaction; break }
    'PrepareUninstall' {
        $journal = Read-Journal
        if ($null -eq $journal) { break }
        if ($journal.mode -ne 'remove') { throw 'An installer upgrade transaction must be recovered before uninstall.' }
        Assert-OwnedSibling $journal.removalDirectory 'removal'
        if (Test-Path -LiteralPath $InstallDirectory) { throw 'Uninstall recovery found unexpected content at the install path.' }
        break
    }
    'Promote' {
        if (-not (Test-Manifest $StagingDirectory)) { throw 'The staged installer payload is invalid.' }
        $transaction = [guid]::NewGuid().ToString('N').Substring(0, 16)
        $backup = "$InstallDirectory.backup-$transaction"
        $oldHash = $null
        if (Test-Path -LiteralPath $InstallDirectory) {
            if (-not (Test-Manifest $InstallDirectory)) { throw 'The existing installation manifest is invalid.' }
            $oldHash = Get-Hash (Join-Path $InstallDirectory $manifestName)
        }
        $journal = [pscustomobject]@{
            kind = 'voltura-air-installer'; mode = 'upgrade'; installDirectory = $InstallDirectory
            stagingDirectory = $StagingDirectory; backupDirectory = $backup
            oldManifestHash = $oldHash; newManifestHash = Get-Hash (Join-Path $StagingDirectory $manifestName)
        }
        Write-Journal $journal
        if (Test-Path -LiteralPath $InstallDirectory) { [IO.Directory]::Move($InstallDirectory, $backup) }
        [IO.Directory]::Move($StagingDirectory, $InstallDirectory)
        break
    }
    'Commit' {
        $journal = Read-Journal
        if ($null -eq $journal -or $journal.mode -ne 'upgrade' -or -not (Test-Manifest $InstallDirectory) -or
            (Get-Hash (Join-Path $InstallDirectory $manifestName)) -ne $journal.newManifestHash) { throw 'Promoted installation verification failed.' }
        if (Test-Path -LiteralPath $journal.backupDirectory) { Remove-Item -LiteralPath $journal.backupDirectory -Recurse -Force }
        Remove-Item -LiteralPath $JournalPath -Force
        break
    }
    'Rollback' {
        $journal = Read-Journal
        if ($null -eq $journal -or $journal.mode -ne 'upgrade') { throw 'No owned installer upgrade can be rolled back.' }
        if (Test-Path -LiteralPath $InstallDirectory) {
            if (-not (Test-Manifest $InstallDirectory) -or (Get-Hash (Join-Path $InstallDirectory $manifestName)) -ne $journal.newManifestHash) { throw 'Rollback will not move unexpected installation content.' }
            [IO.Directory]::Move($InstallDirectory, $journal.stagingDirectory)
        }
        if ($journal.oldManifestHash) {
            if (-not (Test-Manifest $journal.backupDirectory) -or (Get-Hash (Join-Path $journal.backupDirectory $manifestName)) -ne $journal.oldManifestHash) { throw 'Rollback backup verification failed.' }
            [IO.Directory]::Move($journal.backupDirectory, $InstallDirectory)
        }
        elseif (Test-Path -LiteralPath $journal.stagingDirectory) {
            if (-not (Test-Manifest $journal.stagingDirectory) -or
                (Get-Hash (Join-Path $journal.stagingDirectory $manifestName)) -ne $journal.newManifestHash) {
                throw 'Clean-install rollback will not remove unexpected staging content.'
            }
            Remove-Item -LiteralPath $journal.stagingDirectory -Recurse -Force
        }
        Remove-Item -LiteralPath $JournalPath -Force
        break
    }
    'StageRemoval' {
        $existing = Read-Journal
        if ($null -ne $existing) {
            if ($existing.mode -ne 'remove') { throw 'An installer upgrade transaction must be recovered before uninstall.' }
            Assert-OwnedSibling $existing.removalDirectory 'removal'
            if (Test-Path -LiteralPath $InstallDirectory) {
                throw 'The retained uninstall transaction is not in its expected state.'
            }
            break
        }
        if (-not (Test-Manifest $InstallDirectory)) { throw 'The installation cannot be verified for removal.' }
        $transaction = [guid]::NewGuid().ToString('N').Substring(0, 16)
        $removal = "$InstallDirectory.removal-$transaction"
        Write-Journal ([pscustomobject]@{ kind = 'voltura-air-installer'; mode = 'remove'; installDirectory = $InstallDirectory; removalDirectory = $removal })
        [IO.Directory]::Move($InstallDirectory, $removal)
        break
    }
    'CompleteRemoval' {
        $journal = Read-Journal
        if ($null -eq $journal -or $journal.mode -ne 'remove') { throw 'No owned uninstall removal can be completed.' }
        Assert-OwnedSibling $journal.removalDirectory 'removal'
        if (Test-Path -LiteralPath $InstallDirectory) { throw 'Uninstall will not remove an unexpected installation directory.' }
        if (Test-Path -LiteralPath $journal.removalDirectory) { Remove-Item -LiteralPath $journal.removalDirectory -Recurse -Force }
        Remove-Item -LiteralPath $JournalPath -Force
        break
    }
}
