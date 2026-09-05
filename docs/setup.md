# Development and validation

## Development workflows

The [source quick start](../README.md#develop-from-source) owns prerequisites
and the default launch. Browse all commands with `npm run help`. Use
`npm install` only when changing dependency manifests.

The repository pins its .NET SDK, Node/npm range, NuGet graph, and npm graph.
Run `npm run tools:check` after installing or updating prerequisites, and
`npm run deps:check` for the non-mutating npm, NuGet, container, and native
dependency audit.

Install the pinned PowerShell analyzer once with
`Install-PSResource PSScriptAnalyzer -Version 1.25.0 -Scope CurrentUser`;
`npm run powershell:check` validates every script with its declared edition.

Fast real-device validation of current sources:

```powershell
./scripts/host-preflight.ps1
npm run dev:quick
```

Use that two-command preflight only when launching or replacing the validation
host. It stops the current host, then `dev:quick` performs an unchecked fast
mobile bundle when its inputs changed and an incremental host build. It intentionally uses normal
production settings so the human validates real device/configuration behavior.
Restart the flow after source edits. It does not replace the risk-appropriate
checks below.

For detailed Screen View troubleshooting, enable **Write application log** in
the host and set `$env:VOLTURA_SCREEN_TRACE = "1"` before starting `npm run dev:quick`.
The debug host reads this flag once at startup. It adds quality-change entries
and aggregate capture/encoder/receiver progress about every ten seconds while
receiver feedback arrives. `dev:quick` does not set the flag itself, and the
Developer mode checkbox does not control it. Remove it with
`Remove-Item Env:VOLTURA_SCREEN_TRACE -ErrorAction SilentlyContinue` before the
next launch to return to ordinary logging. Release builds omit these trace calls.
The development mobile client also writes event-only Screen View WebRTC state
transitions to the browser console; it never logs frames or payloads.

Direct Vite LAN client:

```powershell
$env:VOLTURA_AIR_USE_VITE_CLIENT = "1"
npm run dev
```

Run one side with `npm run dev:web` or `npm run dev:host`.

The local site initializer owns the ignored PHP 8.5/MariaDB configuration and
applies the additive telemetry schema independently from the fresh Custom
Screens catalog schema:

```powershell
npm run site:dev:init
npm run site:check
npm run test:site-telemetry-integration
```

The telemetry integration command is mandatory before a telemetry release. It
uses uniquely identifiable local fixtures, exercises success/failure/rollback,
rate, retention, dashboard, and cleanup boundaries, and removes its fixtures
without clearing catalog data. It never reaches the hosted one.com database.

Automated tests, screenshot capture, `dev:ui`, and temporary hosts use loopback,
disposable pairing/settings, and `--isolated-test-mode`; they never access
production settings or run beside the normal host. `npm run test:ui` is the
isolated real-pairing smoke flow.

## Validation by change

Run the smallest relevant checks:

| Change                                                                                      | Default checks                                                                                                                                                                                                                                                                                           |
| ------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Documentation/public copy                                                                   | `npm run docs:check`                                                                                                                                                                                                                                                                                     |
| Documentation checker or command help                                                       | Relevant `tests/scripts/<file>` test plus `npm run docs:check`                                                                                                                                                                                                                                           |
| Ordinary mobile code                                                                        | `npm run check --workspace apps/mobile-web`; focused Vitest only for changed behavior/state                                                                                                                                                                                                              |
| Mobile bundle, dependency, entry point, or broad integration                                | Mobile production build; full `npm run test:web` only for broad work or shared foundation/protocol/app shell                                                                                                                                                                                             |
| Ordinary host code                                                                          | Warning-free `dotnet build VolturaAir.slnx`; focused `dotnet test --filter` for changed behavior                                                                                                                                                                                                         |
| Host source structure                                                                       | Add `npm run host:ownership:check`                                                                                                                                                                                                                                                                       |
| Shared host lifecycle, native/resource, registry/persistence, network, or protocol boundary | Focused production-path boundary tests; full `npm run test:host` only when broad/shared                                                                                                                                                                                                                  |
| Interaction/transport hot path                                                              | Prove delayed media, analytics, logging, persistence, and UI work cannot hold command/input processing; test bounded overload rather than latency growth                                                                                                                                                 |
| Usage statistics host/protocol                                                              | Focused consent/identity/sender/session/protocol tests, including blocked sender and cancellation; mobile capability/coalescing tests; warning-free host/mobile build                                                                                                                                    |
| Usage statistics PHP/MariaDB/dashboard                                                      | `npm run site:check`, static site telemetry tests, then mandatory `npm run test:site-telemetry-integration` against the configured local database; cover rollback and catalog sentinels                                                                                                                  |
| Usage statistics installer                                                                  | Both package variants plus consent static/transaction tests; verify unset upgrade, existing decision, silent mode, Deny focus, cancellation, write failure, and final real installer matrix                                                                                                              |
| Secure Direct controller transport                                                          | Focused Relay signaling/Origin tests, mobile lifecycle/parser tests, host admission/native-boundary tests, bundle/size gates, then real-device private-LAN setup and signaling-loss validation; preserve the selected transport without automatic fallback                                               |
| Gyro or motion input                                                                        | Focused motion mapping, permission, hook cleanup, Trackpad, and app-navigation tests; mobile check/build as scoped; then real sensor, orientation, visibility, and permission validation over HTTPS                                                                                                      |
| Screen viewing                                                                              | Fake-capture `TestServer` protocol/crypto/cleanup tests, mobile parser/renderer tests, bundle/size gates, then Windows preflight and `npm run dev:quick`; real phone/Wi-Fi viewing remains user acceptance                                                                                               |
| Files on PC                                                                                 | Mobile pagination/selection/gesture/transfer-storage tests, strict protocol tests, focused host file-system/clipboard/job/cleanup tests, host ownership and size gates, then real one-file upload/download acceptance over Direct and Relay, including slow-iPhone negotiation and permission revocation |
| Script                                                                                      | Relevant script test; full `npm run test:scripts` only for shared orchestration/root package composition                                                                                                                                                                                                 |
| Significant UI                                                                              | Visual checkpoint by default; `npm run test:ui` only when its real pairing/smoke flow changes                                                                                                                                                                                                            |
| Structural/source ownership                                                                 | `npm run size:check`                                                                                                                                                                                                                                                                                     |
| Release or repository-wide shared contract                                                  | Sequential `npm run build` then `npm test`                                                                                                                                                                                                                                                               |

UI-only work also receives focused visual verification. Changed external or
resource boundaries cover success, expected failure, and cleanup/restoration.

## Host options

Packaged Release:

| Option                 | Purpose                                                                            |
| ---------------------- | ---------------------------------------------------------------------------------- |
| `--minimized`          | Start without opening the window.                                                  |
| `--isolated-test-mode` | Loopback-only isolated settings, pairing, network choice, and safe system actions. |

Debug additionally supports:

| Option                                         | Purpose                                                                                   |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `--client-url <URL>`                           | Put a development client URL in the pairing link; `VOLTURA_AIR_CLIENT_URL` is equivalent. |
| `--print-host-client-url`                      | Print the selected host URL for `dev:host`.                                               |
| `--pairing-store-root <path>`                  | Redirect pairing data; requires isolation.                                                |
| `--pairing-url-file <path>`                    | Write a temporary private live pairing URL for automation.                                |
| `--site-screenshot-mode`                       | Public-safe rendering; requires isolation.                                                |
| `--site-screenshot-theme <Light                | Dark                                                                                      | System>` | Select capture theme. |
| `--site-screenshot-preferences-section <name>` | Open a Preferences section for capture.                                                   |
| `--site-screenshot-relay-connection`           | Open Connection with Relay selected for an isolated UI review capture.                    |

Release builds ignore Debug-only options and `VOLTURA_AIR_CLIENT_URL`.

## Product limits

The host targets Windows 11. Standard Local and Enhanced Direct require a
reachable private LAN; Enhanced Direct also requires internet access to load
the hosted controller and complete setup. Cloud Relay supports internet control
without an inbound PC firewall exception. Browser speech recognition and motion
input depend on browser/origin/device support. Normal input cannot control UAC,
secure desktop, lock screen, or higher-integrity apps. Firewall/network isolation
can block traffic, and an unreachable/sleeping/shut-down host cannot receive
commands.

Encrypted Screen viewing requires the intended device's **View PC screen**
permission. A freshly paired client must have the PC identity pin.
Desktop Duplication GPU frames, cursor metadata, and default Windows multimedia
output support one display and one viewer at a time. D3D11 conversion and a hardware Media Foundation
H.264 encoder plus WASAPI loopback/Opus feed an adaptive WebRTC stream using direct LAN
ICE or Relay-only TURN, up to 60 frames per second within the negotiated codec,
hardware, receiver, and network capabilities. The browser must support H.264
and Opus WebRTC playback. PC-sound capture remains 48 kHz stereo; High, Standard,
and Low configure nominal 96 kbps stereo, 64 kbps stereo, and 48 kbps mono output.
Choose the PC default under **Preferences → Screen viewing** and optional device
overrides under Windows **Devices → Screen viewing** or the paired device's
**Menu → Settings → Screen viewing**. Each new peer starts device playback muted. Pinch/spread
magnifies the local mirror up to 10×, and two-finger drag pans while magnified.
UAC/secure desktop, protected content,
lock/session loss, display removal, or duplication loss stops/pauses the
mirror; development and tests never substitute a real capture source in
isolated mode.

Phone webcam audio is optional and off by default. It requires the base VB-CABLE
device installed separately by the user from [VB-Audio](https://vb-audio.com/Cable/).
VB-CABLE is third-party donationware and is not included, installed, updated,
licensed, or removed by Voltura Air. After detection succeeds, select `CABLE Output`
as the microphone in the receiving Windows application.
