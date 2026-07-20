[CmdletBinding()]
param(
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d:[0-5]\d$')]
    [string]$Time = '04:00:00'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $PSScriptRoot 'run-chatgpt-codex-update-hidden.vbs'
$taskName = 'ChatGPT Codex Update Check'
$powerShellWindow = "$env:SystemRoot\System32\wscript.exe"
$runAt = [DateTime]::ParseExact($Time, 'HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)

if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "Scheduled updater launcher was not found: $launcherPath"
}

$action = New-ScheduledTaskAction -Execute $powerShellWindow -Argument "//B //Nologo `"$launcherPath`"" -WorkingDirectory $repositoryRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $runAt
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -Hidden
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Enable-ScheduledTask -TaskName $taskName | Out-Null

# Register-ScheduledTask does not expose the task's registration author. Set
# it in the task XML so Task Scheduler identifies this repository as owner.
[xml]$taskXml = Export-ScheduledTask -TaskName $taskName
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($taskXml.NameTable)
$namespaceManager.AddNamespace('task', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
$registrationInfo = $taskXml.SelectSingleNode('/task:Task/task:RegistrationInfo', $namespaceManager)
$author = $registrationInfo.SelectSingleNode('task:Author', $namespaceManager)
if ($null -eq $author) {
    $author = $taskXml.CreateElement('Author', $registrationInfo.NamespaceURI)
    $registrationInfo.AppendChild($author) | Out-Null
}
$author.InnerText = 'Voltura Air Development'
Register-ScheduledTask -TaskName $taskName -Xml $taskXml.OuterXml -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $taskName
Write-Host "Scheduled hidden ChatGPT/Codex update check daily at $Time. Next run: $($info.NextRunTime)."
