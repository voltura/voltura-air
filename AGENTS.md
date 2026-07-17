# AGENTS.md

## Project

Voltura Air is a new repository for a mobile trackpad project. Keep this file
updated as the app structure, tooling, and release process become concrete.

## Working Agreements

- Read this file before making changes in the repository.
- Keep changes small and focused on the requested task.
- Prefer the project's established patterns once source files and tooling exist.
- The Windows host targets .NET 10 and stable C# 14. Use current language and runtime features when they simplify ownership, improve correctness, or reduce meaningful allocation or CPU cost: collection expressions, records, primary constructors, pattern matching, target-typed construction, file-scoped namespaces, `Lock`, spans, cached serializer options, and async disposal. Do not introduce legacy syntax for compatibility with older language versions, and do not rewrite clear code solely to showcase newer syntax.
- For new or modified Windows native interop, use source-generated `LibraryImport` declarations when supported. Use exact Unicode entry points for text-sensitive APIs, explicit native boolean and string marshalling, the host's System32 DLL search policy, and `SafeHandle` types when they make ownership clear. Keep `DllImport` or classic COM interop only where source generation cannot faithfully represent callbacks, activation, or lifetime; place a narrow suppression and rationale beside that declaration.
- Keep the Windows host lightweight as a continuously running tray application. Prefer event-driven work over frequent polling, avoid unnecessary background activity and repeated allocations, release timers, subscriptions, sockets, and other resources correctly, and keep idle CPU, memory, disk, network, thread, and handle usage proportionate. Do not trade maintainability for insignificant micro-optimizations; measure when the benefit is uncertain.
- Follow `docs/host-quality.md` for the Windows host analyzer policy, runtime ownership expectations, modern C# standard, and required quality gate. Do not enable all optional .NET analyzers globally; promote reviewed host-relevant rules and document narrow exceptions instead.
- Keep host ownership boundaries explicit: `WpfHostRuntime` owns startup rollback and process-resource shutdown; `WpfTrayApplicationContext` and WPF windows render state and request commands or shutdown; services own their timers, subscriptions, native resources, protocol workers, and persistence. UI or tray code must not dispose or directly operate service internals.
- Keep WebSocket sends serialized per registered socket and bounded by cancellation and operation timeouts. Status changes use the host-owned coalescing broadcaster; do not add fire-and-forget broadcast tasks, parallel sends on one socket, or a second background worker for the same state.
- **Enable alpha features** is the reusable, default-off host umbrella gate for incomplete experimental work. Every alpha feature must advertise its own capability only while the gate is enabled and enforce the gate again at its production command boundary; hiding UI is not enforcement. Keep the gate cached and event-driven: never poll it, read the registry on an input/render hot path, add a timer or worker for it, or check it per pointer movement or animation frame. A disabled alpha feature must start no feature-specific timers, subscriptions, native resources, background work, or network activity. Reuse the existing coalescing status broadcaster when alpha availability changes.
- Treat pairing data as untrusted persistence. Keep reads size/record bounded and validated, write replacement data atomically in the same directory, and never persist or log plaintext reconnect secrets.
- The mobile client targets React 19 and TypeScript 6. Prefer function components, typed hooks, discriminated unions, `satisfies`, optional chaining, and modern platform APIs that work on the app's HTTP LAN origin. Avoid legacy React class components and compatibility patterns for older React or TypeScript releases.
- For web UI interaction, layout, scrolling, touch, pointer, focus, viewport, and browser-compatibility behavior, research the relevant web standard and official browser documentation before implementing unfamiliar behavior or repeatedly patching a browser-specific symptom. Prefer W3C/WHATWG specifications, MDN, and official Chromium/WebKit documentation; record the standards constraint that drives any non-obvious design.
- Design mobile-web interactions for the broadest practical set of modern browsers and touch platforms, including Android, iOS/iPadOS, Windows touch devices, and desktop browsers. Use standards-based APIs, feature detection, and progressive enhancement instead of user-agent checks or assumptions about one device or browser. Platform-only feedback such as vibration must never be required for discovering or completing an interaction.
- Declare browser gesture ownership before contact begins. In particular, `touch-action` is evaluated at gesture start and changing it after a long press or drag begins cannot reliably revoke native panning. Choose the correct initial `touch-action`, preserve ordinary scrolling through an explicit compatible interaction design, and do not rely on late `preventDefault()`, passive-listener workarounds, overflow toggles, or scroll-position correction as the primary way to stop an active browser gesture.
- Keep gesture state machines and scroll ownership small and explicit. Distinguish taps, pre-activation scrolling, long presses, active drags, cancellation, and release; clean up timers/listeners/capture on every exit path; suppress accidental follow-up clicks; preserve accessibility and keyboard behavior; and remove superseded workaround layers after the standards-based fix is established.
- Validate non-trivial mobile interaction and responsive-layout changes at representative portrait and landscape sizes. Cover both gesture directions, already-scrolled containers, boundary positions, cancellation, and persistence in focused tests, then run the mobile production build. When browser behavior cannot be represented faithfully in DOM tests, use Playwright on the relevant browser engines or real-device verification and state the remaining validation boundary.
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
- When the user is present and can validate a transient, subjective, or visual UI
  result in seconds, hand that check to the user instead of spending substantial
  time or tokens building temporary automation, preview harnesses, preview-only
  production switches, or capture infrastructure. Run the normal automated quality gate,
  but do not automate a quick human check unless the user explicitly asks Codex to
  work autonomously or requests automated coverage for that behavior.
