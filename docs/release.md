# Release

This document describes how to prepare a Voltura Air version, verify it, build
Windows release assets, and publish a GitHub release.

## Quick release

```powershell
npm run release -- 0.6.0
npm run branding:generate
```

Review, commit, and push to `main`. Run **Publish Voltura Air release**
in GitHub Actions. After it succeeds, upload the contents of `docs/site` to
`voltura.se/air`. Increment every later public release (`0.6.1`, `0.6.2`, then
`0.7.0` for the next larger milestone).

## Prepare the release version

From the repository root, pass one semantic version to the release command:

```powershell
npm run release -- 0.6.0
```

The command accepts semantic versions such as `0.6.0` and `0.6.0-beta.1`.
Numeric components must fit a Windows version resource.

The command updates these authoritative release values together:

- root `package.json` `version`;
- `apps/mobile-web/package.json` `version`;
- the root and mobile workspace versions in `package-lock.json`;
- `apps/windows-host/VolturaAir.Host.csproj` `<Version>` and explicit
  `AssemblyVersion`, `FileVersion`, and `InformationalVersion` values;
- `.github/workflows/release.yml` defaults for `release_tag` and `version`.

The script validates every target before writing and stops if the expected file
structure has changed.

Build and packaging consumers read those prepared values:

- Vite reads the mobile package version and defines `__APP_VERSION__`;
- the host project writes the numeric release core as `AssemblyVersion` and
  `FileVersion` (`0.6.0.0`) and the full semantic version as
  `InformationalVersion` (`0.6.0` or `0.6.0-beta.1`);
- Windows File Explorer displays the prepared file and product versions for
  `VolturaAir.Host.exe` and `VolturaAir.Host.dll`;
- `AppVersion` reads the assembly informational version at runtime;
- `scripts/package-win.ps1` reads the root package version by default;
- the NSIS script receives `APP_VERSION` and `APP_VERSION_QUAD` from the packaging script.

Review the resulting diff before committing. The command only updates files.

## Optional local preflight

GitHub Actions performs the required build, tests, packaging, and metadata
validation. To catch failures before pushing, optionally run these commands
sequentially from the repository root:

```powershell
npm run build
npm test
npm run package:win
```

`npm run package:win` reads the root package version, builds the mobile client,
publishes both host variants, compiles the cursor watchdog, creates the portable
ZIP and both NSIS installers, and validates their Windows metadata.

Build only the framework-dependent installer with:

```powershell
npm run package:win:small
```

Validate both installer definitions without compression with:

```powershell
npm run package:win:test
```

This writes test installers under `artifacts/test`:

```text
artifacts/test/VolturaAir-Setup-<version>-win-x64-test-uncompressed.exe
artifacts/test/VolturaAir-Setup-<version>-win-x64-full-test-uncompressed.exe
```

Never publish files from `artifacts/test`.

Reuse existing publish directories for an NSIS-only check with:

```powershell
npm run package:win:test -- -SkipBuild
```

For a prepared version and the default `win-x64` runtime, output files are named:

```text
artifacts/publish/VolturaAir-<version>-win-x64.zip
artifacts/publish/VolturaAir-Setup-<version>-win-x64.exe
artifacts/publish/VolturaAir-Setup-<version>-win-x64-full.exe
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

Run **Publish Voltura Air release** from the prepared `main` commit. The workflow
defaults match the version written by `npm run release`.

The workflow verifies its version and tag inputs, runs tests, packages and
validates the Windows assets, then creates the tag and GitHub release. It rejects
an existing tag. Use a new version for every published build. Versions with a
prerelease suffix are published as GitHub prereleases.

## Create GitHub release assets manually

First confirm that the tag and release do not already exist. If either exists,
prepare a new version rather than deleting or replacing it. Create a new release:

```powershell
$version = "<version>"
gh release create "v$version" `
  --repo voltura/voltura-air `
  --target main `
  --title "Voltura Air v$version" `
  --notes "Windows release assets for Voltura Air v$version." `
  "artifacts/publish/VolturaAir-$version-win-x64.zip" `
  "artifacts/publish/VolturaAir-Setup-$version-win-x64.exe" `
  "artifacts/publish/VolturaAir-Setup-$version-win-x64-full.exe"
```

Add `--prerelease` when the semantic version contains a prerelease suffix.

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

- Both installers install Voltura Air per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- `VolturaAir-Setup-<version>-win-x64.exe` downloads the .NET 10 Windows Desktop and ASP.NET Core runtimes when they are missing. This requires an internet connection in that case and can require Windows administrator approval because the .NET runtimes are installed for the PC.
- `VolturaAir-Setup-<version>-win-x64-full.exe` includes all required components, works without an internet connection, and does not require administrator rights.
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
- confirm all three GitHub release assets exist with the expected names;
- install both downloaded installers on clean or disposable Windows profiles; verify the default installer obtains missing runtimes and the full installer works without a runtime download;
- inspect the downloaded installers, installed `VolturaAir.Host.exe`, and
  `VolturaAir.Host.dll` in File Explorer Properties > Details and confirm the
  expected file and product versions;
- confirm the host and mobile UI display the expected version;
- confirm a phone can pair from a fresh QR code and reconnect after host restart;
- update public release/download text when product-facing behavior changed.
