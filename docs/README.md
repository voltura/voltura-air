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
| [Third-party software notices](../THIRD-PARTY-NOTICES.md) | Legal/attribution authority | Shipped components, licenses, source availability, and distribution notices. |
| [Brand assets](../assets/branding/README.md) | Authority | Artwork sources/consumers. |
| [Bug form](../.github/ISSUE_TEMPLATE/bug_report.yml) | Public intake | Safe reproduction/diagnostics. |
| [Website](site/index.php), [human sitemap](site/sitemap.php), [XML sitemap](site/sitemap.xml), [relay short redirect](site/a/index.php), [Secure Direct short redirect](site/s/index.php) | Public/service | Use cases, trust, downloads, public navigation, search discovery, and fragment-preserving hosted entries. |
| [Custom-screen catalog](site/screens/index.php), [view](site/screens/view.php), [device preview](site/screens/preview-frame.php), [install](site/screens/install.php), [download](site/screens/download.php), [login](site/screens/login.php), [register](site/screens/register.php), [upload](site/screens/upload.php), [edit](site/screens/edit.php), [withdraw](site/screens/withdraw.php), [remove rejected](site/screens/remove-rejected.php), [moderation](site/screens/admin.php), [official import](site/screens/official-import.php), [deletion](site/screens/delete.php), [rating](site/screens/rate.php), [report](site/screens/report.php), [logout](site/screens/logout.php), [catalog helpers](site/screens/lib.php) | Public/service | Reviewed custom-screen sharing, account, moderation, official bulk import, deletion, rating, preview, and download surfaces. |
| [Machine summary](site/llms.txt) | Public | Compact public facts/links. |
| [Code statistics](site/stats.html) | Generated | Regenerate with `npm run code:statistics -- --report`. |

## Product and engineering

| Document | Role | Read/update |
| --- | --- | --- |
| [Architecture](architecture.md) | Target | Dependencies, owners, resources, size. |
| [Secure-context browser spike](../apps/secure-web-spike/README.md), [WebRTC host spike](../apps/webrtc-spike-host/README.md) | Historical evidence | Real-device feasibility harnesses; production contracts remain in the owners below. |
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
| [Relay deployment](relay-deployment.md) | Operations | Cloudflare setup, quota checks, and advanced WSL self-hosting. |
| [TODO](todo.md) | Approved work | Prioritized, decision-ready outcomes. |
| [Ideas](ideas.md) | Possible work | Directions awaiting decisions/evidence. |
