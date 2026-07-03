# Release Packaging and GitHub Asset Replacement

This document explains how to rebuild the Windows release assets and replace them on an existing GitHub release.

## Requirements

- Node.js and npm.
- .NET 10 SDK.
- NSIS 3.12 or later, with `makensis` available on `PATH`.
- GitHub CLI authenticated with access to `voltura/voltura-air`.

To install NSIS with Chocolatey:

```powershell
choco install nsis --version=3.12.0 -y
```

## Build, test, and package

From the repository root, run the release verification commands sequentially:

```powershell
npm install
npm run build
npm test
npm run package:win
```

`npm run package:win` builds the mobile web client, publishes the self-contained Windows host, creates the portable zip, and compiles the NSIS installer.

The default output files are:

```text
artifacts/publish/VolturaAir-0.1.0-win-x64.zip
artifacts/publish/VolturaAir-Setup-0.1.0-win-x64.exe
```

To package a specific version or runtime:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version 0.1.0 -Runtime win-x64
```

To rebuild the zip and installer from an existing publish directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -SkipBuild
```

## Replace GitHub release assets

Upload both assets and overwrite any existing files with the same names:

```powershell
$version = "0.1.0"
gh release upload v$version `
  artifacts/publish/VolturaAir-$version-win-x64.zip `
  artifacts/publish/VolturaAir-Setup-$version-win-x64.exe `
  --clobber `
  --repo voltura/voltura-air
```

If you need to update the release notes too:

```powershell
gh release edit v0.1.0 --notes "Updated Windows release assets." --repo voltura/voltura-air
```

## GitHub Actions

The `Build and replace release assets` workflow performs the same release path on a Windows runner:

1. Install npm dependencies.
2. Install NSIS 3.12.0.
3. Run `npm run build`.
4. Run `npm test`.
5. Run `npm run package:win`.
6. Upload both the portable zip and installer to the selected release.

## Installer behavior

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Does not require administrator rights.
- Creates Start Menu shortcuts and an Apps & Features uninstall entry.
- Leaves Windows startup behavior to the in-app setting.
- Removes installed program files and shortcuts on uninstall.
- Keeps pairing and settings data under `%APPDATA%\Voltura Air`.
