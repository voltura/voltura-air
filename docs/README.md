# Documentation map

Canonical catalog of Voltura Air documentation. Each row identifies the
document's role, subject, and update trigger.

## Document roles

| Role | Purpose |
| --- | --- |
| Authority | Defines current rules, contracts, or implemented behavior. |
| Target authority | Defines the design the repository is implementing and labels any incomplete transition. |
| Operational | Gives commands and procedures derived from authorities. |
| Derived public | Selects current facts for users or prospective users. |
| Approved TODO | Orders approved unfinished outcomes. |
| Approved design | Records a decision-complete design without assigning implementation order. |
| Candidate register | Records directions that require a product decision. |

## Writing rules

- Give each durable rule or fact one owner; link to that owner elsewhere.
- State current behavior, target design, procedure, approved task, or candidate
  direction directly.
- Keep rationale that affects a current decision; use Git history for revision
  narratives.
- Keep public copy concise and trace every factual claim to a current authority.
- Update this catalog when a maintained document or public surface is added,
  removed, renamed, or changes role. Run `npm run docs:check`.

## Governance and public entry points

| Document | Role | Subject and update trigger |
| --- | --- | --- |
| [Root AI instructions](../AGENTS.md) | Repository authority | Decision order, development policy, invariants, documentation policy, and validation routing. Update with working-policy changes. |
| [Mobile web AI instructions](../apps/mobile-web/AGENTS.md) | Scoped authority | React, TypeScript, target slicing, browser behavior, and mobile validation. Update with mobile engineering-policy changes. |
| [Windows host AI instructions](../apps/windows-host/AGENTS.md) | Scoped authority | .NET, ownership, interop, WPF, resources, and host validation. Update with host engineering-policy changes. |
| [Automation AI instructions](../scripts/AGENTS.md) | Scoped authority | Process safety, cleanup, tooling, packaging, and script validation. Update with automation-policy changes. |
| [Root README](../README.md) | Derived public | Product orientation, screenshots, trust, installation choices, development entry point, and authority links. |
| [Contributing guide](../CONTRIBUTING.md) | Contributor authority | Contribution scope, checks, conduct, and security routing. |
| [Code of conduct](../CODE_OF_CONDUCT.md) | Governance authority | Community behavior and enforcement. |
| [Privacy policy](../PRIVACY.md) | Privacy authority | Local data handling, external services, diagnostic logging, retention, and deletion. Update when those behaviors change. |
| [Security policy](../SECURITY.md) | Security authority | Private vulnerability reporting and disclosure expectations. |
| [Branding asset guide](../assets/branding/README.md) | Asset authority | Artwork inputs, generated consumers, and retained source assets. |
| [Bug-report form](../.github/ISSUE_TEMPLATE/bug_report.yml) | Public intake | Reproduction, environment, connection, and redacted diagnostic fields. |

## Product, engineering, operations, and planning

| Document | Role | Subject and update trigger |
| --- | --- | --- |
| [Architecture](architecture.md) | Target authority | Subsystem ownership, dependency direction, resource inventory, invariants, bundle budget, and source-size review. |
| [Feature inventory](features.md) | Current authority | Implemented product capabilities and guarantees. Update with user-visible behavior. |
| [One-page marketing site](site/index.php) | Derived public | Core use cases, capability summary, trust, screenshots, and downloads. |
| [Machine-readable product summary](site/llms.txt) | Derived public | Compact product facts and authority links for AI systems. |
| [Windows host quality](host-quality.md) | Engineering authority | Analyzer policy, runtime ownership, resource expectations, and host quality gate. |
| [Network and host selection](network-and-host-selection.md) | Current authority | Adapter/port selection, host hints, saved-PC behavior, validation, and recovery. |
| [Pairing feedback](pairing-feedback.md) | UX authority | Pairing and connection states, failure mapping, recovery, layout, and diagnostics. |
| [Protocol](protocol.md) | Wire authority | Messages, authentication, capabilities, acknowledgements, bounds, and errors. |
| [Security architecture diagrams](security-architecture-diagrams.md) | Derived engineering | Security-sensitive runtime, pairing, authorization, and release-flow diagrams. Update when trust boundaries, authentication, authorization, or release artifact production changes. |
| [Release](release.md) | Operational | Versioning, verification, packaging, and publication. |
| [Release notes](release-notes.md) | Operational | User-facing notes consumed by the local release command. |
| [Screenshot capture](screenshots.md) | Operational | Isolated screenshot and installer-artwork capture. |
| [Setup](setup.md) | Operational | Installation, development startup, host options, and first connection. |
| [Site deployment](site-deployment.md) | Operational | Marketing-site deployment and hosting behavior. |
| [Project TODO](todo.md) | Approved TODO | P1 Presentation graduation. |
| [PC-assisted Dictate plan](dictate-pc-assist-plan.md) | Approved design | Alpha-gated PC microphone and Windows Voice Typing assistance within Dictate; implementation order is unassigned. |
| [Candidate directions](ideas.md) | Candidate register | Product, platform, distribution, and project directions awaiting decisions. |
| [Troubleshooting](troubleshooting.md) | Operational | Pairing, network, input, pointer, text, logging, and host recovery. |
| [Windows host UI guidelines](host-ui-guidelines.md) | Target authority | WPF composition, accordion, scrolling, diagnostics, feedback, and tray behavior. |
| [Product UI system](ui-system.md) | Target authority | UX principles, tokens, primitives, layout, vertical slicing, accessibility, and UI completion. |
