# Documentation map

AI loads root plus the closest scoped `AGENTS.md`, then only task-relevant
sections; full authorities are for broad contract work. Update an authority and
derived public copy together.

Documents describe the present except release notes (history), TODO (approved
work), and Ideas (possible work). Each durable fact has one owner. Generated
files are rebuilt, never hand-edited.

## Governance and public

| Document | Role | Read/update |
| --- | --- | --- |
| [Root AI](../AGENTS.md) | Authority | Always; repository policy/invariants. |
| [Mobile AI](../apps/mobile-web/AGENTS.md) | Authority | Mobile work/policy. |
| [Host AI](../apps/windows-host/AGENTS.md) | Authority | Host work/policy. |
| [Automation AI](../scripts/AGENTS.md) | Authority | Script work/policy. |
| [README](../README.md) | Public authority | Product, download/install, connection, trust, source quick start. |
| [Contributing](../CONTRIBUTING.md) | Authority | Contributor workflow/policy. |
| [Code of Conduct](../CODE_OF_CONDUCT.md) | Authority | Community conduct/enforcement. |
| [Privacy](../PRIVACY.md) | Authority | Data, services, logs, retention, deletion. |
| [Security](../SECURITY.md) | Authority | Vulnerability reporting/trust boundary. |
| [Brand assets](../assets/branding/README.md) | Authority | Artwork sources/consumers. |
| [Bug form](../.github/ISSUE_TEMPLATE/bug_report.yml) | Public intake | Safe reproduction/diagnostics. |
| [Website](site/index.php) | Public | Use cases, trust, screenshots, downloads. |
| [Machine summary](site/llms.txt) | Public | Compact public facts/links. |
| [Code statistics](site/stats.html) | Generated | Regenerate with `npm run code:statistics -- --report`. |

## Product and engineering

| Document | Role | Read/update |
| --- | --- | --- |
| [Architecture](architecture.md) | Target | Dependencies, owners, resources, size. |
| [Features](features.md) | Authority | Visible capabilities, permissions, limits, states. |
| [Protocol](protocol.md) | Authority | Wire shape, bounds, auth, capabilities, acks, errors. |
| [UI system](ui-system.md) | Target | Product UX, tokens, layout, input, accessibility. |
| [Host UI](host-ui-guidelines.md) | Target | WPF composition, scrolling, feedback, tray. |
| [Host quality](host-quality.md) | Authority | Analyzers, lifetimes, boundaries, validation. |
| [Network selection](network-and-host-selection.md) | Authority | Adapter, port, saved PC, manual host, recovery. |
| [Pairing feedback](pairing-feedback.md) | Authority | Pairing/connection states, failures, recovery. |
| [Security diagrams](security-architecture-diagrams.md) | Derived | Security, pairing, authorization, or release-boundary work. |

## Operations and planning

| Document | Role | Read/update |
| --- | --- | --- |
| [Setup](setup.md) | Operations | Advanced development, isolation, validation routing, host options, product limits. |
| [Troubleshooting](troubleshooting.md) | Operations | Recovery by symptom. |
| [Screenshots](screenshots.md) | Operations | Isolated screenshot/installer-art capture. |
| [Release](release.md) | Operations | Version, verification, package, publication. |
| [Release notes](release-notes.md) | History | User-visible release changes. |
| [Site deployment](site-deployment.md) | Operations | Website publication/hosting. |
| [TODO](todo.md) | Approved work | Prioritized, decision-ready outcomes. |
| [Ideas](ideas.md) | Possible work | Directions awaiting decisions/evidence. |
