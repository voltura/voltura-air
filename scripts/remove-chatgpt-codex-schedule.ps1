[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$managedTaskNames = @('ChatGPT Codex Update Check')
$tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $_.TaskName -in $managedTaskNames
})

if ($tasks.Count -eq 0) {
    Write-Host 'No ChatGPT/Codex updater scheduled tasks were found.'
    exit 0
}

foreach ($task in $tasks) {
    $taskIdentity = "{0}{1}" -f $task.TaskPath, $task.TaskName
    if ($PSCmdlet.ShouldProcess($taskIdentity, 'Remove ChatGPT/Codex updater scheduled task')) {
        Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false
        Write-Host "Removed ChatGPT/Codex updater scheduled task: $taskIdentity"
    }
}
