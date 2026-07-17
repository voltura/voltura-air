# Setup

## Install Release Build

Choose one installer from the latest GitHub release and run it on the Windows PC:

- `VolturaAir-Setup-<version>-win-x64.exe` checks for .NET 10 Windows Desktop and ASP.NET Core runtimes during setup, then downloads and installs any missing runtime. An internet connection is required in that case, and Windows may show an administrator approval prompt because .NET is installed for the PC.
- `VolturaAir-Setup-<version>-win-x64-full.exe` includes all required components and can be installed without an internet connection.

Both installers:

- Installs per user under `%LOCALAPPDATA%\Programs\Voltura Air`.
- Creates Start Menu shortcuts.
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

## Windows Host Command-Line Options

The packaged Release host supports only options needed by normal startup or safe
validation:

| Option | Purpose |
| --- | --- |
| `--minimized` | Starts without opening the main window. Voltura Air uses this for start-at-sign-in behavior. |
| `--isolated-test-mode` | Uses the normal single-instance scope but binds only to loopback, isolates host settings, disables real system power actions, and avoids persisting automatic network or port choices. Temporary validation hosts must use this option. |

Debug builds additionally support these development and capture options:

| Option | Purpose |
| --- | --- |
| `--client-url <URL>` | Places a development client URL in the pairing link instead of the host-served client URL. The `VOLTURA_AIR_CLIENT_URL` environment variable provides the same Debug-only override and origin allowance. |
| `--print-host-client-url` | Writes the selected Windows host URL to standard output after the host has successfully started. `npm run dev:host` uses this option. |
| `--pairing-store-root <path>` | Redirects pairing persistence to a disposable development directory. Use it only with `--isolated-test-mode`. |
| `--pairing-url-file <path>` | Writes the real pairing URL for local automation. The file contains a live short-lived pairing token and must stay temporary and private. |
| `--enable-alpha-features` | Enables alpha features only in the disposable settings scope created by `--isolated-test-mode`. The development UI launcher uses this to exercise alpha surfaces without changing normal host settings. |
| `--site-screenshot-mode` | Enables public-safe screenshot rendering and requires `--isolated-test-mode`. |
| `--site-screenshot-theme <Light\|Dark\|System>` | Selects the required host theme for screenshot mode. |
| `--site-screenshot-preferences-section <name>` | Opens the named Preferences section for a host screenshot. |

Release builds do not process the Debug-only options or
`VOLTURA_AIR_CLIENT_URL`. See [Screenshot and Installer Artwork Capture](screenshots.md)
for the supported capture workflow and required option combinations.

## Pairing And Reconnects

The QR code contains the host URL and a short-lived pairing token. The mobile app sends that token over the WebSocket connection. The Windows host returns a client secret, stores a hash of it in `%AppData%\Voltura Air`, and the mobile app stores the secret in browser storage for reconnects.

The QR link also includes the current app version. The host serves the mobile
app shell and service worker with no-store cache headers, and the installed PWA
uses versioned service-worker caches so scanning a fresh QR code should fetch
the matching mobile app instead of running stale cached code.

Paired devices are managed by the Windows host and can reconnect without scanning a new QR code while the saved secret remains available in the browser or installed app storage. The mobile app also keeps saved PC profiles so a phone can reconnect to a known PC or forget old PCs from Settings.

When an active PC is unavailable, scanning a valid fresh QR code stops retrying that PC and opens the device-name confirmation for the newly scanned PC. The unavailable PC remains in the saved-PC list and can be selected again later.

Use the Windows tray menu or the Voltura Air window to open **Devices**. The Devices page shows connected and paired devices, lets you set per-device pointer speed and permission overrides, lets you disconnect or remove devices, and can clean up duplicate pairings created by browser/home-screen storage changes. **Permissions > Application launch** can inherit the global setting or explicitly allow/block launch controls for that device.

## Mobile Controls

