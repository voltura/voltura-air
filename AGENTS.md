# Voltura Air

React PWA plus a .NET Windows tray host. `docs/README.md` is the documentation
map; `docs/ui-system.md` and `docs/architecture.md` define the target design.

## Decide

Use this order:

1. Safety, data integrity, and explicit product invariants.
2. Current user request, including explicitly requested behavior changes.
3. Closest `AGENTS.md`.
4. Applicable authority documents.
5. Intentional behavior evidenced by existing code and tests.

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
- Use the current design; remove replaced internal formats and structures only
  after affected persistence and wire compatibility is handled explicitly.
  Before changing or removing a persisted or wire format, define its support
  window, migration or rejection behavior, and current authority and test
  updates. Stored data remains untrusted and bounded.
- Protocol and security tests state the current contract directly; helpers must
  not repair or enrich tested messages.

## UI work

- Material UI work changes layout, visual styling, content hierarchy,
  interaction, navigation, or visible state behavior beyond an isolated copy or
  generated-token correction.
- Use a visual checkpoint by default. Use autonomous completion only when the
  user explicitly requests it; if the requested mode is unclear, keep the
  visual-checkpoint default.
- Autonomous: implement and validate normally.
- Visual checkpoint: make a runnable representative result, stop further coding
  and validation, launch it, and wait for feedback. For WPF run
  `./scripts/host-preflight.ps1`, then `npm run dev:quick`. After approval,
  finish the implementation and validation normally.
- Keep the chosen mode for the task.
- Install needed validation or inspection tooling. Do not report incomplete work
  as complete.

## Scope

- `apps/mobile-web`: read its `AGENTS.md` and `docs/ui-system.md`.
- `apps/windows-host`: read its `AGENTS.md`, `docs/host-quality.md`, and
  `docs/host-ui-guidelines.md`.
- `scripts`: read `scripts/AGENTS.md`.
- Generate icons with `npm run icons:generate`; do not edit generated copies.

## Authority routing

- User-visible capability or guarantee: `docs/features.md` and any feature
  authority named there.
- UI, layout, interaction, or accessibility: `docs/ui-system.md`; for WPF also
  `docs/host-ui-guidelines.md`; for pairing states also
  `docs/pairing-feedback.md`.
- Wire messages, authentication, capabilities, acknowledgements, or bounds:
  `docs/protocol.md` and `docs/architecture.md`.
- Network, adapter, port, host-hint, or saved-PC behavior:
  `docs/network-and-host-selection.md`.
- Persistence, diagnostics, logging, credentials, or trust boundaries:
  `docs/architecture.md`, `PRIVACY.md`, and the relevant protocol or feature
  authority.
- Installation, development commands, packaging, or publication:
  `docs/setup.md`, `docs/release.md`, and `scripts/AGENTS.md` as applicable.
- Documentation or public-copy changes: use `docs/README.md` to identify the
  owning authority and every derived surface that must stay synchronized.

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
