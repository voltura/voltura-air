# Release Packaging and GitHub Asset Upsert

This document describes how to prepare a Voltura Air version, verify it, build
Windows release assets, and create or update the corresponding GitHub release.

## Requirements

- Node.js and npm.
- .NET 10 SDK.
- NSIS 3.12 or later, with `makensis` available on `PATH`.
- GitHub CLI authenticated with access to `voltura/voltura-air` for manual release work.

To install NSIS with Chocolatey:

```powershell
choco install nsis --version=3.12.0 -y
```

## Prepare the release version

From the repository root, pass one semantic version to the release command:

```powershell
npm run release -- 0.3.0
```

Prerelease and build metadata are accepted when they use semantic-version syntax,
for example `0.3.0-beta.1`. Each numeric component must also fit a Windows version
resource.

The command updates these authoritative release values together:

- root `package.json` `version`;
- `apps/mobile-web/package.json` `version`;
- the root and mobile workspace versions in `package-lock.json`;
- `apps/windows-host/VolturaAir.Host.csproj` `<Version>` and explicit
  `AssemblyVersion`, `FileVersion`, and `InformationalVersion` values;
- `.github/workflows/release-zip.yml` defaults for `release_tag` and `version`.

The script validates every expected target before writing. If a target is missing,
ambiguous, or no longer has the expected structure, it stops with an error so the
release process can be updated deliberately.

The following values are not separate release versions:

- `.vscode/launch.json` uses VS Code's debug configuration schema version;
- `.vscode/tasks.json` uses VS Code's task configuration schema version.

Other release consumers are dynamic and do not require a version edit:

- Vite reads the mobile package version and defines `__APP_VERSION__`;
- the host project writes the numeric release core as `AssemblyVersion` and
  `FileVersion` (`0.3.0.0`) and the full semantic version as
  `InformationalVersion` (`0.3.0` or `0.3.0-beta.1`);
- Windows File Explorer displays the file version, product version, product name,
  company, and description on the Details tab for `VolturaAir.Host.exe` and
  `VolturaAir.Host.dll`;
- source revision hashes are disabled in `InformationalVersion`, so Explorer shows
  the exact semantic product version prepared by `npm run release`;
- `AppVersion` reads the assembly informational version at runtime;
- `scripts/package-win.ps1` reads the root package version by default;
- the NSIS script receives `APP_VERSION` and `APP_VERSION_QUAD` from the packaging script.

Review the resulting diff before committing. The preparation command does not
commit, tag, publish, or upload anything.

## Build, test, and package locally

Run release verification sequentially from the repository root:

```powershell
npm run build
npm test
npm run package:win
```

`npm run package:win` builds the mobile client, publishes the self-contained
Windows host, creates the portable zip, and compiles the NSIS installer. It reads
the version from the root `package.json` when `-Version` is omitted. Packaging then
runs `scripts/verify-windows-version.ps1` and fails if the host EXE, host DLL, or
installer exposes missing or stale Windows File Explorer version metadata.

For a prepared version and the default `win-x64` runtime, output files are named:

```text
artifacts/publish/VolturaAir-<version>-win-x64.zip
artifacts/publish/VolturaAir-Setup-<version>-win-x64.exe
```

To override the prepared version or runtime explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version <version> -Runtime win-x64
```

To rebuild the zip and installer from an existing publish directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version <version> -Runtime win-x64 -SkipBuild
```

## GitHub Actions release path

The `Build and upsert release assets` workflow performs the release path on a
Windows runner:

1. Install npm dependencies with `npm ci`.
2. Verify that the workflow `version` equals the committed root package version.
3. Verify that `release_tag` is `v<version>`.
4. Build and test the repository.
5. Package the portable zip and installer.
6. Validate the built host EXE, build-output host DLL, and installer Windows version
   resources against the workflow version.
7. Refresh the selected tag so GitHub source archives point to the workflow commit.
8. Create the release when it does not exist.
9. Upload both assets, replacing same-named assets when present.

After running `npm run release -- <version>` and committing the prepared files,
the workflow defaults already match the release.

## Create or replace GitHub release assets manually

Create a release when it does not exist:

```powershell
$version = "<version>"
gh release create "v$version" `
  --repo voltura/voltura-air `
  --target main `
  --title "Voltura Air v$version" `
  --notes "Windows release assets for Voltura Air v$version."
```

Upload both assets and overwrite files with the same names:

```powershell
$version = "<version>"
gh release upload "v$version" `
  "artifacts/publish/VolturaAir-$version-win-x64.zip" `
  "artifacts/publish/VolturaAir-Setup-$version-win-x64.exe" `
  --clobber `
  --repo voltura/voltura-air
```

## Freeware release notes

Voltura Air is distributed as freeware. Release notes should avoid trial,
license-key, premium, and paid-upgrade language.

Suggested wording:

```text
Voltura Air is free software from Voltura AB. If it helps you, optional support links are available through Ko-fi and PayPal.
```

## Unsigned installer status

Release assets are not code-signed. Do not claim that the installer or executable
is signed. Windows may show an unknown publisher or Microsoft Defender SmartScreen
warning. Direct users only to the official product page or GitHub releases page.

## Installer behavior

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Does not require administrator rights.
- Creates Start Menu shortcuts and an Apps & Features uninstall entry.
- Leaves Windows startup behavior to the in-app setting.
- Removes installed program files and shortcuts on uninstall.
- Keeps pairing and settings data under `%APPDATA%\Voltura Air`.

## Connection reliability release checks

For releases that touch pairing, WebSocket handling, protocol, or input dispatch,
verify these cases manually in addition to automated tests:

- normal QR pairing and saved-device reconnect;
- expired, stale, invalid, and missing pairing token feedback;
- host closed while the mobile app is connected;
- phone browser or installed PWA backgrounded and resumed;
- network interruption or IP/port change;
- input dispatch failure shows unavailable or retrying state instead of dead controls;
- Lock PC shows an accepted result or a specific permission, policy, unsupported, or Windows failure without closing the WebSocket;
- Blackout display covers all monitors without suspending the host and closes on local or remote input;
- Screen saver is visible only with an enabled, configured Windows screen saver and invokes the native Windows command;
- Turn off display warns that HDMI output will stop and that some PCs enter sleep or Modern Standby, requires confirmation, and reports the PC unavailable normally when Windows suspends the host; physical input may be required to wake;
- browser storage cleanup requires re-pairing or reconnects visibly.

## Final release sanity checks

Before announcing the release:

- confirm the preparation diff contains the intended version only;
- confirm build, tests, and packaging completed successfully;
- confirm both GitHub release assets exist with the expected names;
- confirm source downloads, zip, and installer are based on the same commit;
- install the downloaded installer on a clean or disposable Windows profile;
- inspect the downloaded installer and installed `VolturaAir.Host.exe` in File
  Explorer Properties > Details and confirm the expected file/product versions;
- confirm the host and mobile UI display the expected version;
- inspect `VolturaAir.Host.exe` and `VolturaAir.Host.dll` in File Explorer and
  confirm File version and Product version match the release;
- confirm a phone can pair from a fresh QR code and reconnect after host restart;
- confirm Preferences accordions are themed in light and dark mode, start collapsed, and keep only one section open;
- confirm Diagnostics keeps log filters and actions visible while only log records scroll, and verify log enable, retention, filtering, copy, folder, and delete behavior;
- update public release/download text when product-facing behavior changed.
