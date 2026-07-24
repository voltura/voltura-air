$hostProcesses = Get-Process -Name 'VolturaAir.Host' -ErrorAction SilentlyContinue

if ($hostProcesses) {
    $hostProcesses | Stop-Process -Force
}
