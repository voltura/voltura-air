# AGENTS.md

## Project and development stage

Voltura Air is a mobile control surface with a React PWA and a lightweight .NET
Windows tray host. The product is in pre-release development.

`docs/README.md` is the canonical documentation map. `docs/ui-system.md` and
`docs/architecture.md` describe the target design. Existing code and tests are
evidence of current behavior; new and refactored code follows the target design.

## Decision authority

Use this order when deciding how to proceed:

1. The explicit user goal and decisions for the current task.
2. Security, data integrity, product invariants, and intentional user-visible
   behavior.
3. The closest applicable `AGENTS.md` instructions.
4. The authoritative target documents referenced by those instructions.
5. Current implementation, tests, and established patterns.

Preserve intentional behavior, safety, and ownership while implementing the
documented target.

When sources disagree in a way that materially changes behavior, data,
architecture, security, performance, or task scope, stop that part of the work,
show the exact evidence, and resolve it with the user. Correct an obvious stale
reference within the task without creating an unnecessary decision point.

Provide constructive technical pushback when repository evidence, platform
standards, safety, maintainability, performance, or a simpler design supports a
different approach. Distinguish evidence from judgment, explain the tradeoff,
and recommend an alternative. Find relevant rules and contradictions without
relying on the user to name every document.

## Working agreements

- Read this file and the closest scoped `AGENTS.md` before changing files.
- Keep each change coherent and focused on the requested outcome. Do not reduce
  necessary scope merely to avoid a justified rewrite.
- Preserve user work and unrelated changes in the tree.
- Prefer simple, explicit ownership over generic abstraction. Apply KISS without
  weakening correctness, accessibility, resource ownership, or the documented
  UI and architecture contracts.
- Do not add a production dependency without a clear task need and a review of
  its runtime, bundle, maintenance, and security cost. Tooling dependencies do
  not become production dependencies merely for convenience.
- Keep the continuously running host and mobile client proportionate in CPU,
  memory, allocation, disk, network, thread, handle, and battery use. Prefer
  event-driven work and measured optimization.

### Current design policy

Build the clearest current design from a clean state. Development-era source
layouts, settings shapes, persisted formats, internal APIs, and protocol shapes
have no compatibility guarantee. Remove replaced structures and formats. Keep
persisted-data readers bounded and defensive because stored data remains
untrusted. Define an external compatibility policy before promising upgrade
compatibility.

## AI execution and tools

- Do not lower the quality of an implementation because a preferred local tool
  is missing. Install and use required validation, inspection, capture,
  automation, runtime, or toolchain support without asking first.
- Tool installation may be temporary, user-scoped, project-local, or system-wide
  as appropriate. Prefer the least intrusive option when capabilities are equal,
  do not change declared product/runtime versions unless they are in scope, and
  report material installations in the handoff.
- When the user can verify a transient visual result in seconds, use that quick
  human check instead of building disposable capture infrastructure. Still run
  the normal automated gate appropriate to the changed behavior.
- If GitHub connector editing cannot safely apply a large or multi-file change,
  request the exact branch files as a zip, edit them locally, and return complete
  repo-relative replacements. Do not keep retrying fragile partial patches.
- Do not mark incomplete connector or PR work as complete. Code, documentation,
  and validation instructions must match the actual branch state.

## Repository map and scoped instructions

- `apps/mobile-web` contains the React/TypeScript PWA. Follow
  `apps/mobile-web/AGENTS.md` and `docs/ui-system.md`.
- `apps/windows-host` contains the .NET 10 WPF tray host. Follow
  `apps/windows-host/AGENTS.md`, `docs/host-quality.md`, and
  `docs/host-ui-guidelines.md`.
- `apps/cursor-watchdog` contains the native Windows cursor-recovery watchdog.
- `tests` contains automated host and script tests.
- `scripts` contains development, validation, capture, cleanup, and packaging
  automation. Follow `scripts/AGENTS.md`.
- `assets/branding/voltura-air-master.png` is the authoritative product artwork.
  Run `npm run icons:generate`; do not edit generated icon copies directly.
- `docs/architecture.md` owns subsystem boundaries, dependency direction,
  migration state, compatibility invariants, and the source-size review policy.
- `docs/README.md` catalogs every maintained document and public descriptive
  surface with its authority, state, and update trigger.
- `docs/ui-system.md` is the product-wide UI authority.
  `docs/host-ui-guidelines.md` adds host-specific behavior, and
  `docs/pairing-feedback.md` owns pairing
  failure and recovery guidance.
- `docs/release.md` is the release and packaging runbook.

Add or refine a scoped `AGENTS.md` when a directory develops rules that do not
need to occupy the context of unrelated work.

## Cross-project invariants

- Never run multiple Voltura Air host instances. Temporary UI hosts use the same
  single-instance scope, must stop the normal host first, and must use
  `--isolated-test-mode` when settings or pairing data are temporary.
- Automated host protocol tests use ASP.NET Core `TestServer`; they do not open a
  configured TCP port or create Windows Firewall permissions.
- Never log typed text, pointer coordinates, pairing tokens, reconnect secrets,
  or other credential material.
- Incomplete experimental host features use the default-off **Enable alpha
  features** umbrella gate and enforce it at the production command boundary.
  Hidden UI is not enforcement.

## Documentation

- Before a project-wide documentation or policy review, read `docs/README.md`,
  inventory the maintained surfaces, and inspect every catalog entry.
- State current behavior, target design, procedure, approved work, or candidate
  direction directly. Each rule or fact has one owner; other documents link to
  it. Keep conversational history and revision narratives in Git history.
- Treat `README.md`, `docs/site/index.php`, and `docs/site/llms.txt` as derived
  communication. They select and simplify facts owned by current product,
  setup, release, security, and troubleshooting documents.
- `docs/todo.md` orders approved work: P0 architecture and quality, then P1
  Presentation. `docs/ideas.md` records candidate directions; explicit product
  approval moves a candidate into the TODO.
- When externally documented behavior changes, update the affected documentation,
  static site, and README. Update `AGENTS.md` only when working policy changes.
- Add, remove, or rename maintained documentation only together with the
  `docs/README.md` catalog entry, then run `npm run docs:check`.

## Verification

Choose validation by behavior and risk:

- New or changed native, filesystem, registry, network, process, persistence, or
  resource-owning boundaries require focused success, failure, and cleanup or
  recovery coverage through the production path.
- Stateful interactions and regressions require focused behavioral tests where
  the behavior can be represented reliably.
- Presentation-only changes require the relevant build or static gate plus a
  focused visual check at the affected layouts, themes, and interaction states.
- Documentation-only changes require reference, command, and factual checks.

Start with the smallest focused check, then run the broader relevant gate once
at the integration boundary. Run root build and test commands sequentially
because host projects share .NET outputs:

```powershell
npm run build
npm test
```

Run `npm run package:win` only for release verification or changes that affect
packaging. Run `npm run size:report` to review ownership and separation concerns;
size is a review signal, not a mechanical failure.

## Release and Git

- Use a new semantic version for every published build. Do not replace release
  assets or move an existing tag. Follow `docs/release.md`.
- Use descriptive commits and preserve a focused diff.
- Keep build artifacts, local secrets, and machine-specific configuration out of
  version control. Check in generated sources or assets only when the documented
  project workflow treats them as repository outputs, and never edit those
  generated copies directly.
