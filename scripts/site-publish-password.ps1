#requires -Version 7.6 -PSEdition Core

[CmdletBinding()]
param(
    [switch]$Clear
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required. Run this command from pwsh.'
}

Import-Module Microsoft.PowerShell.Security -ErrorAction Stop

$storageDirectory = Join-Path $env:LOCALAPPDATA 'Voltura Air'
$credentialPath = Join-Path $storageDirectory 'site-publish-sftp-password.dpapi'

if ($Clear) {
    Remove-Item -LiteralPath $credentialPath -Force -ErrorAction SilentlyContinue
    Write-Host 'Removed the stored one.com SFTP password.'
    exit 0
}

New-Item -ItemType Directory -Force -Path $storageDirectory | Out-Null
$password = Read-Host 'one.com SFTP password' -AsSecureString
if ($null -eq $password -or $password.Length -eq 0) {
    throw 'The SFTP password cannot be empty.'
}

$ciphertext = ConvertFrom-SecureString -SecureString $password
if ([string]::IsNullOrWhiteSpace($ciphertext)) {
    throw 'Windows returned an empty encrypted password.'
}

[IO.File]::WriteAllText($credentialPath, $ciphertext, [Text.UTF8Encoding]::new($false))
if (-not (Test-Path -LiteralPath $credentialPath -PathType Leaf) -or
    (Get-Item -LiteralPath $credentialPath).Length -eq 0) {
    throw 'Windows did not create the encrypted password file.'
}

Write-Host 'Stored the one.com SFTP password for this Windows account.'
