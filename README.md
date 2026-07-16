# Voltura Air

<p align="center">
  <img src="apps/windows-host/Assets/VolturaAir-256.png" alt="Voltura Air application icon" width="128">
</p>

Turn any phone, tablet, or touch browser into a wireless remote for your Windows PC.

Use it as a trackpad, keyboard, dictation surface, and media remote — including YouTube and Kodi modes for couch and TV control. No app-store install, account, or cloud needed.

Scan the QR code from a phone, tablet, or browser-capable device on the same network to pair it with your PC. You can also install the web app to your home screen.

## What you can do

- Use a trackpad, keyboard, dictation, media remote, and app controls from your phone or tablet.
- Send or paste multiline text to the focused Windows app, copy it to the PC clipboard, create a new document in a configured app, or explicitly fetch the PC clipboard into selectable web-app text when the host permits it.
- Use a dedicated presenter surface for PowerPoint, Google Slides, and browser/PDF navigation, with acknowledged Next/Previous/End commands, target-safe Black screen and laser controls, and a local elapsed timer.
- Open a reviewed HTTP or HTTPS address once in the PC's default browser, with a separate host permission and retryable result.
- Lock the PC, blackout displays, or use approved power controls with deliberate confirmation for destructive actions.
- Choose per-device permissions and pointer speed, plus a host-wide Custom pointer for the Windows desktop.
- Use it on your own Wi-Fi/LAN with no account or cloud service.

## Best for

- Controlling a PC connected to a TV, stereo, or home-theater system.
- Browsing and watching YouTube from the sofa or bed.
- Navigating Kodi with dedicated playback and interface controls.
- Using your phone as a trackpad, keyboard, media remote, and app launcher.
- Replacing a broken, unreliable, or inconvenient wireless keyboard and mouse.
- Controlling presentations without staying beside the PC.
- Quickly typing, pasting, searching, or dictating text from your phone.
- Using a landscape tablet as a combined keyboard and trackpad, with the trackpad on either side.

## Screenshots

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/site/assets/voltura-air-host-dark.png">
    <img src="docs/site/assets/voltura-air-host.png" alt="Voltura Air Windows host pairing screen" width="900">
  </picture>
  <br>
  <sub>Windows host pairing screen</sub>
</p>

<table>
  <tr>
    <td align="center" width="34%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/site/assets/voltura-air-iphone-dark.png">
        <img src="docs/site/assets/voltura-air-iphone.png" alt="Voltura Air trackpad on a phone" width="320">
      </picture>
      <br>
      <sub>Phone trackpad</sub>
    </td>
    <td align="center" width="66%">
      <img src="docs/site/assets/voltura-air-split.png" alt="Voltura Air split keyboard and trackpad on a landscape tablet">
      <br>
      <sub>Landscape split keyboard and trackpad</sub>
    </td>
  </tr>
</table>

## Not intended for

- Remote support over the internet.
- Full remote-desktop replacement.
- Phone notification sync.
- File backup/sync.

## How it works

Install the Windows host on the PC you want to control. It runs from the tray, displays a pairing QR code, and serves the mobile controls over your local network. Only one host runs per signed-in Windows user.
At startup, the tray stays neutral briefly while previously paired phones reconnect. Its connected badge also stays visible through a brief automatic-reconnect grace period, so a phone refresh does not flash a disconnected state.

The host keeps device permissions and app-launch settings on the PC. You can choose which devices may use remote, power, keep-awake, app-launch, and URL-opening controls. URL opening is off by default, appears on the phone only when allowed, accepts only HTTP and HTTPS, and uses the Windows default browser. Sensitive actions require confirmation, and remote wake is not available after a PC sleeps or shuts down.

Preferences includes device management, Keep awake, remote/app controls, and an optional diagnostics log. You can add Browser, Spotify, VLC, PowerPoint, or custom app buttons to the Remote panel; custom paths and arguments stay on the PC.

## Features

Voltura Air's full capability list is in [docs/features.md](docs/features.md).
Setup, pairing recovery, manual network selection, and protocol details live in
the dedicated documentation below.

Pairing failure and recovery behavior is documented in
[docs/pairing-feedback.md](docs/pairing-feedback.md). Manual network/host
selection behavior is documented in
[docs/manual-network-selection.md](docs/manual-network-selection.md). Protocol
message shapes are documented in [docs/protocol.md](docs/protocol.md).

