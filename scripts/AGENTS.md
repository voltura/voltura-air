# Automation instructions

These instructions apply to development, validation, capture, cleanup, packaging,
and release automation under `scripts`.

## Process and host safety

- Never run multiple Voltura Air host instances. A launcher stops the existing
  host before starting a temporary one and leaves no competing process behind.
- Stop the host without recursively terminating
  `VolturaAir.CursorWatchdog.exe`, then wait for an existing watchdog to restore
  the Windows cursor scheme and exit. The watchdog is user-disableable, so its
  absence is normal and is not proof that restoration ran.
- A temporary host with temporary or empty pairing data or settings must pass
  `--isolated-test-mode`. It uses the normal single-instance scope, binds and
  advertises only loopback, isolates settings, and does not persist automatic
  network choices. Never expose a temporary pairing store on the LAN.
- Automated protocol tests use in-memory `TestServer` and do not inspect,
  reserve, or open the configured port or create firewall permissions.
- Reuse the required port or stop the relevant process instead of silently
  selecting another port. The ordinary VS Code development path is
  `npm run dev`.

## Destructive and maintenance commands

- Keep destructive targets explicit and validated. Provide a preview for broad
  ignored-file cleanup and preserve the local `.vs` directory and
  `.vscode/settings.json`.
- `npm run clean:temp:preview` previews ignored build/cache removal;
  `npm run clean:temp` performs it.
- `npm run cache:purge` stops the host, purges the current user's Windows icon
  cache, and restarts Explorer. Use it only for stale Windows application or
  notification icons.
- `npm run clean:git` compacts the local Git object database and prunes
  unreachable objects. Do not run it during another Git operation.
- `npm run deps:update` intentionally updates dependencies within declared
  ranges. It is dependency maintenance, not build-output cleanup.
- `npm run maintenance:full` deliberately performs the cache purge, ignored-file
  cleanup, Git maintenance, and dependency update together. Run it only when all
  four operations are intended.

## Tooling and packaging

- Install required SDKs, browsers, compilers, CLIs, and validation tools when
  missing instead of silently substituting weaker validation. Report material
  installs in the handoff.
- Maintained browser automation and icon generation use `@playwright/test` from
  the mobile workspace. Install its missing browser engines with
  `npx playwright install <engine>` from `apps/mobile-web`. The `dev:ui`,
  `test:ui`, and `screenshots:site` launchers intentionally install their
  capture-only `playwright` package in a temporary workspace.
- The native cursor watchdog uses the Visual Studio x64 C++ toolset discovered
  through `vswhere`; install the Desktop development with C++ workload if needed.
- Test-only uncompressed installers under `artifacts/test` are never release
  inputs. Follow `../docs/release.md` for packaging and publication.

## Script validation

- Keep launch, cleanup, release preparation, and package-script behavior covered
  by focused tests under `tests/scripts`.
- `scripts/check-documentation-map.mjs` enforces the canonical catalog, required
  public documentation surfaces, and local document-link integrity. Update its
  focused tests when the coverage contract changes.
- Run `npm run test:scripts` after changing JavaScript automation or root package
  script composition. Run packaging only when the changed behavior reaches that
  boundary.