- Never run multiple Voltura Air host instances in parallel, including normal,
  debug, development, screenshot, UI-validation, and isolated-test hosts. The
  single-instance behavior is a product invariant and scripts must not bypass
  it by using another instance scope or by allowing a second host to select the
  next available port. If a test or validation needs to launch its own host,
  stop the running Voltura Air host first, run the temporary host in the required
  test mode, stop it when validation finishes, and leave no competing host
  process running. Automated in-memory `TestServer` protocol tests are not host
  application instances and do not require stopping the running app.
- Development, UI-validation, and screenshot launchers must stop the host
  process without recursively terminating `VolturaAir.CursorWatchdog.exe`, then
  wait for the watchdog to restore the Windows cursor scheme and exit before
  launching another host. Do not use host-tree termination that defeats cursor
  recovery. The watchdog is a user-disableable runtime option, so generic
  launchers, cleanup code, and unrelated tests must treat an absent watchdog
  process as normal and already stopped; they may wait for an existing process
  but must not require one or use its absence as proof that cursor recovery ran.
  Packaging checks and dedicated watchdog tests may still require the binary or
  a watchdog process that they explicitly start.
- Any temporary host that uses a temporary or empty pairing store for tests,
  screenshots, or UI validation must pass `--isolated-test-mode`. This mode uses
  the same single-instance scope as the normal host, binds only to `127.0.0.1`,
  advertises loopback, isolates host settings from the normal Voltura Air
  registry key, and does not persist automatic network or port choices.
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

- `assets/branding` contains the authoritative product artwork master and
  Apple launch-image device matrix.
- `apps/mobile-web` contains the React/TypeScript PWA.
- `apps/windows-host` contains the .NET 10 Windows tray host.
- `apps/cursor-watchdog` contains the native Windows cursor-recovery watchdog source.
- `tests` contains automated test projects.
- `installer` contains the NSIS installer script for Windows release builds.
- `scripts` contains local development and packaging automation.
- `docs` contains setup, protocol, and troubleshooting notes.
- `docs/architecture.md` documents subsystem boundaries, dependency direction,
  compatibility invariants, and the source-file size policy.
- Add narrower `AGENTS.md` or `AGENTS.override.md` files in subdirectories when a
  specific area needs different commands or conventions.

## Verification

### Feature-complete test gate

