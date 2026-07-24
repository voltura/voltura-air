# Development and validation

## Development workflows

The [source quick start](../README.md#develop-from-source) owns prerequisites
and the default launch. Browse all commands with `npm run help`. Use
`npm install` only when changing dependency manifests.

Fast real-device validation of current sources:

```powershell
./scripts/host-preflight.ps1
npm run dev:quick
```

Use that two-command preflight only when launching or replacing the validation
host. It stops the current host, then `dev:quick` performs an unchecked fast
mobile bundle and incremental host build. It intentionally uses normal
production settings so the human validates real device/configuration behavior.
Restart the flow after source edits. It does not replace the risk-appropriate
checks below.

Direct Vite LAN client:

```powershell
$env:VOLTURA_AIR_USE_VITE_CLIENT = "1"
npm run dev
```

Run one side with `npm run dev:web` or `npm run dev:host`.

Automated tests, screenshot capture, `dev:ui`, and temporary hosts use loopback,
disposable pairing/settings, and `--isolated-test-mode`; they never access
production settings or run beside the normal host. `npm run test:ui` is the
isolated real-pairing smoke flow.

## Validation by change

Run the smallest relevant checks:

| Change | Default checks |
| --- | --- |
| Documentation/public copy | `npm run docs:check` |
| Documentation checker or command help | Relevant `tests/scripts/<file>` test plus `npm run docs:check` |
| Ordinary mobile code | `npm run check --workspace apps/mobile-web`; focused Vitest only for changed behavior/state |
| Mobile bundle, dependency, entry point, or broad integration | Mobile production build; full `npm run test:web` only for broad work or shared foundation/protocol/app shell |
| Ordinary host code | Warning-free `dotnet build VolturaAir.slnx`; focused `dotnet test --filter` for changed behavior |
| Host source structure | Add `npm run host:ownership:check` |
| Shared host lifecycle, native/resource, registry/persistence, network, or protocol boundary | Focused production-path boundary tests; full `npm run test:host` only when broad/shared |
| Script | Relevant script test; full `npm run test:scripts` only for shared orchestration/root package composition |
| Significant UI | Visual checkpoint by default; `npm run test:ui` only when its real pairing/smoke flow changes |
| Structural/source ownership | `npm run size:check` |
| Release or repository-wide shared contract | Sequential `npm run build` then `npm test` |

UI-only work also receives focused visual verification. Changed external or
resource boundaries cover success, expected failure, and cleanup/restoration.

## Host options

Packaged Release:

| Option | Purpose |
| --- | --- |
| `--minimized` | Start without opening the window. |
| `--isolated-test-mode` | Loopback-only isolated settings, pairing, network choice, and safe system actions. |

Debug additionally supports:

| Option | Purpose |
| --- | --- |
| `--client-url <URL>` | Put a development client URL in the pairing link; `VOLTURA_AIR_CLIENT_URL` is equivalent. |
| `--print-host-client-url` | Print the selected host URL for `dev:host`. |
| `--pairing-store-root <path>` | Redirect pairing data; requires isolation. |
| `--pairing-url-file <path>` | Write a temporary private live pairing URL for automation. |
| `--enable-alpha-features` | Enable alpha only inside isolated settings. |
| `--site-screenshot-mode` | Public-safe rendering; requires isolation. |
| `--site-screenshot-theme <Light|Dark|System>` | Select capture theme. |
| `--site-screenshot-preferences-section <name>` | Open a Preferences section for capture. |

Release builds ignore Debug-only options and `VOLTURA_AIR_CLIENT_URL`.

## Product limits

The host targets Windows 11 and LAN use. Browser speech recognition depends on
browser/origin support. Normal input cannot control UAC, secure desktop, lock
screen, or higher-integrity apps. Firewall/network isolation can block LAN
traffic, and an unreachable/sleeping/shut-down host cannot receive commands.
