[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$accountId = (Read-Host 'Cloudflare Account ID').Trim()
$turnKeyId = (Read-Host 'Realtime TURN key ID').Trim()
$token = Read-Host 'Account Analytics Read API token' -AsSecureString
if ($accountId -notmatch '^[a-f0-9]{32}$') { throw 'The Account ID format is invalid.' }
if ($turnKeyId -notmatch '^[A-Za-z0-9_-]{8,128}$') { throw 'The TURN key ID format is invalid.' }

$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
try {
    $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $today = [DateTime]::UtcNow
    $variables = @{ accountId = $accountId; dateFrom = $today.ToString('yyyy-MM-01'); dateTo = $today.ToString('yyyy-MM-dd') }
    $query = @'
query Usage($accountId: String!, $dateFrom: Date!, $dateTo: Date!) {
  viewer { accounts(filter: { accountTag: $accountId }) {
    callsTurnUsageAdaptiveGroups(limit: 10000, filter: { date_geq: $dateFrom, date_leq: $dateTo, keyId: "TURN_KEY_ID" }) {
      sum { egressBytes ingressBytes }
    }
  } }
}
'@.Replace('TURN_KEY_ID', $turnKeyId)
    $body = @{ query = $query; variables = $variables } | ConvertTo-Json -Depth 8
    $response = Invoke-RestMethod -Method Post -Uri 'https://api.cloudflare.com/client/v4/graphql' -Headers @{ Authorization = "Bearer $plainToken" } -ContentType 'application/json' -Body $body
    if ($response.errors) { throw 'Cloudflare returned an analytics error.' }
    $groups = @($response.data.viewer.accounts[0].callsTurnUsageAdaptiveGroups)
    $bytes = ($groups | ForEach-Object { [decimal]$_.sum.egressBytes + [decimal]$_.sum.ingressBytes } | Measure-Object -Sum).Sum
    if ($null -eq $bytes) { $bytes = 0 }
    Write-Host ("Current-month TURN transfer: {0:N2} GB" -f ($bytes / 1000000000))
    Write-Host ("Quota estimate: {0:N1}% of 1 TB" -f (($bytes / 1000000000000) * 100))
}
finally {
    if ($null -ne $plainToken) { $plainToken = $null }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
