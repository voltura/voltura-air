# Setup

## Install Release Build

Download `VolturaAir-Setup-<version>-win-x64.exe` from the latest GitHub release and run it on the Windows PC. The installer:

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Creates Start Menu shortcuts.
- Does not require administrator rights.
- Keeps pairing and settings data under `%APPDATA%\Voltura Air` when uninstalled.

Use the in-app setting to choose whether Voltura Air starts with Windows.

## Developer Run

1. Install dependencies:

   ```powershell
   npm install
   ```

2. Build the mobile web app and Windows host:

   ```powershell
   npm run build
   ```

3. Start the Windows host:

   ```powershell
   dotnet run --project apps/windows-host/VolturaAir.Host.csproj
   ```

4. Scan the QR code shown by the Windows host from a phone or tablet connected to the same Wi-Fi/LAN.

## Development Run

Use the developer loop while changing the app:

```powershell
npm run dev
```

This starts Vite on port `5173` for browser-based React fast refresh and starts the Windows host with `dotnet run`. The QR code opens the Windows host URL on the phone, so the app and `/ws` use the same host port.

For installed mobile-app testing, enable **Preferences** -> **Developer tools** -> **Developer mode** in the Windows host. When developer mode is enabled, auto refresh keys off the current host run instead of the release version.

Use the Vite client on a phone when direct mobile hot reload is needed:

```powershell
$env:VOLTURA_AIR_USE_VITE_CLIENT = "1"
npm run dev
```

With `VOLTURA_AIR_USE_VITE_CLIENT=1`, the QR opens the Vite URL and includes the Windows host URL as `h` so pairing and input use the host process.

To debug only one side:

```powershell
npm run dev:web
npm run dev:host
```

## Pairing And Reconnects

The QR code contains the host URL and a short-lived pairing token. The mobile app sends that token over the WebSocket connection. The Windows host returns a client secret, stores a hash of it in `%AppData%\Voltura Air`, and the mobile app stores the secret in browser storage for reconnects.

The QR link also includes the current app version. The host serves the mobile
app shell and service worker with no-store cache headers, and the installed PWA
uses versioned service-worker caches so scanning a fresh QR code should fetch
the matching mobile app instead of running stale cached code.

Paired devices are managed by the Windows host and can reconnect without scanning a new QR code while the saved secret remains available in the browser or installed app storage. The mobile app also keeps saved PC profiles so a phone can reconnect to a known PC or forget old PCs from Settings.

Use the Windows tray menu or the Voltura Air window to open **Devices**. The Devices page shows connected and paired devices, lets you set a per-device pointer speed override, lets you disconnect or remove devices, and can clean up duplicate pairings created by browser/home-screen storage changes.

## Mobile Controls

- **Trackpad** supports pointer movement, tap-to-click, left/right click buttons, two-finger scrolling, pinch zoom, host default/device override pointer speed, scroll direction, and an expanded trackpad surface.
- **Keyboard** supports live typing, buffered text send, on-screen Backspace/Enter/Tab/Escape/Win/Space, arrow navigation, common modifier shortcuts, optional function keys, and single-key app shortcuts such as `F` for video fullscreen. Delete can be forwarded from a physical/browser keyboard when the browser emits it, but there is no dedicated on-screen Delete button yet.
- **Remote** supports media playback, press-and-hold seek, video fullscreen, browser fullscreen, volume, mute, Standard/YouTube/Kodi shortcut modes, Kodi stop/info/subtitle shortcuts, a default navigation ring with repeatable directional zones and a mini-trackpad center that sends Enter in Kodi mode, optional legacy D-pad with OK, Start, Alt+Tab, and Browser Back.
- **Dictate** sends browser speech recognition text to Windows when the browser supports speech recognition for the current origin.
- **PC volume** controls appear on the trackpad screen when the host can read the default Windows output device state.

Mobile settings are stored locally per device and include trackpad behavior, keyboard function keys, remote behavior, app auto refresh, saved PCs, device name, theme, and home-screen app refresh/install actions. Pointer speed uses the Windows host default unless that device has an override; changing the mobile pointer speed slider updates that device override on the host.

## Windows Host

The tray icon menu can show Voltura Air, open Devices, open Settings, show Technical details, open the product page, or exit the host.

Windows host settings include:

- Start Voltura Air when signing in to Windows.
- Show or hide connection status notifications.
- Host-enforced global and per-device permissions.
- Default pointer speed for paired devices, with optional per-device overrides.
- Default Remote mode for newly connected mobile clients.
- Connection settings for host networking.
- System, light, and dark appearance modes.

## Limitations

- Voltura Air is LAN-only. There is no internet relay or account system.
- The web app is served over HTTP for local development. Some browser speech APIs are stricter over insecure origins, so dictation support depends on the browser.
- Windows may block input injection into elevated/admin windows when the host is not also elevated.
- Firewalls can block inbound LAN traffic. Allow the host app through Windows Defender Firewall when prompted.


## Connection health

After pairing, the mobile app keeps checking the host with lightweight health
messages. It checks more quickly while input is active, slows down after the
foreground app is idle, and closes the WebSocket while the browser page or
installed app is backgrounded. When the host advertises input acknowledgement
support, recent pointer and keyboard input must also be acknowledged. If health
checks or input acknowledgements fail, the mobile app shows unavailable/retrying
instead of leaving dead controls on screen.
