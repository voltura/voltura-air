# Release

This document describes how to prepare a Voltura Air version, verify it, build
Windows release assets, and publish a GitHub release.

## Quick release

Prepare `docs/release-notes.md` for the next version, commit that notes change,
and start from a clean `main` branch. Create an audited GitHub draft with:

```powershell
npm run release:local
```

The default advances the existing stable odometer version. Supply an explicit
version when needed:

```powershell
npm run release:local -- 0.8.0
```

The command leaves the audited release as a draft. Append `latest` to publish a
stable version immediately as GitHub's Latest release:

```powershell
npm run release:local -- latest
npm run release:local -- 0.8.0 latest
```

The local release validates its environment, prepares the version, regenerates
branding and screenshots, runs all tests, builds and validates the ZIP and both
installers, commits and pushes the generated release changes, rebuilds from the
final commit, creates or resumes a matching draft, audits its assets, and
publishes `docs/site`. Only the `latest` mode makes the GitHub release public.
Prerelease versions can be prepared as drafts but cannot be marked Latest.

## Local release prerequisites

- Windows with Node.js/npm, the .NET 10 SDK, Git, and NSIS available;
- an authenticated GitHub CLI with write access to `voltura/voltura-air`;
- the one.com SFTP password stored with `npm run publish:site:password`;
- a clean `main` worktree with no merge or rebase in progress and no divergence
  from `origin/main`;
- no workflow YAML under `.github/workflows`;
- one committed, publishable target section in `docs/release-notes.md`.

Generated binaries remain under `artifacts/publish`. The command prints SHA-256
hashes for the ZIP and both installers after the draft or release is complete.

## Prepare the release version

From the repository root, pass one semantic version to the release command:

```powershell
npm run release -- 0.6.0
```

For the usual release sequence, use:

```powershell
npm run release:bump
```

It advances the current stable version as an odometer: `0.6.7` becomes `0.6.8`,
`0.6.9` becomes `0.7.0`, and `0.9.9` becomes `1.0.0`. It supports one-digit
minor and patch components only; use `npm run release -- <version>` when choosing
another semantic version explicitly.

Run the complete local release preparation, branding generation, and site
publication sequence with:

```powershell
npm run release:full
```

To commit and push those generated release changes after the sequence completes,
run:

```powershell
npm run release:full -- auto
```

Both full-release modes require a clean working tree before they change files or
publish the site. Automatic mode also requires a configured Git author and
tracking branch. It commits `Release version <version>` and runs a normal `git
push`; it never force-pushes or includes pre-existing local changes.
Before changing the version or publishing anything, both modes run
`npm run size:check` and stop if a refactor introduced an unresolved strong
source-size warning.

The command accepts semantic versions such as `0.6.0` and `0.6.0-beta.1`.
Numeric components must fit a Windows version resource.

The command updates these authoritative release values together:

- root `package.json` `version`;
- `apps/mobile-web/package.json` `version`;
- the root and mobile workspace versions in `package-lock.json`;
- `apps/windows-host/VolturaAir.Host.csproj` `<Version>` and explicit
  `AssemblyVersion`, `FileVersion`, and `InformationalVersion` values;

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

## Optional standalone preflight

The local release command performs the required checks. To run the same major
boundaries independently, use these commands sequentially from the repository
root:

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

## GitHub Actions state

The Release and Quality workflow sources are stored under `scripts/legacy/`. No
workflow YAML exists under `.github/workflows`, so GitHub cannot discover or run
repository workflows. GitHub also records both workflows as manually disabled.
The local release command refuses to run if workflow YAML has been restored,
preventing local and hosted publication paths from competing.

To deliberately copy both workflows back to `.github/workflows` for review and
possible future re-enabling, run:

```powershell
npm run actions:restore
```

The restore command refuses to overwrite existing workflow files. Restoring the
files changes repository state but does not inspect or change GitHub's remote
workflow setting; review triggers and remote state before committing them.

## Maintain release notes

Maintain `docs/release-notes.md` newest first. Each release uses one heading such
as `## v0.8.0`, followed by concise user-facing bullets. The local release
command refuses to prepare a version when its section is missing, duplicated,
empty, or contains only an editorial HTML comment.

```markdown
## v0.8.0

- <New capability or important user-visible improvement.>
- <Important defect, recovery, pairing, or compatibility improvement.>
- <Any setup, compatibility, alpha-feature, or known-limitation note.>
```

Include only new user-facing features and fixes for defects that users could
actually experience. Omit refactors, code organization, tests, CI, tooling,
documentation, dependency maintenance, and other internal work. End every
version section with these paragraphs:

```markdown
Voltura Air is free software from Voltura AB. If it helps you, optional support is available through [Ko-fi](https://ko-fi.com/voltura) or [PayPal](https://www.paypal.me/voltura).

Release binaries are not code-signed. Windows may show an unknown-publisher or Microsoft Defender SmartScreen warning. Download release files only from the official Voltura Air website or GitHub release page.
```

The local command requires at least one user-facing change in addition to these
paragraphs, then adds download guidance and a changelog link to the GitHub body.
The generated body places invisible synchronization markers around the editable
notes and notices. Preserve those HTML comments when editing a draft in GitHub.

After publishing the draft manually in GitHub, synchronize the published
editorial block back into its matching local section:

```powershell
npm run release:sync-release-notes
```

With no argument, the command selects GitHub's Latest published release. Select
another published stable or prerelease version explicitly with:

```powershell
npm run release:sync-release-notes -- 0.8.0
```

Synchronization requires a clean worktree, exactly one marker pair, both exact
notices, user-facing content, and one matching local version section. It updates
only `docs/release-notes.md`, preserves its line endings, and does not edit
GitHub, commit, push, bump a version, or publish anything. Review and commit the
resulting documentation diff manually. Repeating the command when both copies
already match succeeds without rewriting the file.

When a release changes pairing, authentication, permissions, or network
exposure, state the practical security impact without implying transport
protection. Voltura Air remains an HTTP/WebSocket application for trusted local
networks; link to [SECURITY.md](../SECURITY.md) for the LAN and same-user trust
boundary.

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
