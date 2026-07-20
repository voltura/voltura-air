[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Check for ChatGPT Codex updates.lnk'

if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
    Write-Host "No ChatGPT/Codex updater shortcut was found at: $shortcutPath"
    exit 0
}

if ($PSCmdlet.ShouldProcess($shortcutPath, 'Remove ChatGPT/Codex updater desktop shortcut')) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed ChatGPT/Codex updater desktop shortcut: $shortcutPath"
}
