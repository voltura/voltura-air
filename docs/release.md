# Release

Every published build uses a new semantic version. This public procedure prepares,
verifies, packages, and publishes the Windows/GitHub release. Voltura-operated Relay
and website deployment is owned by the private `voltura-air-service` repository.

## Complete release

Prepare and commit the target section in
[release notes](release-notes.md), then start from a clean, synchronized `main`.

Publish a stable release as GitHub Latest:

```powershell
npm run release:full
npm run release:full -- 0.8.0
```

Run the same release gate but leave GitHub in draft:

```powershell
npm run release:draft
npm run release:draft -- 0.8.0
```

The command validates prerequisites and repository state, prepares the version,
regenerates the hosted app, catalog preview, and statistics, commits the prepared
sources locally, packages and audits all artifacts once from that exact commit,
then pushes and creates/resumes the matching release. `release:full` publishes the
audited GitHub draft as Latest; neither public command deploys hosted infrastructure.
Prerelease versions remain drafts. Set `NO_COLOR` to disable colored output.

Run the full build and aggregate test gate when product changes are made, before
pushing `main`. Release commands package and publish that already validated code;
they do not rerun the development test suite or recapture screenshots.

The catalog preview, hosted PWA, and statistics page are generated before the release
commit so private production operations can upload that exact reviewed snapshot.
A successful public release leaves the Git working tree clean.

Fast tool, publish-lock, GitHub/push, and NSIS
preflights run before source generation. If a later step
fails, the command restores tracked release changes and records an ignored local
checkpoint tied to the exact release commit. Rerun the same command: it reuses
only artifact results whose commit, filenames, sizes, SHA-256 hashes, release title,
and release-body hash still match, or resumes an audited GitHub draft, instead of repeating completed
long-running work. A standalone successful publication removes the checkpoint so
the next parameterless release advances normally. The private production workflow
uses `npm run release:publish-audited` after its production gates. That strict path
re-audits and publishes only the existing exact draft; it cannot create or repair a
release or upload assets. The private workflow then removes the checkpoint only
after final published-release verification succeeds.

## Prerequisites

- Windows, Node.js 24.18.1 LTS, npm 11.18.0, .NET SDK 10.0.400,
  PowerShell 7.6 LTS, Git, and NSIS 3.12 or newer.
- Visual Studio 2026 18.9 or newer with the Desktop development with C++ workload.
- PHP 8.5 for the public-site validation gate.
- Authenticated GitHub CLI with write access to `voltura/voltura-air`.
- Clean `main`, no merge/rebase, and no divergence from `origin/main`.
- No workflow YAML under `.github/workflows`.
- One committed non-empty target section in `docs/release-notes.md`.

Outputs are under `artifacts/publish`; the command prints SHA-256 hashes for the
ZIP and both installers.
The release also generates a deterministic `VolturaAir-Update-<version>.json` and raw RSA-PSS signature after both installers are final. The authorized release PC supplies the encrypted private key path through `VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH` and enters its passphrase without echo; the public key is the only key material in this repository. All five assets are checkpointed and uploaded as one exact set.

## Version preparation

Prepare an explicit semantic version:

```powershell
npm run release -- 0.8.0
```

Advance the stable one-digit minor/patch odometer:

```powershell
npm run release:bump
```

For example, `0.8.9` advances to `0.9.0`. Use an explicit version for other
semver forms, including prereleases. Numeric components must fit Windows version
resources.

Preparation synchronizes:

- root/mobile `package.json` and `package-lock.json`;
- host `Version`, `AssemblyVersion`, `FileVersion`, and
  `InformationalVersion`.

Vite, host assemblies, packaging, NSIS, filenames, and displayed versions read
those values. Standalone preparation and the complete release share one release
lock. Preparation calculates all target text before writing, stages files beside
their targets, and journals target/original/staged hashes under Git metadata.
Retry completes only recognized transaction-owned states; unexpected content is
left untouched for manual inspection. Review the diff; preparation does not
commit or publish.

## Standalone package checks

The complete release already runs these gates. For independent verification:

```powershell
npm run build
npm test
npm run package:win
```

Installer iteration:

```powershell
npm run package:win:small
npm run package:win:test
npm run package:win:test -- -SkipBuild
```

`package:win:test` writes uncompressed test installers under `artifacts/test`;
never publish them. Releasable names are:

