# Release

Every published build uses a new semantic version. This procedure prepares,
verifies, packages, and publishes the Windows release and public site.

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
regenerates public assets/statistics, runs full build/tests, packages/audits all
artifacts, commits and pushes generated changes, rebuilds the final commit,
creates/resumes the matching release, and deploys `docs/site`. Prerelease
versions remain drafts. Set `NO_COLOR` to disable colored output.

## Prerequisites

- Windows, Node.js/npm, .NET 10 SDK, Git, and NSIS.
- Authenticated GitHub CLI with write access to `voltura/voltura-air`.
- Site SFTP password stored by `npm run publish:site:password`.
- Clean `main`, no merge/rebase, and no divergence from `origin/main`.
- No workflow YAML under `.github/workflows`.
- One committed non-empty target section in `docs/release-notes.md`.

Outputs are under `artifacts/publish`; the command prints SHA-256 hashes for the
ZIP and both installers.

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
those values. Review the diff; preparation does not commit or publish.

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
- public copy, links, package labels, and screenshots.

Installer choices and requirements are owned by the
[README](../README.md#download-and-install).