The optional **Custom pointer** applies across the desktop. Choose its size and color in Windows Preferences, or turn it on and off from a paired device. It restores the user's normal scheme when switched off or when the host exits. UAC prompts, the lock screen, and other secure Windows surfaces cannot be remotely controlled.

## Requirements

For normal use:

- Windows 11 PC.
- Phone, tablet, or browser-capable device on the same Wi-Fi/LAN as the PC.

For development and release packaging:

- .NET 10 SDK.
- Node.js and npm.
- Visual Studio Build Tools with the Desktop development with C++ workload, which builds the native cursor watchdog.
- NSIS 3.12 or later when building Windows installer assets.

For normal installation, choose one installer from the latest GitHub release:

- `VolturaAir-Setup-<version>-win-x64.exe` downloads and installs the required .NET 10 Desktop and ASP.NET Core runtimes when they are missing. An internet connection is required in that case, and Windows may request administrator approval.
- `VolturaAir-Setup-<version>-win-x64-full.exe` includes all required components and installs per user without administrator rights.

Both installers keep pairing and settings data when uninstalled.

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

This creates the portable zip plus the default and full NSIS installers under `artifacts/publish`.

To quickly create only the small installer that downloads missing .NET runtimes, run:

```powershell
npm run package:win:small
```

This creates `VolturaAir-Setup-<version>-win-x64.exe` and skips the portable zip and full installer.

## Regenerate Branding

The transparent, sticker-outlined `assets/branding/voltura-air-master.png`
artwork is the source of truth for every generated product icon. The borderless
`voltura-air-borderless-for-safekeeping.png` copy is retained only as an artwork
backup and is not consumed by the scripts. The generator adds the Windows-green
check connected badge and a quieter red cross disconnected badge. After
replacing the production master PNG, regenerate the mobile, iOS, Android,
Windows host, installer, marketing-site, and README-referenced assets with:

```powershell
npm run icons:generate
```

On Windows, refresh those assets and all marketing-site screenshots together
with:

```powershell
npm run branding:generate
```

The screenshot capture pairs a browser with a separate loopback host. Its
pairing data and host settings are isolated, so capture cannot reset or change
the normal Windows-host preferences.

Do not edit `apps/mobile-web/dist` or a host `wwwroot` copy. They are generated
from `apps/mobile-web/public` by the normal build and packaging commands.

## Run From CLI

```powershell
npm install
npm run build --workspace apps/mobile-web
dotnet run --project apps/windows-host/VolturaAir.Host.csproj
```

The Windows app opens the Voltura Air window and a tray icon near the clock. Scan the QR code on the Connect page from the phone, tablet, or browser-capable device to open the mobile app and pair it with the PC.

Use the tray icon's context menu to show Voltura Air, open Devices or Preferences, control Keep awake, open the product page, or exit the host.

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

## Sync A Feature Branch

From a clean checked-out branch other than `main`, run:

```powershell
npm run branch:sync
```

The helper fetches the latest `origin/main` and merges it into the current branch without switching branches. It refuses a dirty worktree or `main`; resolve any merge conflicts through the normal Git workflow.

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


## Statistics

[![Visitors](https://hits.sh/github.com/voltura/voltura-air.svg?style=flat&label=visitors&labelColor=555&color=5690f2&extraCount=19)](https://hits.sh/github.com/voltura/voltura-air/)
[![Code size](https://img.shields.io/github/languages/code-size/voltura/voltura-air)](https://github.com/voltura/voltura-air)
[![Stars](https://img.shields.io/github/stars/voltura/voltura-air)](https://github.com/voltura/voltura-air/stargazers)
[![Forks](https://img.shields.io/github/forks/voltura/voltura-air)](https://github.com/voltura/voltura-air/forks)
[![Last commit](https://img.shields.io/github/last-commit/voltura/voltura-air?color=red)](https://github.com/voltura/voltura-air/commits)
[![Languages](https://img.shields.io/github/languages/count/voltura/voltura-air)](https://github.com/voltura/voltura-air)
[![Top language](https://img.shields.io/github/languages/top/voltura/voltura-air)](https://github.com/voltura/voltura-air)
