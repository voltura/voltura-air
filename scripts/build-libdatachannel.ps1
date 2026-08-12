#requires -Version 7.6 -PSEdition Core

param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

$libdatachannelCommit = "443f6934d9007eb7076ab7825ba330f355fcbead"
$vcpkgCommit = "dc597a1a553bf65d2883ac61efa5a42db41cdd51"
$expectedSubmodules = @{
    "deps/json" = "55f93686c01528224f448c19128836e7df245f72"
    "deps/libjuice" = "3c40a3545b6b1b62c7adee7f8f2bd58aa290afd6"
    "deps/libsrtp" = "24b3bf8f19b6f5ab4cd2bcceb4f4064efca86fd5"
    "deps/plog" = "94899e0b926ac1b0f4750bfbd495167b4a6ae9ef"
    "deps/usrsctp" = "fec583d54493f879d2ae44a743423bf8a04371ab"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ("artifacts\native\libdatachannel-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$buildRoot = Join-Path $env:TEMP ("voltura-libdatachannel-build-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $buildRoot | Out-Null
$sourceDirectory = Join-Path $buildRoot "libdatachannel"
$vcpkgDirectory = Join-Path $buildRoot "vcpkg"
$vcpkgInstallDirectory = Join-Path $buildRoot "vcpkg-installed"
$buildDirectory = Join-Path $buildRoot "build"

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "Visual Studio Installer's vswhere.exe was not found."
}
$visualStudio = & $vswhere -latest -products * -version '[18.9,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw "Visual Studio 2026 18.9 or newer with the x64 C++ build tools was not found."
}
$cmake = Join-Path $visualStudio "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
if (-not (Test-Path -LiteralPath $cmake -PathType Leaf)) {
    throw "Visual Studio's bundled CMake was not found: $cmake"
}

try {
git clone --quiet https://github.com/microsoft/vcpkg.git $vcpkgDirectory
if ($LASTEXITCODE -ne 0) { throw "vcpkg clone failed." }
git -C $vcpkgDirectory checkout --quiet $vcpkgCommit
if ($LASTEXITCODE -ne 0) { throw "vcpkg checkout failed." }
& (Join-Path $vcpkgDirectory "bootstrap-vcpkg.bat") -disableMetrics
if ($LASTEXITCODE -ne 0) { throw "vcpkg bootstrap failed." }

$manifest = @{
    name = "voltura-libdatachannel-build"
    version = "0.24.5"
    dependencies = @("openssl")
    overrides = @(@{ name = "openssl"; version = "3.6.3"; "port-version" = 0 })
} | ConvertTo-Json -Depth 5
$configuration = @{
    "default-registry" = @{
        kind = "builtin"
        baseline = $vcpkgCommit
    }
} | ConvertTo-Json -Depth 5
$manifest | Set-Content -LiteralPath (Join-Path $buildRoot "vcpkg.json") -Encoding utf8
$configuration | Set-Content -LiteralPath (Join-Path $buildRoot "vcpkg-configuration.json") -Encoding utf8

& (Join-Path $vcpkgDirectory "vcpkg.exe") install `
    --triplet x64-windows-static `
    "--x-manifest-root=$buildRoot" `
    "--x-install-root=$vcpkgInstallDirectory" `
    --clean-after-build
if ($LASTEXITCODE -ne 0) { throw "Static OpenSSL build failed." }

git clone --quiet https://github.com/paullouisageneau/libdatachannel.git $sourceDirectory
if ($LASTEXITCODE -ne 0) { throw "libdatachannel clone failed." }
git -C $sourceDirectory checkout --quiet $libdatachannelCommit
if ($LASTEXITCODE -ne 0) { throw "libdatachannel checkout failed." }
git -C $sourceDirectory submodule update --init --recursive --depth 1
if ($LASTEXITCODE -ne 0) { throw "libdatachannel submodule checkout failed." }

foreach ($entry in $expectedSubmodules.GetEnumerator()) {
    $actual = (git -C (Join-Path $sourceDirectory $entry.Key) rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -cne $entry.Value) {
        throw "Unexpected $($entry.Key) commit '$actual'; expected '$($entry.Value)'."
    }
}

$prefixPath = Join-Path $vcpkgInstallDirectory "x64-windows-static"
& $cmake `
    -S $sourceDirectory `
    -B $buildDirectory `
    -G "Visual Studio 18 2026" `
    -A x64 `
    -DBUILD_SHARED_LIBS=ON `
    -DBUILD_SHARED_DEPS_LIBS=OFF `
    -DNO_WEBSOCKET=ON `
    -DNO_EXAMPLES=ON `
    -DNO_TESTS=ON `
    -DOPENSSL_USE_STATIC_LIBS=TRUE `
    -DCMAKE_POLICY_DEFAULT_CMP0091=NEW `
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded$<$<CONFIG:Debug>:Debug>' `
    "-DCMAKE_PREFIX_PATH=$prefixPath"
if ($LASTEXITCODE -ne 0) { throw "libdatachannel configuration failed." }

& $cmake --build $buildDirectory --config Release --target datachannel
if ($LASTEXITCODE -ne 0) { throw "libdatachannel build failed." }

$builtDll = Join-Path $buildDirectory "Release\datachannel.dll"
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "The expected datachannel.dll was not produced."
}
$destinationDll = Join-Path $OutputDirectory "datachannel.dll"
Copy-Item -LiteralPath $builtDll -Destination $destinationDll -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "LICENSE") -Destination (Join-Path $OutputDirectory "libdatachannel-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "deps\libjuice\LICENSE") -Destination (Join-Path $OutputDirectory "libjuice-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "deps\libsrtp\LICENSE") -Destination (Join-Path $OutputDirectory "libsrtp-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "deps\usrsctp\LICENSE.md") -Destination (Join-Path $OutputDirectory "usrsctp-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "deps\plog\LICENSE") -Destination (Join-Path $OutputDirectory "plog-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory "deps\json\LICENSE.MIT") -Destination (Join-Path $OutputDirectory "nlohmann-json-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $prefixPath "share\openssl\copyright") -Destination (Join-Path $OutputDirectory "openssl-LICENSE.txt") -Force

$hash = Get-FileHash -LiteralPath $destinationDll -Algorithm SHA256
Write-Host "Built audited libdatachannel payload: $OutputDirectory"
Write-Host "datachannel.dll SHA-256: $($hash.Hash.ToLowerInvariant())"
Write-Host "Review the ABI, dependency list, tests, hash, licenses, and SOURCE.txt before replacing the shipped binary."
}
finally {
    if (Test-Path -LiteralPath $buildRoot -PathType Container) {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force
    }
}
