# Voltura Air

React PWA plus a .NET Windows tray host. `docs/README.md` is the documentation
map; `docs/ui-system.md` and `docs/architecture.md` define the target design.

## Decide

Use this order:

1. Current user request.
2. Safety, data integrity, product invariants, and intentional behavior.
3. Closest `AGENTS.md`.
4. Applicable authority documents.
5. Existing code and tests.

If sources materially conflict, show the evidence and ask. Correct obvious
stale references within the task.

## Work

- Read this file, the closest scoped `AGENTS.md`, and applicable authority docs
  before changing or reviewing code.
- Use repository evidence. Preserve unrelated user changes.
- Reuse an existing owner before adding helpers, storage, protocol fields, or
  test-harness behavior.
- Keep changes focused. Prefer explicit ownership and event-driven work.
- Review a production dependency before adding it.
- Use the current design; remove replaced internal formats and structures.
  Stored data remains untrusted and bounded.
- Protocol and security tests state the current contract directly; helpers must
  not repair or enrich tested messages.

## UI work

- Before material UI work, ask for autonomous completion or visual checkpoints.
- Autonomous: implement and validate normally.
- Visual checkpoint: make a runnable representative result, stop coding and
  tests, launch it, and wait for feedback. For WPF run
  `./scripts/host-preflight.ps1`, then `npm run dev:quick`.
- Keep the chosen mode for the task.
- Install needed validation or inspection tooling. Do not report incomplete work
  as complete.

## Scope

- `apps/mobile-web`: read its `AGENTS.md` and `docs/ui-system.md`.
- `apps/windows-host`: read its `AGENTS.md`, `docs/host-quality.md`, and
  `docs/host-ui-guidelines.md`.
- `scripts`: read `scripts/AGENTS.md`.
- Generate icons with `npm run icons:generate`; do not edit generated copies.

## Invariants

- Only one host runs. Temporary UI hosts use `--isolated-test-mode`.
- Protocol tests use ASP.NET Core `TestServer`, never a configured TCP port or
  firewall rule.
- Tests and development runs use `HostSettingsRegistry.BeginIsolatedScope()` or
  `--isolated-test-mode`; never the production settings registry key.
- Never log typed text, pointer coordinates, pairing tokens, reconnect keys, or
  reconnect proofs.
- Pairing links and QR codes remain short; exchange credentials after opening
  the normal pairing link.
- Presentation is behind the default-on **Enable alpha features** gate. Enforce
  it at production command boundaries; explicit off removes the capability.

## Documentation

- `docs/README.md` catalogs maintained docs. Read it before a project-wide doc
  review.
- Each rule has one owner. README and site copy are derived from authorities.
- Update affected docs, site copy, and README when documented behavior changes.
- Add, remove, or rename docs with a `docs/README.md` entry, then run
  `npm run docs:check`.

## Verify

- Run the smallest relevant check. Cover changed native, filesystem, registry,
  network, process, persistence, and resource boundaries through production
  paths, including failure and cleanup.
- UI-only work needs the relevant build/static check and a focused visual check.
- When root build and test are needed, run them sequentially:

```powershell
npm run build
npm test
```

- After a refactor or structural pass, run `npm run size:check` and resolve new
  strong warnings.

## Release and Git

- Published builds require a new semantic version; follow `docs/release.md`.
- Keep commits focused. Do not commit artifacts, secrets, or machine settings.
