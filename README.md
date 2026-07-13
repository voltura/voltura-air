# Voltura Air

Turn any phone, tablet, or touch browser into a wireless remote for your Windows PC.

Use it as a trackpad, keyboard, dictation surface, and media remote — including YouTube and Kodi modes for couch and TV control. No app-store install, account, or cloud needed.

## Product promise

Remote mode includes an Fn panel for common Windows window actions and browser tab/page shortcuts, plus a compact Power sheet for locking Windows, turning off displays, signing out, restarting, and shutting down. Destructive actions require a deliberate hold and every power action is controlled by explicit host permissions. Kodi mode separates its video/UI toggle from its fullscreen/windowed control and places supporting actions around the navigation ring. Switch app supports quick previous-app taps plus hold-and-slide visual app selection, and Task view opens the persistent Windows window overview. Taller portrait phones retain a compact navigation ring below the Fn helpers while hiding volume when space is constrained; phone landscape keeps volume and navigation beside the helpers. Phone and portrait-tablet layouts preserve the main control surface, and tapping the active mode collapses the full mode row into a compact selector.

- Control your Windows PC from any phone, tablet, or touch browser.
- No mobile app-store install required.
- Works on your own Wi-Fi/LAN. No account. No cloud needed.
- Freeware with no trial limits or feature locks.

## Best for

- PC connected to TV/stereo.
- Sofa/bed control.
- Broken or annoying wireless keyboard/trackpad replacement.
- Presentations.
- Quick typing/search from phone.
- A combined couch keyboard and trackpad on a landscape tablet, with the trackpad on either side.

## Not intended for

- Remote support over the internet.
- Full remote-desktop replacement.
- Phone notification sync.
- File backup/sync.

The project is split into two apps:

- `apps/mobile-web`: a React/TypeScript PWA for Android, iPhone, iPad, tablets, ChromeOS, and other modern browsers.
- `apps/windows-host`: a .NET 10 Windows tray app that shows a QR code, hosts the PWA on the LAN, receives WebSocket commands, and injects input into Windows.

The Windows host installs per user, runs from the tray, manages paired devices, and serves the mobile app over the local network. Only one host instance runs for the signed-in Windows user; launching Voltura Air again opens and focuses the existing host window. The mobile app can be used directly in the browser or installed to the home screen.

The host has a global permission for whether paired-device input may interact with the Voltura Air host UI and tray menu. When disabled, paired devices can still control Windows and other apps, while native host window controls such as minimize, maximize, and close remain available.

Power and session controls have separate global and per-device permissions. Lock PC and Blackout display are enabled by default; screen saver is shown only when Windows has one enabled and configured; display off, sign out, restart, and shut down require explicit host approval. Blackout covers every monitor with black while Windows, networking, and Voltura Air remain active, then closes on any local or remote input. The host detects explicit current-user workstation-lock policy. When policy disables locking, a local non-elevated **Enable Windows locking** action writes DWORD zero and refreshes policy; when the value is missing or zero, **Test Lock PC** tests Windows directly without an unnecessary registry write. Host results appear inline on mobile. Sign out, restart, and shut down require hold-to-confirm. Turning off a display also cuts HDMI output to TVs and receivers and requires confirmation because some Windows PCs treat the monitor-power command as sleep or Modern Standby. On those PCs Voltura Air disconnects and cannot provide remote wake; physical keyboard or mouse input is required, and Windows may show its sign-in screen after resuming.

Preferences uses themed, single-open accordion sections. An optional sanitized application log is available there and is off by default. Diagnostics opens on a themed, filterable Application log view for remote commands, host actions, outcomes, responses, and Windows errors; the log itself scrolls while the filter and action controls remain reachable.

## Features

Voltura Air's high-level host and client capabilities are listed in
[docs/features.md](docs/features.md). Keep that page at product capability
level so contributors can quickly understand what the app can do without
turning the README into detailed implementation documentation.

Pairing failure and recovery behavior is documented in
[docs/pairing-feedback.md](docs/pairing-feedback.md). Manual network/host
selection behavior is documented in
[docs/manual-network-selection.md](docs/manual-network-selection.md). Protocol
message shapes are documented in [docs/protocol.md](docs/protocol.md).

Connection reliability is part of the product surface. The mobile client must not
stay visually connected when the host is unavailable, health checks fail, or
input delivery stops being acknowledged. Foreground idle checks should stay
lightweight so a paired phone can rest without polling status and audio state.
QR links include the host app version, and the host serves the mobile app shell
and service worker with no-store cache headers so fresh pairing codes are not
masked by stale installed-PWA code.
Scanning a valid new QR while a saved PC is unavailable pauses that stale retry,
keeps the old PC saved, and opens the new pairing confirmation screen.

## Requirements

For normal use:

- Windows 11 PC.
- Phone, tablet, or browser-capable device on the same Wi-Fi/LAN as the PC.

For development and release packaging:

- .NET 10 SDK.
- Node.js and npm.
- NSIS 3.12 or later when building Windows installer assets.

