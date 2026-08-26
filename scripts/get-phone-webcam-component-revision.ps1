[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$NativeRoot,
    [Parameter(Mandatory)]
    [string]$BuildScriptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$NativeRoot = [System.IO.Path]::GetFullPath($NativeRoot)
$BuildScriptPath = [System.IO.Path]::GetFullPath($BuildScriptPath)
$revisionScriptPath = [System.IO.Path]::GetFullPath($PSCommandPath)

$inputs = @(
    Get-ChildItem -LiteralPath $NativeRoot -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:bin|obj|packages)[\\/]'
        } |
        ForEach-Object {
            [pscustomobject]@{
                LogicalPath = "native/" + [System.IO.Path]::GetRelativePath($NativeRoot, $_.FullName).Replace("\", "/")
                FullPath = $_.FullName
            }
        }
)
$inputs += [pscustomobject]@{
    LogicalPath = "scripts/build-phone-webcam-native.ps1"
    FullPath = $BuildScriptPath
}
$inputs += [pscustomobject]@{
    LogicalPath = "scripts/get-phone-webcam-component-revision.ps1"
    FullPath = $revisionScriptPath
}
$inputs = @($inputs | Sort-Object LogicalPath)

$revisionHash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    foreach ($input in $inputs) {
        $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($input.LogicalPath)
        $revisionHash.AppendData($pathBytes)
        $revisionHash.AppendData([byte[]](0))
        $revisionHash.AppendData([System.IO.File]::ReadAllBytes($input.FullPath))
        $revisionHash.AppendData([byte[]](0))
    }
    [Convert]::ToHexString($revisionHash.GetHashAndReset()).ToLowerInvariant()
}
finally {
    $revisionHash.Dispose()
}
