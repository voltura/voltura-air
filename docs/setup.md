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

This starts Vite on port `5173` for React fast refresh and starts the Windows host with `dotnet run`. The QR code opens the Vite URL on the phone and passes the .NET host URL as compact query parameter `h`, so pairing and input still use the Windows host.

To debug only one side:

```powershell
npm run dev:web
npm run dev:host
```

## Pairing And Reconnects

The QR code contains the host URL and a short-lived pairing token. The mobile app sends that token over the WebSocket connection. The Windows host returns a client secret, stores a hash of it in `%AppData%\Voltura Air`, and the mobile app stores the secret in browser storage for reconnects.

Paired devices are managed by the Windows host and can reconnect without scanning a new QR code while the saved secret remains available in the browser or installed app storage. The mobile app also keeps saved PC profiles so a phone can reconnect to a known PC or forget old PCs from Settings.

Use the Windows tray menu or pairing window to open **Device manager**. The device manager shows connected and paired devices, lets you disconnect or remove devices, and can clean up older duplicate pairings created by browser/home-screen storage changes.

## Mobile Controls

- **Trackpad** supports pointer movement, tap-to-click, left/right click buttons, two-finger scrolling, pinch zoom, pointer speed, scroll direction, and an expanded trackpad surface.
- **Keyboard** supports live typing, buffered text send, Backspace/Delete/Enter/Tab/Escape, arrow navigation, common modifier shortcuts, and optional function keys.
- **Dictate** sends browser speech recognition text to Windows when the browser supports speech recognition for the current origin.
- **PC volume** controls appear on the trackpad screen when the host can read the default Windows output device state.

Mobile settings are stored locally per device and include trackpad behavior, keyboard function keys, saved PCs, device name, theme, and home-screen app refresh/install actions.

## Windows Host

The tray icon menu can show the QR code, open Device manager, open Settings, show Technical details, open the product page, or exit the host.

Windows host settings include:

- Start Voltura Air when signing in to Windows.
- Show or hide connection status notifications.
- Connection settings for host networking.
- System, light, and dark appearance modes.

## Limitations

- Voltura Air is LAN-only. There is no internet relay or account system.
- The web app is served over HTTP for local development. Some browser speech APIs are stricter over insecure origins, so dictation support depends on the browser.
- Windows may block input injection into elevated/admin windows when the host is not also elevated.
- Firewalls can block inbound LAN traffic. Allow the host app through Windows Defender Firewall when prompted.