- **Trackpad** supports pointer movement, tap-to-click, left/right click buttons, two-finger scrolling, pinch zoom, host default/device override pointer speed, scroll direction, and an expanded trackpad surface.
- **Keyboard** supports live typing, buffered text send, on-screen Backspace/Delete/Enter/Tab/Escape/Win/Space, repeatable arrow and document navigation, common modifier shortcuts, optional function keys, and single-key app shortcuts such as `F` for video fullscreen.
- **Presentation** is a default-off alpha feature enabled from **Preferences > Developer tools > Enable alpha features**. It provides a dedicated one-screen presenter with large Next/Previous controls, target-specific Start/End and laser-pointer actions, a broad **Blackout** action, and a local Start/Pause/Reset elapsed timer. Choose PowerPoint, Google Slides, or PDF/browser before presenting. PowerPoint uses F5, Escape, and Ctrl+L; Google Slides uses Escape and L after you start presenting on the PC; PDF/browser exposes navigation and exit only. For PowerPoint and Google Slides, **Blackout** uses Voltura Air's separately permissioned black curtain across every monitor rather than the target application's B shortcut. Each Presentation command waits for a host result, and target-incompatible actions are hidden. The shortcut mappings follow the current [PowerPoint delivery shortcuts](https://support.microsoft.com/en-us/office/use-keyboard-shortcuts-to-deliver-powerpoint-presentations-1524ffce-bd2a-45f4-9a7f-f18b992b93a0) and [Google Slides shortcuts](https://support.google.com/docs/answer/1696717).
- **Remote** supports media playback, press-and-hold seek, video fullscreen, browser fullscreen, volume, mute, Standard/YouTube/Kodi shortcut modes, Kodi video/UI toggle, fullscreen/windowed toggle, and stop/info/subtitle/power-menu shortcuts, a default navigation ring with repeatable directional zones and a mini-trackpad center that sends Enter in Kodi mode, optional legacy D-pad with OK, and an Fn panel with Windows window actions, browser tab/page shortcuts, and host-approved application buttons. **Minimize** directly minimizes the focused top-level window even when it is maximized. Tap **Switch app** to return to the previous app, or hold it until the visual Windows switcher appears, slide left or right, and release to open the selected app. **Task view** opens the persistent Windows window overview. Taller portrait phones retain a compact navigation ring below the Fn helpers while volume remains hidden; shorter portrait phones use the helpers-only view. In phone landscape, the Fn panel replaces the media column and keeps volume and navigation available beside it.
- **Dictate** sends browser speech recognition text to Windows when the browser supports speech recognition for the current origin.
- **Send text to PC** opens in Keyboard mode for composing or pasting text, reviewing the safe host-configured destination, choosing whether to append Enter, and saving local snippets in a section that starts folded. Its plain editor keeps a compact, left-aligned **Keyboard/Touchpad** switch and **ABC/123** selector together along the top; only the inactive mode shows its text label. Saved snippets appear as compact bordered cards with their action buttons together below the name. Loading one briefly highlights the editor border; long-press a card, drag it up or down, and release to save a new order. Touchpad mode restores the trackpad grid, hides the draft and keyboard-only controls, provides normal pointer gestures, and replaces the send row with **Left** and **Right** buttons ordered by the trackpad's left-handed setting. **Send text** and **Send text + Enter** stay side by side in portrait and landscape. The top app-mode row is hidden on this page in landscape. Short viewports in either orientation progressively hide the destination warning, field label, and title explanation to preserve room for the controls. Focused remains the default; **Preferences > Text destination** can instead copy to the Windows clipboard or prepare a fresh item in a supported installed app. Paste-driven delivery verifies the intended non-elevated compose window before pasting; file drafts are written before their associated app opens.
- **Get text from PC** starts with an empty selectable field. Press **Get text from PC** to fetch the PC's current text clipboard into that field; Voltura Air does not use the phone/tablet browser clipboard. **Show snippets** opens the local snippet controls for loading or saving text, below the field in portrait and in a side panel in landscape. The host blocks this default-off action until **Preferences > Global permissions > Allow paired devices to read the PC clipboard** is enabled, unless the selected device's **Read PC clipboard** override allows it. Clipboard text longer than 4,096 characters is not transferred.
- **PC volume** controls appear on the trackpad screen when the host can read the default Windows output device state.

Mobile settings are stored locally per device and include trackpad behavior, keyboard controls, remote behavior, grouped visibility for extra window and browser helpers, configurable fourth mode, text-transfer clearing and snippets, app auto refresh, saved PCs, device name, theme, and home-screen app refresh/install actions. The fourth button can be Dictation, Send text, or Get text; Presentation is added while its alpha capability is enabled. A saved Presentation choice falls back to Dictation while the gate is off. The Presentation timer intentionally resets on reload and is not host-synchronized. Pointer speed uses the Windows host default unless that device has an override. **Custom pointer** size and color are configured in the Windows host; a paired device can turn the host-wide setting on or off. Choosing YouTube or Kodi in Remote settings closes settings and switches the mobile app to the Remote screen for that mode.

## Windows Host

Voltura Air runs one host process for the signed-in Windows user. Launching the app again brings the existing host window to the front instead of starting another server on a different port.

The tray icon menu can show Voltura Air, open Devices or Preferences, control Keep awake, open the product page, or exit the host. **Keep awake** offers the selected Windows power plan, 30-minute, 1-hour, 2-hour, expiration, and indefinite choices. **Until…** opens the full host configuration.

Windows host settings include:

- Start Voltura Air when signing in to Windows.
- Show or hide connection status notifications.
- Optionally show Voltura Air when the last device disconnects; this is off by default on a clean install.
- Host-enforced global and per-device permissions.
- Preferences sections start collapsed and only one opens at a time. When a lower section opens, the view scrolls only as far as needed to reveal its first control while keeping the section header visible. Power and session permissions are configured under **Preferences > Global permissions**. Lock PC and Blackout display are enabled by default. Screen saver is enabled and shown only when Windows has an enabled, configured `.scr` program. Turn off display, Sign out, Restart PC, and Shut down PC are disabled until explicitly enabled. Presentation control appears only while alpha features are enabled.
- **Preferences > Developer tools > Enable alpha features** is the reusable, default-off gate for incomplete experimental work. Enabling it exposes each available alpha feature and its permissions; disabling it removes those entry points and the host rejects direct alpha commands. The gate is cached and event-driven, and disabled alpha features start no feature-specific timers, subscriptions, native resources, background work, or network activity.
- **Preferences > Keep awake** configures Off, indefinite, interval, and date/time expiration modes plus **Keep screen on**. It uses a temporary Windows execution-state request without editing the selected power plan or requiring administrator rights. Manual sleep, lid close, and the Windows lock screen still take precedence. Exiting Voltura Air releases the request.
- **Preferences > Developer tools > Windows locking** reports whether `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableLockWorkstation` explicitly disables locking for the signed-in user. When the policy value is missing or zero, **Test Lock PC** calls `LockWorkStation` directly without writing the registry. When a DWORD value explicitly disables locking, **Enable Windows locking** confirms locally, writes value `0` through the 64-bit current-user registry view, broadcasts `WM_SETTINGCHANGE` for `Policy`, verifies the readback, and tests `LockWorkStation`. It does not require administrator rights or UAC and does not read or change automatic Windows sign-in settings.
- A paired device can inherit global power and Keep awake permissions or override each one from **Devices > Permissions**. Remote Keep awake control is off by default. The mobile Power sheet provides one Keep awake toggle and always uses the host's Keep screen on setting.
- While alpha features are enabled, a paired device can likewise inherit, allow, or block **Presentation control** from **Devices > Permissions**. The host advertises the effective value only while the alpha gate is on and rejects direct Presentation commands when either the gate or permission is off.
- Sign out, restart, and shut down always require holding the mobile confirmation button for 1.6 seconds. Releasing or moving away cancels the action.
- Global permission for whether paired-device input may change the Voltura Air host UI and tray menu; when disabled, native minimize, maximize, and close controls still work, and Remote mode's **Minimize** button can minimize the focused Voltura Air window.
- Default pointer speed for paired devices, with optional per-device overrides.
- Default-off host-wide **Custom pointer**, with a 1-15 size slider and RGB colour. **Use cursor recovery watchdog** is on by default; turning it off marks the setting and its information button in red because an unexpected host termination can otherwise leave the custom cursor active.
- Default Remote mode for newly connected mobile clients.
- **Preferences > Application launch buttons** configures optional Browser, Spotify, VLC, and PowerPoint buttons plus custom `.exe` paths with optional arguments. Each enabled preset has a compact editable mobile label that saves automatically as it is changed; custom labels are entered in the command editor. Label inputs stop accepting text after 10 characters, and labels must contain at least one character, so the responsive Fn grid can always show them in full. Every custom add or edit requires a local warning confirmation. Paths and arguments remain on the PC, are validated again at launch time, and are never sent to the phone. The global/per-device **Allow paired devices to start applications** permission controls whether these buttons are advertised or executable.
- **Preferences > Text destination** is independent of application-launch permissions. It selects focused input, clipboard-only, or a managed Windows Notepad, Notepad++, Word, Visual Studio Code, Excel, default text-file app, default email client, or classic Outlook compose destination. The default text-file app opens a fresh UTF-8 `.txt` draft containing the sent text; choose **Open Windows default apps**, then **Choose defaults by file type** and select `.txt` to change that application. Notepad++ always opens a generated `.txt` draft through its documented file-path command line, including when it is already running, so delivery never depends on `Ctrl+N`. The default email client receives a `mailto:` draft with the sent text as its body; from the same Windows page, choose **Choose defaults by link type**, search for `MAILTO`, and select the mail app. This route is limited by the handler's URI length support, so an unsupported or oversized draft remains on the clipboard. Excel creates a new workbook and pastes when it is already running; when it is not running, Voltura Air creates a fresh `.xlsx` draft under `%LOCALAPPDATA%\Voltura Air\Text destination drafts`, with lines as worksheet rows and tabs as columns, then opens that exact workbook after foreground verification. Word uses the same deterministic startup path with a fresh `.docx` containing the sent paragraphs. Every generated `.txt`, `.xlsx`, and `.docx` draft starts with its path, auto-generated status, and retention notice. **Keep generated draft files** is off by default: Voltura Air removes its `Untitled-*` drafts after 24 hours when they are not in use, and the draft gives its exact removal date plus this setting's location. Enable the setting to retain new generated drafts until you remove them; **Open generated drafts folder** opens the local folder for inspection, saving, or removal. Classic Outlook uses its local object model to create and activate a message with the sent text in the message body; unsupported variants fall back to the clipboard. Save As moves either draft to a chosen location. The host finds known app locations/App Paths, lets the local user explicitly approve an absolute `.exe` override for executable-based destinations, validates that path immediately before use, and never exposes it to paired devices or ordinary logs.
- Connection settings for host networking.
- System, light, and dark appearance modes.
- Optional **Write application log**, off by default. When enabled, sanitized daily JSON Lines files under `%APPDATA%\Voltura Air\Logs` record remote command flow and local host actions through a bounded background writer; movement and pointer coordinates, typed text, and pairing credentials are excluded. Accepted entries flush during clean shutdown. If diagnostic activity exceeds the bounded queue, the log records a sanitized dropped-entry count instead of delaying remote input. Retention defaults to 2 days and can be set to 1, 2, 7, 14, or 30 days.
- **Diagnostics** opens directly on **Application log**, with **System details** available from the switch at the top. Logging can be enabled or disabled directly in this view; when disabled, the status and empty state explicitly say that no new activity is being written. The log displays themed activity cards and filters with a two-month date-range picker plus event, source, action, and client ID controls. The filtered view can be copied, the folder can be opened, and all log files can be deleted after confirmation. **Automatic log refresh** is off by default and, when selected, reacts to successful writes while the current Diagnostics view is visible.
- **Turn off display** intentionally cuts video output, including HDMI to a TV or home-theater receiver. It requires confirmation because some PCs interpret the Windows monitor-power command as sleep or Modern Standby. On those PCs the host and network connection suspend, Voltura Air reports the PC unavailable, and remote trackpad or keyboard input cannot wake it; use a physical keyboard or mouse. Windows may require PIN, fingerprint, or another configured sign-in method after resuming. This is a locked session, not a sign-out.
- **Blackout display** covers every monitor with black while leaving display power, Windows, the host, and networking active. Any local mouse, keyboard, touch, or pen input closes it. Any later remote pointer or keyboard command closes it before normal input dispatch.
- **Turn on screen saver** uses the native Windows screen-saver command. Voltura Air shows it only when Windows reports the feature enabled and the configured screen-saver program exists.

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
