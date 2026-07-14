# AGENTS.md

## Project

Voltura Air is a new repository for a mobile trackpad project. Keep this file
updated as the app structure, tooling, and release process become concrete.

## Working Agreements

- Read this file before making changes in the repository.
- Keep changes small and focused on the requested task.
- Prefer the project's established patterns once source files and tooling exist.
- The Windows host targets .NET 10 and stable C# 14. Prefer current C# syntax such as collection expressions, records, pattern matching, target-typed construction, and file-scoped namespaces when they improve clarity. Do not introduce legacy syntax for compatibility with older language versions.
- The mobile client targets React 19 and TypeScript 6. Prefer function components, typed hooks, discriminated unions, `satisfies`, optional chaining, and modern platform APIs that work on the app's HTTP LAN origin. Avoid legacy React class components and compatibility patterns for older React or TypeScript releases.
- Do not add production dependencies without a clear reason in the change summary.
- Preserve user work in the tree; do not revert unrelated changes.
- Design settings and persisted data from a clean state. Do not add migrations,
  preserve earlier setting shapes, or retain backward compatibility unless the
  user explicitly requests it.
- Codex may stop local development or app processes when needed for build, test,
  debugging, or file-lock cleanup without asking first.
- Codex may start local debug, development, preview, host, or app processes when
  needed to build, test, inspect, capture, or otherwise validate requested work.
  Stop processes started by Codex when validation is complete unless the user
  asks to keep them running. If a required port is already in use, reuse the
  relevant running process when practical or stop it instead of taking the next
  port; the user normally starts debugging from VS Code with `npm run dev`.
- Never run multiple Voltura Air host instances in parallel, including normal,
  debug, development, screenshot, UI-validation, and isolated-test hosts. The
  single-instance behavior is a product invariant and scripts must not bypass
  it by using another instance scope or by allowing a second host to select the
  next available port. If a test or validation needs to launch its own host,
  stop the running Voltura Air host first, run the temporary host in the required
  test mode, stop it when validation finishes, and leave no competing host
  process running. Automated in-memory `TestServer` protocol tests are not host
  application instances and do not require stopping the running app.
- Any temporary host that uses a temporary or empty pairing store for tests,
  screenshots, or UI validation must pass `--isolated-test-mode`. This mode uses
  the same single-instance scope as the normal host, binds only to `127.0.0.1`,
  advertises loopback, and does not persist automatic network or port choices.
  The launching script must stop any running host before starting it. Never
  expose a temporary pairing store on the LAN because a real phone can interpret
  its rejection as revoked pairing and delete its saved reconnect secret.
- Automated host protocol tests must use ASP.NET Core's in-memory `TestServer`;
  they must not inspect, reserve, or open a configured TCP port or create
  Windows Firewall permissions.
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
- The Windows host references both WPF and WinForms/System.Drawing. In C# files,
  proactively qualify or alias collision-prone framework types such as `Brushes`,
  `Color`, `Image`, `Button`, `CheckBox`, `Application`, `DataFormats`, and
  `HorizontalAlignment`. Do not rely on an unqualified type name when imports or
  implicit/global usings can make the reference ambiguous.
- Pairing failures must not leave the mobile app stuck without explanation.
  Map raw protocol/network errors to friendly feedback, recovery actions, and
  copyable diagnostics. Follow `docs/pairing-feedback.md`.
- Keep-awake execution state is thread-scoped. All native set, reapply, and clear
  calls must remain owned by the dedicated Awake service thread; tray, WPF, and
  protocol surfaces must use `IAwakeService` and must not edit Windows power
  plans or require elevation.

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
- Product, setup, protocol, architecture, and troubleshooting docs describe only current behavior. Keep unimplemented or proposed work in `docs/todo.md` or an explicitly named roadmap, and remove completed implementation plans instead of preserving change history in product docs.

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

- Prepare a release version from the repository root with
  `npm run release -- <version>` (for example, `npm run release -- 0.3.0`).
- The release command validates semantic version syntax and synchronizes root
  `package.json`, `apps/mobile-web/package.json`, the root and workspace entries
  in `package-lock.json`, the host package, assembly, file, and informational
  versions in `apps/windows-host/VolturaAir.Host.csproj`, and the workflow
  dispatch defaults in `.github/workflows/release-zip.yml`.
- Do not treat `.vscode/launch.json` or `.vscode/tasks.json` schema versions as
  application versions. The NSIS installer, packaging filenames, and Vite client
  version consume the synchronized sources at build time. The host project keeps
  explicit `AssemblyVersion`, `FileVersion`, and `InformationalVersion` values so
  Windows File Explorer shows the release version on built EXE and DLL files.
- Before publishing a release, run `npm run build`, `npm test`, and
  `npm run package:win` sequentially. Packaging validates File Explorer metadata
  for the host EXE, host DLL, and NSIS installer with
  `scripts/verify-windows-version.ps1`.
- `npm run package:win` reads the default release version from the root
  `package.json`. Pass `-Version <version> -Runtime win-x64` only when overriding
  the defaults.
- The GitHub Actions workflow `.github/workflows/release-zip.yml` validates that
  its version and tag inputs match the committed root package version, then can
  create or update a release and upload both the portable zip and installer.
- See `docs/release.md` for the full preparation, verification, packaging,
  GitHub asset replacement, unsigned-installer, and sanity-check guide.

## Git

- Use descriptive commit messages.
- Keep generated files, build artifacts, local secrets, and machine-specific
  configuration out of version control.
