#requires -Version 7.6 -PSEdition Core

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'powershell-compatibility.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$windowsScripts = @($manifest.windowsPowerShell51 | ForEach-Object { Join-Path $PSScriptRoot $_ })
$coreScripts = @($manifest.powerShell76 | ForEach-Object { Join-Path $PSScriptRoot $_ })
$listed = @($windowsScripts + $coreScripts | ForEach-Object { [IO.Path]::GetFullPath($_) } | Sort-Object -Unique)
$actual = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | ForEach-Object FullName | Sort-Object -Unique)
if (Compare-Object $listed $actual) {
    throw 'powershell-compatibility.json must classify every PowerShell script exactly once.'
}

$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) { throw 'Windows PowerShell 5.1 was not found.' }

function Assert-Parses([string]$Executable, [string[]]$Paths, [string]$Label) {
    $encodedPaths = ConvertTo-Json $Paths -Compress
    $command = @"
`$ErrorActionPreference = 'Stop'
`$paths = ConvertFrom-Json '$($encodedPaths.Replace("'", "''"))'
`$failures = foreach (`$path in `$paths) {
    `$tokens = `$null
    `$errors = `$null
    [void][Management.Automation.Language.Parser]::ParseFile(`$path, [ref]`$tokens, [ref]`$errors)
    foreach (`$error in `$errors) { "`${path}:`$(`$error.Extent.StartLineNumber): `$(`$error.Message)" }
}
if (`$failures) { `$failures | Write-Error; exit 1 }
"@
    & $Executable -NoProfile -NonInteractive -Command $command
    if ($LASTEXITCODE -ne 0) { throw "$Label parsing failed." }
}

Assert-Parses $windowsPowerShell $windowsScripts 'Windows PowerShell 5.1'
Assert-Parses (Get-Command pwsh -ErrorAction Stop).Source $coreScripts 'PowerShell 7.6'

$analyzer = Get-Module -ListAvailable PSScriptAnalyzer | Where-Object Version -eq ([version]'1.25.0') | Select-Object -First 1
if (-not $analyzer) { throw 'PSScriptAnalyzer 1.25.0 is required. Install-PSResource PSScriptAnalyzer -Version 1.25.0 -Scope CurrentUser' }
Import-Module $analyzer.Path -Force
$findings = @(Invoke-ScriptAnalyzer -Path $PSScriptRoot -Recurse -Severity Error)
if ($findings.Count -gt 0) {
    $findings | Format-Table RuleName, ScriptName, Line, Message -AutoSize | Out-String | Write-Error
    throw 'PSScriptAnalyzer reported errors.'
}

Write-Host "PowerShell check passed: $($windowsScripts.Count) Windows PowerShell 5.1 scripts and $($coreScripts.Count) PowerShell 7.6 scripts."
