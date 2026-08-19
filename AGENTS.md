# Voltura Air

React PWA + .NET Windows tray host. Authority: `docs/README.md`.

## Decide

Apply root + nearest scope. Priority: safety, data integrity, explicit invariants,
user request, relevant authority, intended code/test behavior.

Hard: safety/security, explicit invariants, wire/persisted compatibility.
Architecture/UI are defaults; override only with evidence while preserving hard
contracts/requested behavior. Update authority.

Ask before product-behavior/hard-contract changes or material authority conflicts.
Read only task-relevant sections unless changing broad contracts.

## Work

- Follow the documented workflow. Ask about missing required steps. Do not invent
  extra work; diagnose workflow failures.
- Prefer the simplest design that fits existing architecture. Reuse/extend existing
  owners, logic, protocols, and models before adding services/messages/state/storage/
  frameworks; new abstractions require a concrete gap.
- Before wire/persisted changes, define compatibility/rejection for existing
  values/messages; update authority/tests.
- Protocol/security tests are contracts; helpers never repair tested messages.
- Before destructive or restart-recoverable work, define state transitions and the
  durable owner of every temp/backup/partial artifact. Add failure injection at each
  external boundary, including commit + rollback failure, before visual acceptance.
- Destructive/data-integrity work needs adversarial review before visual acceptance.
  Any P1 security/data-integrity finding resets readiness: fix and re-verify cleanly.
  Tooling-only findings do not require another independent review unless shipped.

## UI

Significant UI = new/substantially reworked surface, layout direction, navigation,
or multi-state interaction. Default: show a representative result; await feedback.

WPF/device validation: `./scripts/host-preflight.ps1`, then `npm run dev:quick`.
Copy/token/contained fixes need focused visual verification only. Skip preflight
for inspection, static/ownership checks, or `TestServer`.

## Invariants

- One host only.
- Automation/capture/temp: `BeginIsolatedScope` or `--isolated-test-mode`;
  human `dev`/`dev:quick`: production settings.
- Protocol tests use ASP.NET Core `TestServer`; never TCP ports/firewall rules.
- Never log typed text, pointer coordinates, pairing tokens, reconnect keys, or proofs.
- Pairing links stay short; exchange credentials after opening.
- Interaction hot paths never wait on media, analytics, logging, UI, registry, disk,
  or background work. Use cached settings, bounded queues/backpressure, and separate
  transports so overload fails locally instead of building lag.
- Custom screens and Presentation are supported. Experimental features use explicit,
  feature-owned toggles under Developer tools; no global alpha gate.

## Verify and release

Use the smallest relevant check:

- Docs/public copy: `npm run docs:check`.
- Code: scoped static/build gate + focused changed-behavior tests.
- Broad/shared: full scoped suites.
- Release/repository-wide shared contracts only: root build, then test.
- Structural: add `npm run size:check`.
- Changed external/resource boundaries: test success/failure/cleanup.

Release readiness requires the full aggregate gate with zero failures. Fix valid
failures regardless of origin. A retry pass does not close a transient failure until
its cause is stabilized and the full gate passes again.

Assess docs every task. If durable truth changed, find its owner via `docs/README.md`;
edit in place, remove superseded/duplicate text, and update derived surfaces.
Otherwise do not edit docs. Never create a document with an existing owner.

Except release notes, docs describe the present. Approved work: `docs/todo.md`;
possible directions: `docs/ideas.md`. Published builds require a new semantic
version per `docs/release.md`.
