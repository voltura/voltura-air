# AGENTS.md

## Project

Voltura Air is a new repository for a mobile trackpad project. Keep this file
updated as the app structure, tooling, and release process become concrete.

## Working Agreements

- Read this file before making changes in the repository.
- Keep changes small and focused on the requested task.
- Prefer the project's established patterns once source files and tooling exist.
- Do not add production dependencies without a clear reason in the change summary.
- Preserve user work in the tree; do not revert unrelated changes.
- Codex may stop local development or app processes when needed for build, test,
  debugging, or file-lock cleanup without asking first.
- If a task has a preferred validation, inspection, capture, automation tool,
  or supporting runtime/toolchain that is not installed locally, Codex may
  install and use it without asking first. Prefer temporary or user-scoped
  installs unless a project-local dependency is clearly the right fit.
- When editing Windows Forms UI, account for Windows DPI/scaling. Prefer
  autosizing layouts or scale fixed dimensions, margins, and padding with
  `LogicalToDeviceUnits`, and make sure text is not clipped at non-100%
  display scaling.

## Repository Structure

- `apps/mobile-web` contains the React/TypeScript PWA.
- `apps/windows-host` contains the .NET 10 Windows tray host.
- `tests` contains automated test projects.
- `installer` contains the NSIS installer script for Windows release builds.
- `scripts` contains local development and packaging automation.
- `docs` contains setup, protocol, and troubleshooting notes.
- Add narrower `AGENTS.md` or `AGENTS.override.md` files in subdirectories when a
  specific area needs different commands or conventions.

## Verification

- Run `npm install` before the first build.
- Run `npm run build` for the mobile PWA and Windows host.
- Run `npm test` for the mobile and host test suites.
- Run `npm run package:win` when verifying Windows release packaging.
- Run build and test commands sequentially. Do not run `npm run build`,
  `npm test`, `dotnet build`, or `dotnet test` in parallel with each other,
  because the .NET host project writes to shared output files and parallel runs
  can cause transient file-lock failures.

## Tooling

- GitHub CLI (`gh`) is available in the Codex shell. It is installed through
  WinGet and exposed to the current Codex environment through
  `%APPDATA%\npm\gh.cmd`.

## Release

- Use the root package/host version for GitHub release tags, formatted as
  `v<version>` (for example, `v0.1.0`).
- Before publishing a release, run a new release build of the .net application and run `npm run build` and `npm test`.
- Publish the Windows host with the mobile web assets already built:
  `dotnet publish apps/windows-host/VolturaAir.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/VolturaAir-win-x64`.
- Zip the publish directory as `artifacts/VolturaAir-<version>-win-x64.zip`.
- Use GitHub CLI to create the release and upload the binary asset:
  `gh release create v<version> artifacts/VolturaAir-<version>-win-x64.zip --repo voltura/voltura-air --title "Voltura Air v<version>" --notes "..."`
- See `docs/release.md` for a step-by-step release packaging and GitHub asset replacement guide.

## Git

- Use descriptive commit messages.
- Keep generated files, build artifacts, local secrets, and machine-specific
  configuration out of version control.
