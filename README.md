# Voltura Air

Turn your phone, tablet, or browser into a wireless trackpad and keyboard for Windows.

Voltura Air turns a browser-capable device into a trackpad, keyboard, dictation surface, and volume remote for a Windows 11 PC.

The project is split into two apps:

- `apps/mobile-web`: a React/TypeScript PWA for Android, iPhone, iPad, tablets, ChromeOS, and other modern browsers.
- `apps/windows-host`: a .NET 10 Windows tray app that shows a QR code, hosts the PWA on the LAN, receives WebSocket commands, and injects input into Windows.

The Windows host installs per user, runs from the tray, manages paired devices, and serves the mobile app over the local network. The mobile app can be used directly in the browser or installed to the home screen.

## Features

- QR-code pairing with saved reconnects for trusted phones, tablets, and browser-capable devices.
- Trackpad input with tap-to-click, physical left/right buttons, two-finger scroll, pinch zoom, pointer speed, scroll direction, and an expanded full-screen trackpad mode.
- Keyboard input with live typing, buffered send, navigation/editing keys, modifier shortcuts, and optional function keys.
- Dictation through the browser speech recognition API when supported by the browser and origin.
- PC volume and mute controls from the trackpad screen.
- Device management on both sides, including saved PC profiles in the mobile app and paired-device cleanup in the Windows host.
- Light, dark, and system theme modes for the mobile app and Windows host.
- Windows startup and connection notification settings.
- Release packaging for a portable zip and per-user NSIS installer.

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

## Windows security warning

Current early builds are not code-signed. Windows may show an unknown publisher or Microsoft Defender SmartScreen warning when the installer or executable is new.

Only download Voltura Air from the official product page or the official GitHub releases page:

- Product page: <https://voltura.se/air>
- GitHub releases: <https://github.com/voltura/voltura-air/releases/latest>

If the project gets real public adoption, code signing or Microsoft Store distribution can be added later.

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

The Windows app opens a pairing window and a tray icon near the clock. Scan the QR code from the phone, tablet, or browser-capable device to open the mobile app and pair it with the PC.

Use the tray icon's context menu to show the QR code, open the device manager, edit settings, inspect technical details, open the product page, or exit the host.

## Local Development

Run both dev servers:

```powershell
npm run dev
```

This starts the React/Vite PWA on port `5173` with client-side hot reload and starts the Windows host through `dotnet run`. The pairing QR opens the Vite LAN URL, but includes the .NET host URL for `/ws`, so the phone uses the hot-reloading client while input messages go to the host process.

Run only one side when needed:

```powershell
npm run dev:web
npm run dev:host
```

`npm run dev:host` stops any existing `VolturaAir.Host.exe` process before starting `dotnet run` so the Debug build output and preferred port are not locked. Override the client URL if needed:

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
