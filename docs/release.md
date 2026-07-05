# Release Packaging and GitHub Asset Upsert

This document explains how to rebuild the Windows release assets, create the selected GitHub release when needed, and replace same-named assets on repeated runs.

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

## Create or replace GitHub release assets

Create the release if this is the first upload:

```powershell
gh release create v0.1.0 `
  --repo voltura/voltura-air `
  --target main `
  --title "Voltura Air v0.1.0" `
  --notes "Windows release assets for Voltura Air v0.1.0."
```

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

## Freeware release notes

Voltura Air is distributed as freeware. Release notes should avoid trial, license-key, premium, and paid-upgrade language.

Use wording like this for public release notes:

```text
Voltura Air is free software from Voltura AB. If it helps you, optional support links are available through Ko-fi and PayPal.
```

Support links are maintained in `.github/FUNDING.yml` and on the static product page.

## Unsigned installer status

Release assets are not code-signed. Do not claim that the installer or executable is signed.

Use wording like this when publishing download instructions:

```text
Windows may show an unknown publisher or Microsoft Defender SmartScreen warning because Voltura Air release assets are not code-signed. Download only from https://voltura.se/air or the official GitHub releases page.
```

Avoid adding workaround-heavy instructions that make the app look suspicious. Keep the message honest and direct.

## GitHub Actions

The `Build and upsert release assets` workflow performs the same release path on a Windows runner:

1. Install npm dependencies.
2. Install NSIS 3.12.0.
3. Run `npm run build`.
4. Run `npm test`.
5. Run `npm run package:win`.
6. Create the selected release if it does not exist.
7. Upload both the portable zip and installer, replacing same-named assets when present.

## Installer behavior

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Does not require administrator rights.
- Creates Start Menu shortcuts and an Apps & Features uninstall entry.
- Leaves Windows startup behavior to the in-app setting.
- Removes installed program files and shortcuts on uninstall.
- Keeps pairing and settings data under `%APPDATA%\Voltura Air`.


## Connection reliability release checks

Before publishing a release that touches pairing, WebSocket handling, protocol,
or input dispatch, verify these cases manually in addition to automated tests:

- normal QR pairing and saved-device reconnect;
- expired, stale, invalid, and missing pairing token feedback;
- host closed while the mobile app is connected;
- phone browser/PWA backgrounded and resumed;
- network interruption or IP/port change;
- input dispatch failure path shows unavailable/retrying instead of dead controls;
- browser storage cleanup requires re-pairing or reconnects visibly.