- Treat a feature's user-visible primary action as an acceptance criterion, not an implication of helper/unit tests. Before reporting a feature as implemented, add and run at least one focused test that exercises the complete primary path from its real input or packaged asset through its production boundary and cleanup/restore path.
- Test the exact production representation when one exists. For example, cursor features must load the packaged `.cur` templates through the same native Windows API used by the host; parsing metadata, recolouring an in-memory bitmap, or testing a size formula alone does not validate that the feature can be enabled.
- Cover both success and failure behavior for new native, filesystem, network, registry, or process boundaries. A user action that reaches such a boundary must catch expected failures, restore any partially changed system state, preserve the running host, and give the user clear feedback. An unhandled event-handler exception that terminates the application is a release-blocking defect.
- Define the feature's externally observable outcomes before implementation: enabled/applied, changed/live-previewed where applicable, disabled/restored, normal shutdown, unexpected host termination, missing or invalid production asset, and native API failure. Add automated coverage for every outcome that can be exercised safely; state any remaining manual validation boundary explicitly.
- Run the focused acceptance test immediately after implementing the boundary, before broad builds/tests. If it fails, fix the production path first; do not substitute reflection, ad-hoc shell loading, UI scripting, or another execution technique for the normal project test runner.
- Do not report a change as complete because the pre-existing suite passes, because helper tests pass, or because a build succeeds. Report completion only after the new acceptance-path test and the proportionate full suite have passed. Include the exact validation boundary in the handoff.
- Keep test techniques transparent and conventional. Do not dynamically load product assemblies or invoke application methods through PowerShell reflection for validation. Use the repository's normal `dotnet`/`npm` build and test commands, or add a focused test to the appropriate test project.
- When a new test exposes a defect after a prior handoff, acknowledge that the original validation was insufficient, add the missing regression test before or alongside the fix, and rerun the complete relevant suite before a corrected handoff.

- Run `npm install` before the first build.
- Choose validation proportionate to the actual change and its risk. Start with the smallest focused build or tests that exercise the changed path, and do not repeatedly rerun an unchanged full suite or packaging step after edits that cannot affect it. Run broader validation once at the appropriate integration or release boundary, or when a cross-cutting, build, packaging, or configuration change warrants it.
- For changed C# code, review applicable compiler and .NET SDK analyzer diagnostics, including suggestion-level diagnostics that may appear only in the IDE. Use focused analyzer checks proportionate to the change, and evaluate recommendations for correctness, safety, maintainability, and current .NET conventions instead of applying them mechanically. Put analyzer rules the project consistently requires in repository configuration so the IDE, command line, and CI share the same policy.
- The host and host-test builds enforce the checked-in quality policy and must complete without compiler, formatting, or reviewed analyzer warnings. Do not lower a rule or add a suppression merely to make the build pass.
- Run `npm run build` for the mobile PWA and Windows host.
- Run `npm test` for the mobile and host test suites.
- Run `npm run package:win` when verifying Windows release packaging.
- The uncompressed installer fast path is optional, not part of the default build or
  test gate. Use `npm run package:win:test` only when changes to NSIS, packaging,
  installer contents, or Windows version metadata warrant validating both installer
  definitions without paying the compression cost. If the required publish outputs
  are already current, use `npm run package:win:test -- -SkipBuild`. Do not run either
  command routinely for unrelated host, mobile, UI, or documentation changes. The
  unmistakably named installers under `artifacts/test` are test-only and must never
  be published; final release verification still requires `npm run package:win`.
- Run `npm run size:report` to review actively maintained source files above 20 KB.
- Run build and test commands sequentially. Do not run `npm run build`,
  `npm test`, `dotnet build`, or `dotnet test` in parallel with each other,
  because the .NET host project writes to shared output files and parallel runs
  can cause transient file-lock failures.

## Tooling

