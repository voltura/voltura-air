# Voltura Air

Turn your phone, tablet, or browser into a wireless trackpad and keyboard for Windows.

Voltura Air turns a browser-capable device into a trackpad, keyboard, dictation surface, and volume remote for a Windows 11 PC.

## Product promise

- Control your Windows PC from any phone, tablet, or modern browser.
- No mobile app-store install required.
- Local-first. No account. No cloud needed. No paywall.

## Best for

- PC connected to TV/stereo.
- Sofa/bed control.
- Broken or annoying wireless keyboard/trackpad replacement.
- Presentations.
- Quick typing/search from phone.

## Not intended for

- Remote support over the internet.
- Full remote-desktop replacement.
- Phone notification sync.
- File backup/sync.

The project is split into two apps:

- `apps/mobile-web`: a React/TypeScript PWA for Android, iPhone, iPad, tablets, ChromeOS, and other modern browsers.
- `apps/windows-host`: a .NET 10 Windows tray app that shows a QR code, hosts the PWA on the LAN, receives WebSocket commands, and injects input into Windows.

The Windows host installs per user, runs from the tray, manages paired devices, and serves the mobile app over the local network. The mobile app can be used directly in the browser or installed to the home screen.

## Features

Voltura Air's high-level host and client capabilities are listed in
[docs/features.md](docs/features.md). Keep that page at product capability
level so contributors can quickly understand what the app can do without
turning the README into detailed implementation documentation.

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

See [docs/setup.md](docs/setup.md), [docs/release.md](docs/release.md), and [docs/protocol.md](docs/protocol.md) for details.
