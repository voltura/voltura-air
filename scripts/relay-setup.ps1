#requires -Version 7.6 -PSEdition Core

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingConvertToSecureStringWithPlainText',
    '',
    Justification = 'Account and key identifiers are validated non-secret values; SecureString prevents Wrangler stdin from being logged.'
)]
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$relayWorkspace = Join-Path $repositoryRoot 'services\relay'
$serviceConfigPath = Join-Path $repositoryRoot 'apps\windows-host\relay-service.json'

function Invoke-Wrangler {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & npm exec --workspace '@voltura-air/relay' -- wrangler @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Cloudflare command failed: wrangler $($Arguments -join ' ')" }
}

function Set-WranglerSecret {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][SecureString]$Value)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $plain | & npm exec --workspace '@voltura-air/relay' -- wrangler secret put $Name
        if ($LASTEXITCODE -ne 0) { throw "Could not store Cloudflare secret $Name." }
    }
    finally {
        if ($null -ne $plain) { $plain = $null }
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

Push-Location $repositoryRoot
try {
    Write-Host 'Step 1/7: Sign in to Cloudflare in the browser.'
    Invoke-Wrangler @('login')

    $accountId = (Read-Host 'Cloudflare Account ID (Dashboard right sidebar)').Trim()
    $turnKeyId = (Read-Host 'Realtime TURN key ID').Trim()
    $workersSubdomain = (Read-Host 'Your workers.dev account name (the part before .workers.dev)').Trim()
    if ($accountId -notmatch '^[a-f0-9]{32}$') { throw 'The Account ID must be 32 lowercase hexadecimal characters.' }
    if ($turnKeyId -notmatch '^[A-Za-z0-9_-]{8,128}$') { throw 'The TURN key ID format is invalid.' }
    if ($workersSubdomain -notmatch '^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$') { throw 'The workers.dev account name is invalid.' }

    Write-Host 'Step 2/7: Validate the relay locally.'
    & npm run relay:check
    if ($LASTEXITCODE -ne 0) { throw 'Relay validation failed.' }

    Write-Host 'Step 3/7: Create the Worker and Durable Object before attaching secrets.'
    Invoke-Wrangler @('deploy')

    Write-Host 'Step 4/7: Store restricted credentials. Typed values are hidden.'
    Set-WranglerSecret 'CLOUDFLARE_ACCOUNT_ID' (ConvertTo-SecureString $accountId -AsPlainText -Force)
    Set-WranglerSecret 'TURN_KEY_ID' (ConvertTo-SecureString $turnKeyId -AsPlainText -Force)
    Set-WranglerSecret 'TURN_API_TOKEN' (Read-Host 'Restricted TURN credential-generation API token' -AsSecureString)
    Set-WranglerSecret 'CLOUDFLARE_ANALYTICS_TOKEN' (Read-Host 'Account Analytics Read API token' -AsSecureString)

    Write-Host 'Step 5/7: Deploy the configured Worker.'
    Invoke-Wrangler @('deploy')

    $relayBase = "https://voltura-air-relay.$workersSubdomain.workers.dev"
    Write-Host 'Step 6/7: Verify the deployed health endpoint.'
    $health = Invoke-RestMethod -Method Get -Uri "$relayBase/v1/health" -TimeoutSec 30
    if ($health.status -ne 'ok' -or $health.protocol -ne 1) { throw 'The deployed relay returned an unexpected health response.' }

    $configuration = [ordered]@{
        serviceId = 'voltura-cloud-v1'
        httpsBase = $relayBase
        supportsTurn = $true
    }
    $configurationJson = ($configuration | ConvertTo-Json) + [Environment]::NewLine
    [IO.File]::WriteAllText($serviceConfigPath, $configurationJson, [Text.UTF8Encoding]::new($false))
    Write-Host 'Step 7/7: Saved the verified relay address for both the host and hosted PWA.'
    Write-Host "SUCCESS: $relayBase"
    Write-Host 'Nothing was uploaded to voltura.se and no Windows release was created.'
}
finally {
    Pop-Location
}