- `assets/branding/voltura-air-master.png` is the single product artwork source.
  Connected and disconnected tray states are generated as green-check and
  muted-red-cross status badges over that master. Ordinary app and task-area
  icons use a tight fit while maskable variants retain their safe zone. Run
  `npm run icons:generate` to regenerate and
  validate mobile, iOS, Android, Windows host, NSIS, marketing-site, and
  README-referenced branding assets. Do not edit generated icon copies directly.
- On Windows, run `npm run branding:generate` to regenerate static branding and
  then refresh all `docs/site/assets` product screenshots through the isolated
  screenshot workflow.
- Run `npm run cache:purge` when Windows keeps showing an earlier Voltura Air
  notification or application icon after branding changes. The command stops
  the host, purges the current user's Windows icon cache, and restarts Explorer.
- Run `npm run clean:temp:preview` before `npm run clean:temp` to remove ignored
  build and cache files while retaining the local `.vs` folder and
  `.vscode/settings.json`.
- Run `npm run clean:git` to compact the local Git object database and prune
  unreachable objects; do not run it while another Git operation is active.
- Run `npm run clean` to stop the host and purge the Windows icon cache before
  removing workspace and Git cleanup targets, then update dependencies within
  their declared version ranges. It restarts Explorer during the cache purge.
- GitHub CLI (`gh`) is available in the Codex shell. It is installed through
  WinGet and exposed to the current Codex environment through
  `%APPDATA%\npm\gh.cmd`.
- Run `npm run branch:sync` from a clean feature branch to fetch the latest
  `origin/main` and merge it into the current branch. It refuses `main` and a
  dirty worktree; resolve merge conflicts through the normal Git workflow.
- Run `npm run code:statistics` to report the maintained mobile-client,
  Windows-host, and NSIS installer source file and line counts, grouped by file
  type, plus repository document, image, cursor asset, script, npm-command,
  file-date, largest-file, and declared test counts. Pass `--report` to write
  the same report as HTML in a temporary directory and open it in the default
  browser.
- `npm run dev:ui` and `npm run test:ui` explicitly enable alpha features only
  inside their isolated test settings so development and smoke coverage can
  exercise gated surfaces without changing the normal default-off host setting.
- The native cursor watchdog is compiled with the Visual Studio x64 C++ toolset.
  `scripts/build-cursor-watchdog.ps1` discovers it through `vswhere`; install the
  Desktop development with C++ workload when the toolset is absent.
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
  dispatch defaults in `.github/workflows/release.yml`.
- Do not treat `.vscode/launch.json` or `.vscode/tasks.json` schema versions as
  application versions. The NSIS installer, packaging filenames, and Vite client
  version consume the synchronized sources at build time. The host project keeps
  explicit `AssemblyVersion`, `FileVersion`, and `InformationalVersion` values so
  Windows File Explorer shows the release version on built EXE and DLL files.
- Before publishing a release, run `npm run build`, `npm test`, and
  `npm run package:win` sequentially. Packaging validates File Explorer metadata
  for the host EXE, native cursor watchdog, host DLL, and both NSIS installers with
  `scripts/verify-windows-version.ps1`.
- `npm run package:win` reads the default release version from the root
  `package.json`. Pass `-Version <version> -Runtime win-x64` only when overriding
  the defaults.
- The GitHub Actions workflow `.github/workflows/release.yml` validates that
  its version and tag inputs match the committed root package version, runs the
  mobile and host tests, then builds and packages the release outputs before creating
  or updating a release. The default installer downloads the required .NET 10
  runtimes during setup when they are missing and may require administrator approval.
- During the current limited beta, rerunning the workflow may replace assets and
  move the same version tag to a newer tested commit. Tag updates must remain
  serialized and use a force-with-lease check, never an unconditional force push.
  The uploaded GitHub release assets are authoritative; uncompressed test
  installers and transient workflow outputs are never release inputs.
- See `docs/release.md` for the full preparation, verification, packaging,
  GitHub asset replacement, unsigned-installer, and sanity-check guide.

## Git

- Use descriptive commit messages.
- Keep generated files, build artifacts, local secrets, and machine-specific
  configuration out of version control.
