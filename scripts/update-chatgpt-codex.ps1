[CmdletBinding()]
param(
    [switch]$Scheduled
)

# Updates only the ChatGPT desktop package (which contains Codex). It queries
# the package-retrieval service used by the official ChatGPT installer and uses
# the Windows package deployment API directly; it never launches Microsoft Store.
$ErrorActionPreference = 'Stop'
$ProductId = '9PLM9XGG6VKS'
$PackageFamilyName = 'OpenAI.Codex_2p2nqsd0c76g0'
$ExpectedPackagePrefix = 'OpenAI.Codex_'
$ExpectedPublisherId = '2p2nqsd0c76g0'
$PackageServiceHost = 'sfdataservice.microsoft.com'
$LogDirectory = Join-Path $env:LOCALAPPDATA 'OpenAI\ChatGPTCodexUpdater\Logs'

New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
$startedAt = Get-Date
$logFile = Join-Path $LogDirectory ("Update-{0:yyyy-MM-dd_HH-mm-ss}.log" -f $startedAt)

function Write-Status {
    param([string]$Message)
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

function Get-InstalledApp {
    $packages = @(Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object {
        $_.PackageFamilyName -eq $PackageFamilyName
    })
    if ($packages.Count -eq 0) { return $null }
    return $packages | Sort-Object Version -Descending | Select-Object -First 1
}

function Get-OsArchitectureToken {
    # RuntimeInformation.OSArchitecture is not guaranteed to be populated in
    # Windows PowerShell 5.1. The Windows-provided environment variables are.
    $architecture = $env:PROCESSOR_ARCHITEW6432
    if ([string]::IsNullOrWhiteSpace($architecture)) {
        $architecture = $env:PROCESSOR_ARCHITECTURE
    }

    switch ($architecture.ToUpperInvariant()) {
        'AMD64' { return 'x64' }
        'ARM64' { return 'arm64' }
        'X86' { return 'x86' }
        default { throw "Unsupported Windows architecture: $architecture" }
    }
}

function Get-WindowsDeviceFamilyVersion {
    # The package service expects Windows' packed major.minor.build.revision
    # number. AnalyticsInfo.VersionInfo can be null in an interactive
    # Windows PowerShell session, so derive the same value from the OS instead.
    $version = [Environment]::OSVersion.Version
    $revision = [Math]::Max($version.Revision, 0)
    return [uint64]$version.Major * [uint64]281474976710656 +
        [uint64]$version.Minor * [uint64]4294967296 +
        [uint64]$version.Build * [uint64]65536 +
        [uint64]$revision
}

function Wait-WinRtDeploymentOperation {
    param([object]$Operation)

    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
        $_.GetGenericArguments().Count -eq 2 -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperationWithProgress`2'
    } | Select-Object -First 1
    if ($null -eq $asTask) { throw 'Windows Runtime task adapter was not found.' }

    $deploymentResultType = [Windows.Management.Deployment.DeploymentResult,Windows.Management.Deployment,ContentType=WindowsRuntime]
    $deploymentProgressType = [Windows.Management.Deployment.DeploymentProgress,Windows.Management.Deployment,ContentType=WindowsRuntime]
    $task = $asTask.MakeGenericMethod($deploymentResultType, $deploymentProgressType).Invoke($null, @($Operation))
    return $task.GetAwaiter().GetResult()
}

try {
    Start-Transcript -Path $logFile -Force | Out-Null
    Write-Status 'Starting autonomous ChatGPT/Codex update check.'
    Write-Status "Log: $logFile"

    $installedBefore = Get-InstalledApp
    if ($null -eq $installedBefore) {
        Write-Status "Installed package: not found ($PackageFamilyName)."
    }
    else {
        Write-Status "Installed package: $($installedBefore.Name) version $($installedBefore.Version)"
        Write-Status "Installed at: $($installedBefore.InstallLocation)"
    }

    $architecture = Get-OsArchitectureToken
    $platformVersion = Get-WindowsDeviceFamilyVersion
    $serviceUri = "https://$PackageServiceHost/package-retrieval/Packages?productId=$ProductId&architecture=$architecture&platformName=Windows.Desktop&platformVersion=$platformVersion"
    Write-Status "Checking the official ChatGPT package source at $serviceUri"
    $online = (Invoke-RestMethod -Uri $serviceUri -TimeoutSec 60).package
    if ($null -eq $online) { throw 'The official package source returned no ChatGPT package.' }

    $packageUri = [uri]$online.url
    $expectedPublisherSuffix = "_{0}$" -f $ExpectedPublisherId
    if ($online.fullName -notlike "$ExpectedPackagePrefix*" -or $online.fullName -notmatch $expectedPublisherSuffix -or
        $packageUri.Scheme -ne 'https' -or $packageUri.Host -ne $PackageServiceHost -or -not $packageUri.AbsolutePath.EndsWith('.Msix')) {
        throw "The package source returned an unexpected package identity: $($online.fullName)"
    }

    $onlineVersion = [version]$online.version
    Write-Status "Online package found: $($online.fullName)"
    Write-Status "Online version: $onlineVersion; size: $($online.sizeInBytes) bytes; found at $(Get-Date)"

    if ($null -ne $installedBefore -and $onlineVersion -le [version]$installedBefore.Version) {
        Write-Status "Installation result: CURRENT - installed version $($installedBefore.Version) is the same as or newer than the official package."
        exit 0
    }

    Write-Status "Installing only ChatGPT/Codex version $onlineVersion without opening Microsoft Store."
    $packageManager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
    $dependencies = [System.Collections.Generic.List[uri]]::new()
    $options = [Windows.Management.Deployment.DeploymentOptions]::ForceTargetApplicationShutdown
    $operation = $packageManager.AddPackageAsync($packageUri, $dependencies, $options)
    $deploymentResult = Wait-WinRtDeploymentOperation $operation
    if ($deploymentResult.ExtendedErrorCode -ne 0) {
        throw "Windows package deployment failed (0x{0:X8}): {1}" -f (([int64]$deploymentResult.ExtendedErrorCode -band 0xffffffffL)), $deploymentResult.ErrorText
    }

    Start-Sleep -Seconds 5
    $installedAfter = Get-InstalledApp
    if ($null -eq $installedAfter) { throw "ChatGPT/Codex package was not found after deployment." }
    if ([version]$installedAfter.Version -lt $onlineVersion) {
        throw "Deployment completed but version $($installedAfter.Version) is older than expected $onlineVersion."
    }
    Write-Status "Installation result: SUCCESS - installed version $($installedAfter.Version)."
}
catch {
    Write-Status "Installation result: FAILED - $($_.Exception.Message)"
    Write-Status "Failure detail: $($_.ScriptStackTrace)"
    Write-Status "Failed at $(Get-Date). See log: $logFile"
    exit 1
}
finally {
    if (Get-Command Stop-Transcript -ErrorAction SilentlyContinue) {
        try { Stop-Transcript | Out-Null } catch { }
    }
}
