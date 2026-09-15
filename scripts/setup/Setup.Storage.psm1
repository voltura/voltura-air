# Files containing credentials are always below a directory protected before writing.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SetupPlainDirectory {
    param([string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Setup refuses a reparse point in a private storage path.' }
        $current = Split-Path -Parent $current
    }
}

function New-SetupPrivateDirectory {
    param([string]$Path)
    Assert-SetupPlainDirectory $Path
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'), [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($Path), $acl)
}

function Write-SetupAtomicFile {
    param([string]$Path, [string]$Text)
    Assert-SetupPlainDirectory (Split-Path -Parent $Path)
    if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Setup refuses a linked configuration file.' }
    $pending = "$Path.setup-pending"
    if (Test-Path -LiteralPath $pending) { throw "Unfinished configuration write: $pending. Inspect and remove this setup-owned pending file before retrying; the destination has been preserved." }
    try {
        $stream = [IO.File]::Open($pending, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($pending, $Path, [System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($pending, $Path) }
    } finally {
        if (Test-Path -LiteralPath $pending) { [IO.File]::Delete($pending) }
    }
}

function New-SetupPassword {
    $bytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}

function Invoke-SetupSql {
    param([string]$Executable, [string[]]$Arguments, [string]$Sql, [string]$Password, [ValidateRange(1, 60000)][int]$TimeoutMilliseconds = 60000)
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.Environment['MYSQL_PWD'] = $Password
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'Could not start the local database client.' }
        $output = $process.StandardOutput.ReadToEndAsync(); $errors = $process.StandardError.ReadToEndAsync()
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $write = $process.StandardInput.WriteAsync($Sql)
        if (-not $write.Wait($TimeoutMilliseconds)) { $process.Kill($true); $process.WaitForExit(); throw 'Local database input timed out.' }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit([math]::Max(1, $TimeoutMilliseconds - [int]$clock.ElapsedMilliseconds))) { $process.Kill($true); $process.WaitForExit(); throw 'Local database operation timed out.' }
        # Never return server error text: it may include SQL or credentials.
        $errorText = $errors.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.GetAwaiter().GetResult(); Error = if ($errorText -match 'Access denied') { 'Access denied' } elseif ($process.ExitCode) { 'Local database operation failed.' } else { '' } }
    } finally { $process.Dispose(); $info.Environment.Remove('MYSQL_PWD') | Out-Null }
}

Export-ModuleMember -Function *-Setup*