```text
artifacts/publish/VolturaAir-<version>-win-x64.zip
artifacts/publish/VolturaAir-Setup-<version>-win-x64.exe
artifacts/publish/VolturaAir-Setup-<version>-win-x64-full.exe
```

Every Windows artifact includes `datachannel.dll` beside the host executable,
the PWA's `third-party-notices.txt`, and the complete native and managed runtime
notices under `ThirdPartyNotices`. The self-contained ZIP/full installer also
includes the .NET redistribution license and third-party notices copied from
the exact SDK used to build it. Packaging validation must reject an artifact
that omits any required notice. The component inventory, source links, and
native build provenance are owned by `THIRD-PARTY-NOTICES.md` and
`ThirdPartyNotices/libdatachannel/SOURCE.txt`; update both whenever the native
binary, its source, or a production dependency changes. Rebuild the native DLL
only with `scripts/build-libdatachannel.ps1`, then review its ABI/dependencies,
run Screen-view tests, and update the recorded hash before packaging.

For explicit script options:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version <version> -Runtime win-x64
powershell -ExecutionPolicy Bypass -File scripts/package-win.ps1 -Version <version> -Runtime win-x64 -SkipBuild
```

## Publication boundary

Releases run locally. `.github/workflows` must be empty; the release command
refuses competing workflow files. `npm run actions:restore` copies the
checked-in workflow definitions for deliberate review and refuses overwrite;
review triggers and GitHub state before committing them.

Never delete, replace, or overwrite an existing release/tag asset. Prepare a new
version. Manual publication, when required:

```powershell
$version = "<version>"
gh release create "v$version" `
  --repo voltura/voltura-air `
  --target main `
  --title "Voltura Air v$version" `
  --notes-file "<prepared-notes-file>" `
  "artifacts/publish/VolturaAir-$version-win-x64.zip" `
  "artifacts/publish/VolturaAir-Setup-$version-win-x64.exe" `
  "artifacts/publish/VolturaAir-Setup-$version-win-x64-full.exe"
```

Add `--prerelease` for a prerelease version.

## Release notes

Maintain `docs/release-notes.md` newest first with one `## v<version>` section
of concise user-visible capabilities, fixes, setup/compatibility notes, or known
limitations. Omit refactors, tests, tooling, dependency maintenance, and other
internal work. Keep its General notices unchanged.

The release command validates the section and builds the GitHub body. After a
manual GitHub edit/publish, import the marked editorial block with:

```powershell
npm run release:sync-release-notes
npm run release:sync-release-notes -- 0.8.0
```

The sync requires a clean worktree and updates only the matching local notes
section; review and commit its diff. Security-sensitive notes state practical
impact without implying encrypted internet transport.

## Release-specific verification

The full release gate is mandatory. Add focused manual checks when the release
touches pairing, WebSockets, protocol, input, power/session actions, installer
runtime acquisition, or recovery. Validate actual affected production paths,
including failure and reconnect/cleanup.

Before announcement, confirm:

- expected version diff and all automated gates;
- ZIP plus both installers and their SHA-256 hashes;
- clean-profile install of the runtime-downloading and full installers;
- Windows file/product and host/mobile displayed versions;
- fresh QR pairing and reconnect;
- when Screen viewing changes: short-QR scan at normal camera distance,
  authenticated first frame, display switch, relative input responsiveness,
  tray indicator/Stop, revocation/disconnect/lock cleanup, slow-client behavior,
  and no command-channel degradation over a real phone and Wi-Fi;
- public copy, links, package labels, and screenshots.

Installer choices and requirements are owned by the
[README](../README.md#download-and-install).

Both installers verify a generated SHA-256 payload manifest in a unique sibling
staging directory before stopping the path-verified installed host. Promotion,
isolated health check, registration, rollback, and cleanup use one journal outside
the install directory. Uninstall removes Phone Webcam first, then journals and
renames the verified installation; a recovery uninstaller remains registered until
the exact owned removal completes. The per-user uninstaller starts without elevation
and requests UAC only when the protected Phone Webcam component must be removed. Its
windowless helper stops the Windows camera services before removing the in-use media
source, without opening a terminal window.
Installer maintenance uses the newly packaged helper for this cleanup, so an
upgrade does not depend on the behavior of the previously installed helper. The
uninstaller likewise runs a temporary packaged copy, allowing the protected helper
and its directory to be removed immediately.
