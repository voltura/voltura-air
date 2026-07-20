[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$updaterPath = Join-Path $PSScriptRoot 'update-chatgpt-codex.ps1'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Check for ChatGPT Codex updates.lnk'
$powerShellWindow = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

if (-not (Test-Path -LiteralPath $updaterPath -PathType Leaf)) {
    throw "ChatGPT/Codex updater was not found: $updaterPath"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $powerShellWindow
$shortcut.Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$updaterPath`""
$shortcut.WorkingDirectory = $repositoryRoot
$shortcut.Description = 'Check for and silently install ChatGPT/Codex updates'
$shortcut.IconLocation = "$powerShellWindow,0"
$shortcut.Save()

Write-Host "Created desktop shortcut: $shortcutPath"