For normal installation, download and run the latest `VolturaAir-Setup-<version>-win-x64.exe` from the GitHub release. The installer installs per user, does not require administrator rights, and keeps pairing/settings data when uninstalled.

## Freeware and support

Voltura Air is freeware from Voltura AB. It can be used without payment, account registration, trial limits, or feature locks.

Optional support links are available for users who want to sponsor development:

- Ko-fi: <https://ko-fi.com/voltura>
- PayPal: <https://www.paypal.me/voltura>

The `.github/FUNDING.yml` file enables GitHub's sponsor button for the repository.

## License

Voltura Air is open source under the [MIT License](LICENSE).

MIT is intentionally permissive: users and contributors can inspect, use, modify, and share the source with minimal friction, while Voltura AB keeps the copyright notice and no-warranty protection.

## Community and security

- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Security policy: [SECURITY.md](SECURITY.md)

Security reports should not include exploit details in public issues. Follow the security policy instead.

## Windows security warning

Release builds are not code-signed. Windows may show an unknown publisher or Microsoft Defender SmartScreen warning when the installer or executable is new.

Only download Voltura Air from the official product page or the official GitHub releases page:

- Product page: <https://voltura.se/air>
- GitHub releases: <https://github.com/voltura/voltura-air/releases/latest>

## Build And Test

```powershell
npm install
npm run build
npm test
```

To create Windows release assets locally, install NSIS 3.12 or later and run:

```powershell
npm run package:win
```

This creates both the portable zip and the NSIS setup executable under `artifacts/publish`.

## Run From CLI

```powershell
npm install
npm run build --workspace apps/mobile-web
dotnet run --project apps/windows-host/VolturaAir.Host.csproj
```

The Windows app opens the Voltura Air window and a tray icon near the clock. Scan the QR code on the Connect page from the phone, tablet, or browser-capable device to open the mobile app and pair it with the PC.

Use the tray icon's context menu to show Voltura Air, open Devices, open Settings, inspect technical details, open the product page, or exit the host.

## Local Development

Run both dev servers:

```powershell
npm run dev
```

This starts the React/Vite PWA on port `5173` for browser-based hot reload and starts the Windows host through `dotnet run`. The pairing QR opens the Windows host URL so phone testing uses the same app and `/ws` origin as release builds.

Use the Vite client on a phone when direct mobile hot reload is needed:

```powershell
$env:VOLTURA_AIR_USE_VITE_CLIENT = "1"
npm run dev
```

With `VOLTURA_AIR_USE_VITE_CLIENT=1`, the QR opens the Vite LAN URL and includes the Windows host URL for `/ws`.

For PC-based manual UI checks in Chrome, run:

```powershell
npm run dev:ui
```

This starts the Vite PWA and Windows host, opens a connected maximized Chrome
session with DevTools and device mode enabled, and selects
`Voltura 393x852 - iPhone Pro` by default. Set
`VOLTURA_AIR_DEV_UI_DEVICE` to one of the seeded `Voltura ...` device names to
start with another preset. The command uses isolated temporary pairing and
browser storage, so debug devices are disposable and do not change the normal
Voltura Air device list. Chrome device emulation is a fast development
complement, not a replacement for real phone, tablet, installed-PWA, and LAN
tests.

`dev:ui` is an interactive manual session. Playwright is used only to launch and
configure Chrome; the command does not assert UI state. Run `npm run test:ui`
for an explicit headless connection smoke test plus Power sheet layout checks at
seeded phone/tablet portrait and landscape sizes. Both commands use loopback-only
services and isolated temporary pairing data.

Run only one side when needed:

```powershell
npm run dev:web
npm run dev:host
```

`npm run dev:host` stops any existing `VolturaAir.Host.exe` process before starting `dotnet run` so the Debug build output and preferred port are not locked. Set a client URL when the phone should load a separate web client:

```powershell
$env:VOLTURA_AIR_CLIENT_URL = "http://192.168.1.20:5173"
npm run dev:host
```

## Debug From VS Code

1. Open this repository folder in VS Code.
2. Install the C# extension or C# Dev Kit if VS Code prompts for it.
3. Open **Run and Debug**.
4. Choose **Debug Windows Host**.
5. Press **F5**.

The VS Code launch task builds the mobile web app first, then builds and starts `VolturaAir.Host.exe` under the debugger.

## Debug From CLI

```powershell
npm run build --workspace apps/mobile-web
dotnet build apps/windows-host/VolturaAir.Host.csproj
dotnet run --project apps/windows-host/VolturaAir.Host.csproj
```

Attach a debugger to the `VolturaAir.Host.exe` process if you need breakpoints while using CLI startup.

See [docs/setup.md](docs/setup.md), [docs/release.md](docs/release.md), [docs/protocol.md](docs/protocol.md), [docs/pairing-feedback.md](docs/pairing-feedback.md), and [docs/manual-network-selection.md](docs/manual-network-selection.md) for details.
