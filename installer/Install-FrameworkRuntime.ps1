[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runtimeMajorMinor = '10.0'
$dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
$dotnetHost = Join-Path $dotnetRoot 'dotnet.exe'

function Test-RequiredRuntime {
    if (-not (Test-Path -LiteralPath $dotnetHost -PathType Leaf)) {
        return $false
    }

    $runtimes = & $dotnetHost --list-runtimes
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $desktopPattern = "^Microsoft\.WindowsDesktop\.App $([regex]::Escape($runtimeMajorMinor))\."
    $aspNetPattern = "^Microsoft\.AspNetCore\.App $([regex]::Escape($runtimeMajorMinor))\."
    return ($runtimes -match $desktopPattern) -and ($runtimes -match $aspNetPattern)
}

function Install-Runtime {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Uri
    )

    $installerPath = Join-Path $env:TEMP "VolturaAir-$Name-$runtimeMajorMinor-win-x64.exe"
    try {
        Write-Host "Downloading the .NET $Name runtime."
        Invoke-WebRequest -Uri $Uri -OutFile $installerPath

        $signature = Get-AuthenticodeSignature -FilePath $installerPath
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "The downloaded .NET $Name runtime installer did not have a valid Authenticode signature."
        }

        Write-Host "Installing the .NET $Name runtime."
        $process = Start-Process `
            -FilePath $installerPath `
            -ArgumentList @('/install', '/quiet', '/norestart') `
            -Verb RunAs `
            -Wait `
            -PassThru

        if ($process.ExitCode -notin 0, 3010) {
            throw "The .NET $Name runtime installer failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-RequiredRuntime)) {
    Install-Runtime `
        -Name 'WindowsDesktop' `
        -Uri "https://aka.ms/dotnet/$runtimeMajorMinor/windowsdesktop-runtime-win-x64.exe"
    Install-Runtime `
        -Name 'AspNetCore' `
        -Uri "https://aka.ms/dotnet/$runtimeMajorMinor/aspnetcore-runtime-win-x64.exe"
}

if (-not (Test-RequiredRuntime)) {
    throw ".NET $runtimeMajorMinor Desktop and ASP.NET Core runtimes were not available after installation."
}
