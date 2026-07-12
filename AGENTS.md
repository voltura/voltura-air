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
- Do not start debug/dev servers unless explicitly asked. If a required dev
  server port is already in use, stop the relevant running process instead of
  taking the next port; the user normally starts debugging from VS Code with
  `npm run dev`.
- If a task has a preferred validation, inspection, capture, automation tool,
  or supporting runtime/toolchain that is not installed locally, Codex may and
  should install and use it without asking first. Do not take shortcuts or
  assume results because a tool is missing; install the needed tool, use it, and
  report what was installed. Prefer temporary or user-scoped installs unless a
  project-local dependency is clearly the right fit.
- The Windows host UI is WPF-first. Prefer device-independent WPF layout
  primitives (`Grid`, `DockPanel`, `StackPanel`, `ScrollViewer`, `ListView`)
  over manual pixel positioning. Keep WinForms usage limited to tray interop or
  legacy code that has not yet been removed. Follow `docs/ui-guidelines.md`.
- Pairing failures must not leave the mobile app stuck without explanation.
  Map raw protocol/network errors to friendly feedback, recovery actions, and
  copyable diagnostics. Follow `docs/pairing-feedback.md`.

## ChatGPT.com / Connector Editing Workflow

- If GitHub connector editing blocks a large or multi-file replacement, do not
  keep retrying fragile partial patches. Ask for a zip of the exact branch files,
  edit them locally in the VM, and return a repo-relative replacement zip.
- When returning edited files from ChatGPT.com, preserve repo-relative paths such
  as `apps/mobile-web/src/...`, `apps/windows-host/...`, and `docs/...` so the
  user can expand the zip at the repository root.
- Prefer complete file replacement for complex TypeScript, C#, markdown, or site
  changes when that is safer than a sequence of brittle search/replace patches.
- Be explicit when a PR is incomplete. Do not mark work as done until code, docs,
  and validation instructions match the actual branch state.
- When connection, pairing, protocol, release, screenshot, setup, troubleshooting,
  or UI behavior changes, update the affected files under `docs/`, the static
  site under `docs/site`, `README.md`, and this file when workflow guidance
  changes. Keep docs current-state focused; avoid historical change notes.

## Repository Structure

- `apps/mobile-web` contains the React/TypeScript PWA.
- `apps/windows-host` contains the .NET 10 Windows tray host.
- `tests` contains automated test projects.
- `installer` contains the NSIS installer script for Windows release builds.
- `scripts` contains local development and packaging automation.
- `docs` contains setup, protocol, and troubleshooting notes.
- `docs/architecture.md` documents subsystem boundaries, dependency direction,
  compatibility invariants, and the source-file size policy.
- Add narrower `AGENTS.md` or `AGENTS.override.md` files in subdirectories when a
  specific area needs different commands or conventions.

## Verification

- Run `npm install` before the first build.
- Run `npm run build` for the mobile PWA and Windows host.
- Run `npm test` for the mobile and host test suites.
- Run `npm run package:win` when verifying Windows release packaging.
- Run `npm run size:report` to review actively maintained source files above 20 KB.
- Run build and test commands sequentially. Do not run `npm run build`,
  `npm test`, `dotnet build`, or `dotnet test` in parallel with each other,
  because the .NET host project writes to shared output files and parallel runs
  can cause transient file-lock failures.

## Tooling

- GitHub CLI (`gh`) is available in the Codex shell. It is installed through
  WinGet and exposed to the current Codex environment through
  `%APPDATA%\npm\gh.cmd`.
- Playwright is available for the mobile workspace through the
  `@playwright/test` dev dependency in `apps/mobile-web/package.json`. Run it
  from the repository root with
  `npm exec --workspace apps/mobile-web -- playwright ...`, or from
  `apps/mobile-web` with `npx playwright ...`.
- Playwright Chromium browser binaries are installed in the user Playwright
  cache (`%LOCALAPPDATA%\ms-playwright`). If a browser binary is missing on a
  new machine or cache, run `npx playwright install chromium` from
  `apps/mobile-web`.

## Release

- Use the root package/host version for GitHub release tags, formatted as
  `v<version>` (for example, `v0.2.0`).
- For a version bump, keep root `package.json`, `apps/mobile-web/package.json`,
  `package-lock.json`, `.github/workflows/release-zip.yml`,
  `apps/windows-host/VolturaAir.Host.csproj`, version-related tests, and docs in
  sync.
- After changing package versions, run `npm install` so `package-lock.json`
  records the same root and workspace versions.
- Before publishing a release, run `npm run build`, `npm test`, and
  `npm run package:win` sequentially.
- `npm run package:win` reads the default release version from the root
  `package.json`. Pass `-Version <version> -Runtime win-x64` only when overriding
  the defaults.
- The GitHub Actions workflow `.github/workflows/release-zip.yml` can create or
  update a release and upload both the portable zip and installer assets.
- See `docs/release.md` for the full version bump, packaging, GitHub asset
  replacement, release-note, unsigned-installer, and sanity-check guide.

## Git

- Use descriptive commit messages.
- Keep generated files, build artifacts, local secrets, and machine-specific
  configuration out of version control.
