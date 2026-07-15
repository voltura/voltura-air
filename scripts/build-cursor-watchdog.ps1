[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourcePath = Join-Path $repoRoot 'apps\cursor-watchdog\VolturaAir.CursorWatchdog.c'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Native cursor watchdog source was not found: $sourcePath"
}

$hostProjectPath = Join-Path $repoRoot 'apps\windows-host\VolturaAir.Host.csproj'
$iconPath = Join-Path $repoRoot 'apps\windows-host\Assets\VolturaAir.ico'
if (-not (Test-Path -LiteralPath $hostProjectPath -PathType Leaf) -or -not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    throw "The Voltura Air host metadata or icon was not found."
}

[xml]$hostProject = Get-Content -LiteralPath $hostProjectPath -Raw
$hostProperties = $hostProject.Project.PropertyGroup | Where-Object { $_.FileVersion -and $_.InformationalVersion } | Select-Object -First 1
if ($null -eq $hostProperties) {
    throw "The Voltura Air host version properties were not found."
}

$fileVersion = [string]$hostProperties.FileVersion
$informationalVersion = [string]$hostProperties.InformationalVersion
$fileVersionParts = @($fileVersion -split '\.')
if ($fileVersionParts.Count -ne 4 -or @($fileVersionParts | Where-Object { $_ -notmatch '^\d+$' }).Count -gt 0) {
    throw "The host file version '$fileVersion' is not a four-part numeric Windows version."
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    throw "Visual Studio Build Tools were not found. Install the Desktop development with C++ workload."
}

$visualStudioPath = (& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw "Visual Studio Build Tools with the x64 C++ toolset were not found. Install the Desktop development with C++ workload."
}

$developerCommandPath = Join-Path $visualStudioPath 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $developerCommandPath -PathType Leaf)) {
    throw "Visual Studio developer command script was not found: $developerCommandPath"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("voltura-air-watchdog-" + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $objectPath = Join-Path $temporaryDirectory 'VolturaAir.CursorWatchdog.obj'
    $resourceScriptPath = Join-Path $temporaryDirectory 'VolturaAir.CursorWatchdog.rc'
    $resourceObjectPath = Join-Path $temporaryDirectory 'VolturaAir.CursorWatchdog.res'
    $resourceIconPath = $iconPath.Replace('\', '/')
    $resourceText = @"
#include <windows.h>

IDI_APPICON ICON "$resourceIconPath"

VS_VERSION_INFO VERSIONINFO
 FILEVERSION $($fileVersionParts -join ',')
 PRODUCTVERSION $($fileVersionParts -join ',')
 FILEFLAGSMASK 0x3fL
 FILEFLAGS 0x0L
 FILEOS 0x40004L
 FILETYPE 0x1L
 FILESUBTYPE 0x0L
BEGIN
    BLOCK "StringFileInfo"
    BEGIN
        BLOCK "000004B0"
        BEGIN
            VALUE "CompanyName", "Voltura AB"
            VALUE "Comments", "Voltura Air Windows host"
            VALUE "FileDescription", "Voltura Air Cursor Watchdog"
            VALUE "FileVersion", "$fileVersion"
            VALUE "InternalName", "VolturaAir.CursorWatchdog"
            VALUE "LegalCopyright", "Copyright (c) Voltura AB"
            VALUE "OriginalFilename", "VolturaAir.CursorWatchdog.exe"
            VALUE "ProductName", "Voltura Air"
            VALUE "ProductVersion", "$informationalVersion"
        END
    END
    BLOCK "VarFileInfo"
    BEGIN
        VALUE "Translation", 0x0000, 1200
    END
END
"@
    [System.IO.File]::WriteAllText($resourceScriptPath, $resourceText, [System.Text.UTF8Encoding]::new($false))

    $compileCommand = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && rc.exe /nologo /fo"{1}" "{2}" && cl.exe /nologo /std:c17 /O2 /MT /W4 /WX /DUNICODE /D_UNICODE /Fo"{3}" "{4}" "{1}" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib /OUT:"{5}"' -f `
        $developerCommandPath,
        $resourceObjectPath,
        $resourceScriptPath,
        $objectPath,
        $sourcePath,
        $resolvedOutputPath

    & $env:ComSpec /d /s /c $compileCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Native cursor watchdog compilation failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf)) {
        throw "Native cursor watchdog compilation did not produce: $resolvedOutputPath"
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
