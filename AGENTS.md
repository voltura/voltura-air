# Voltura Air

React PWA + .NET Windows tray host. Authorities: `docs/README.md`.

## Decide

Apply root + nearest scope. Order: safety, data integrity, explicit invariants,
user request, relevant authority, intended code/test behavior.

Hard: safety/security, explicit invariants, wire/persisted compatibility.
Architecture/UI are rebuttable defaults; override with evidence only while
preserving hard contracts/requested behavior. Update authority.

Ask before product-behavior/hard-contract changes or material authority-conflict
resolution. Read only task-relevant sections unless changing broad contracts.

## Work

- Reuse owners before helpers/storage/fields/workers; prefer explicit ownership
  and event-driven work.
- Before wire/persisted changes, define compatibility/rejection for existing
  values/messages; update authority/tests.
- Protocol/security tests define contracts; helpers never repair tested messages.

## UI

Significant UI: new/substantially reworked surface, layout direction, navigation,
or multi-state interaction. Default: show a representative result; await
feedback.

WPF/device validation: `./scripts/host-preflight.ps1`, then
`npm run dev:quick`. Copy/token/contained fixes: focused visual verification
only. Skip preflight for inspection, static/ownership checks, or `TestServer`.

## Invariants

- One host only.
- Automation/capture/temporary: `BeginIsolatedScope` or `--isolated-test-mode`;
  human `dev`/`dev:quick`: production settings.
- Protocol tests: ASP.NET Core `TestServer`; never TCP ports/firewall rules.
- Never log typed text, pointer coordinates, pairing tokens, reconnect keys, or
  proofs.
- Pairing links stay short; exchange credentials after opening.
- Presentation defaults on behind **Enable alpha features**; explicit off removes
  capability and blocks production commands.

## Verify and release

Use the smallest relevant check:

- Docs/public copy: `npm run docs:check`.
- Code: scoped static/build gate + focused changed-behavior tests.
- Broad/shared: full scoped suites.
- Release/repository-wide shared contracts only: root build, then test.
- Structural: add `npm run size:check`.
- Changed external/resource boundaries: test success/failure/cleanup.

Assess docs every task. If durable truth changed, find its owner via
`docs/README.md`; edit in place; remove superseded/duplicate text; update derived
surfaces. Otherwise, do not edit docs. Never create a document with an existing
owner.

Except release notes, docs describe the present. Approved work:
`docs/todo.md`; possible directions: `docs/ideas.md`. Published builds require a
new semantic version per `docs/release.md`.
