# Release Packaging and GitHub Asset Upsert

This document explains how to prepare a new Voltura Air version, rebuild the Windows release assets, create the selected GitHub release when needed, refresh the release tag for source archives, and replace same-named assets on repeated runs.

## Requirements

- Node.js and npm.
- .NET 10 SDK.
- NSIS 3.12 or later, with `makensis` available on `PATH`.
- GitHub CLI authenticated with access to `voltura/voltura-air`.

To install NSIS with Chocolatey:

```powershell
choco install nsis --version=3.12.0 -y
```

## Version bump checklist

For a normal release, use the same semantic version everywhere. The GitHub release tag must be `v<version>`, for example `v0.2.0`.

Before creating release assets, update these version references:

- root `package.json` `version`;
- `apps/mobile-web/package.json` `version`;
- `package-lock.json` package entries by running `npm install` after changing package versions;
- `apps/windows-host/VolturaAir.Host.csproj` `<Version>`;
- `.github/workflows/release-zip.yml` workflow dispatch defaults for `release_tag` and `version`;
- tests or snapshots that stub or assert `__APP_VERSION__`, such as `apps/mobile-web/src/components/SettingsDrawer.test.tsx`;
- protocol/docs examples that show `hostVersion`, release tags, or release asset names;
- `AGENTS.md` if the release process or commands changed.

Then inspect the tree for stale references by replacing `<previous-version>` with the version you are releasing from:

```powershell
git grep "<previous-version>"
git grep "v<previous-version>"
```

## Build, test, and package locally

From the repository root, run the release verification commands sequentially:

```powershell
npm install
npm run build
npm test
npm run package:win
```

`npm run package:win` builds the mobile web client, publishes the self-contained Windows host, creates the portable zip, and compiles the NSIS installer. When `-Version` is omitted, `scripts/package-win.ps1` reads the version from the root `package.json`.

For version `0.2.0` and runtime `win-x64`, the default output files are:

```text
artifacts/publish/VolturaAir-0.2.0-win-x64.zip
artifacts/publish/VolturaAir-Setup-0.2.0-win-x64.exe
```

To package a specific version or runtime:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version 0.2.0 -Runtime win-x64
```

To rebuild the zip and installer from an existing publish directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version 0.2.0 -Runtime win-x64 -SkipBuild
```

## Create or replace GitHub release assets manually

Create the release if this is the first upload:

```powershell
gh release create v0.2.0 `
  --repo voltura/voltura-air `
  --target main `
  --title "Voltura Air v0.2.0" `
  --notes "Windows release assets for Voltura Air v0.2.0."
```

Upload both assets and overwrite any existing files with the same names:

```powershell
$version = "0.2.0"
gh release upload v$version `
  artifacts/publish/VolturaAir-$version-win-x64.zip `
  artifacts/publish/VolturaAir-Setup-$version-win-x64.exe `
  --clobber `
  --repo voltura/voltura-air
```

If you need to update the release notes too:

```powershell
gh release edit v0.2.0 --notes "Updated Windows release assets." --repo voltura/voltura-air
```

## GitHub Actions release path

The `Build and upsert release assets` workflow performs the same release path on a Windows runner:

1. Install npm dependencies with `npm ci`.
2. Install NSIS 3.12.0.
3. Run `npm run build`.
4. Run `npm test`.
5. Run `npm run package:win -- -Version <version> -Runtime <runtime>`.
6. Refresh the selected release tag so GitHub source archives point to the current workflow commit.
7. Create the selected release if it does not exist.
8. Upload both the portable zip and installer, replacing same-named assets when present.

When dispatching the workflow for `0.2.0`, use:

```text
release_tag: v0.2.0
version: 0.2.0
runtime: win-x64
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

## Installer behavior

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Does not require administrator rights.
- Creates Start Menu shortcuts and an Apps & Features uninstall entry.
- Leaves Windows startup behavior to the in-app setting.
- Removes installed program files and shortcuts on uninstall.
- Keeps pairing and settings data under `%APPDATA%\Voltura Air`.

## Connection reliability release checks

Before publishing a release that touches pairing, WebSocket handling, protocol, or input dispatch, verify these cases manually in addition to automated tests:

- normal QR pairing and saved-device reconnect;
- expired, stale, invalid, and missing pairing token feedback;
- host closed while the mobile app is connected;
- phone browser/PWA backgrounded and resumed;
- network interruption or IP/port change;
- input dispatch failure path shows unavailable/retrying instead of dead controls;
- browser storage cleanup requires re-pairing or reconnects visibly.

## Final release sanity checks

Before announcing the release:

- confirm the workflow run completed successfully;
- confirm both GitHub release assets exist and use the expected names;
- confirm the release source downloads are based on the same commit as the zip and installer build;
- download the installer from the release page and install it on a clean or disposable Windows profile;
- confirm the app shows the expected version in the UI and host diagnostics;
- confirm a phone can pair from the fresh QR code and reconnect after the host restarts;
- update public-facing release/download text if the static site or README changed.
