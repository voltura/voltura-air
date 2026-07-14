param(
    [int]$ProcessId = 0,
    [ValidateRange(10, 86400)]
    [int]$DurationSeconds = 600,
    [ValidateRange(1, 60)]
    [int]$IntervalSeconds = 5,
    [string]$OutputCsv
)

$ErrorActionPreference = "Stop"

if ($ProcessId -le 0) {
    $matches = @(Get-Process -Name "VolturaAir.Host" -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) {
        throw "VolturaAir.Host is not running. Start exactly one host instance before measuring."
    }

    if ($matches.Count -gt 1) {
        throw "Multiple VolturaAir.Host processes are running. Stop competing instances before measuring."
    }

    $ProcessId = $matches[0].Id
}

$logicalProcessorCount = [Math]::Max(1, [Environment]::ProcessorCount)
$startedAt = [DateTimeOffset]::UtcNow
$previousAt = $null
$previousCpu100Nanoseconds = $null
$previousReadBytes = $null
$previousWriteBytes = $null
$samples = [System.Collections.Generic.List[object]]::new()
$canMeasureTcp = $null -ne (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)

while (([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds -le $DurationSeconds) {
    $sampledAt = [DateTimeOffset]::UtcNow
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"
    if ($null -eq $process) {
        throw "VolturaAir.Host process $ProcessId exited while measurements were running."
    }

    $cpu100Nanoseconds = [uint64]$process.KernelModeTime + [uint64]$process.UserModeTime
    $readBytes = [uint64]$process.ReadTransferCount
    $writeBytes = [uint64]$process.WriteTransferCount
    $cpuPercent = 0.0
    $readBytesPerSecond = 0.0
    $writeBytesPerSecond = 0.0
    if ($null -ne $previousAt) {
        $elapsedSeconds = [Math]::Max(0.001, ($sampledAt - $previousAt).TotalSeconds)
        $cpuPercent = (($cpu100Nanoseconds - $previousCpu100Nanoseconds) / 10000000.0) / $elapsedSeconds / $logicalProcessorCount * 100.0
        $readBytesPerSecond = ($readBytes - $previousReadBytes) / $elapsedSeconds
        $writeBytesPerSecond = ($writeBytes - $previousWriteBytes) / $elapsedSeconds
    }

    $tcpConnectionCount = 0
    if ($canMeasureTcp) {
        $tcpConnectionCount = @(Get-NetTCPConnection -OwningProcess $ProcessId -ErrorAction SilentlyContinue).Count
    }

    $sample = [pscustomobject]@{
        Timestamp = $sampledAt.ToLocalTime().ToString("O")
        ProcessId = $ProcessId
        CpuPercent = [Math]::Round($cpuPercent, 3)
        WorkingSetBytes = [uint64]$process.WorkingSetSize
        PrivateBytes = [uint64]$process.PrivatePageCount
        HandleCount = [uint32]$process.HandleCount
        ThreadCount = [uint32]$process.ThreadCount
        ReadBytesPerSecond = [Math]::Round($readBytesPerSecond, 1)
        WriteBytesPerSecond = [Math]::Round($writeBytesPerSecond, 1)
        TotalReadBytes = $readBytes
        TotalWriteBytes = $writeBytes
        TcpConnectionCount = $tcpConnectionCount
    }
    $samples.Add($sample)
    $sample

    $previousAt = $sampledAt
    $previousCpu100Nanoseconds = $cpu100Nanoseconds
    $previousReadBytes = $readBytes
    $previousWriteBytes = $writeBytes

    if (([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds + $IntervalSeconds -le $DurationSeconds) {
        Start-Sleep -Seconds $IntervalSeconds
    }
    else {
        break
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutputCsv)) {
    $fullOutputPath = [System.IO.Path]::GetFullPath($OutputCsv)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    $samples | Export-Csv -LiteralPath $fullOutputPath -NoTypeInformation -Encoding utf8
}
