Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-SetupSiteConfiguration {
    param([string]$Path, [int]$RequestedPort, [bool]$ExplicitPort)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $existing = Get-Content -LiteralPath $Path -Raw
    $dsn = [regex]::Match($existing, "'dsn'\s*=>\s*'mysql:host=127\.0\.0\.1;port=(\d+);dbname=voltura_air_dev;charset=utf8mb4'")
    if (-not $dsn.Success -or $existing -notmatch "'username'\s*=>\s*'voltura_air_dev'") { throw 'Existing site database target is unrecognized; configuration has been preserved.' }
    $savedPort = [int]$dsn.Groups[1].Value
    if ($savedPort -lt 1024 -or $savedPort -gt 65535) { throw 'Existing site database port is invalid; configuration has been preserved.' }
    if ($ExplicitPort -and $RequestedPort -ne $savedPort) { throw 'The requested port differs from existing config.php. Reconfigure the database explicitly before running setup.' }
    $passwordMatch = [regex]::Match($existing, "'password'\s*=>\s*'((?:\\.|[^'\\])*)'")
    if (-not $passwordMatch.Success) { throw 'Existing development password is unrecognized; configuration has been preserved.' }
    $password = [regex]::Replace($passwordMatch.Groups[1].Value, "\\(['\\])", '$1')
    return [pscustomobject]@{ Port = $savedPort; Password = $password }
}

Export-ModuleMember -Function Read-SetupSiteConfiguration
