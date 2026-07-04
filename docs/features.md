# Voltura Air - Features

Updated: 2026-07-05  
Scope: current `voltura/voltura-air` `main` branch. This file describes implemented product capabilities, not future roadmap promises.

Voltura Air turns a phone, tablet, or modern browser into a local-network remote control surface for a Windows PC.

## Product promise

- Control your Windows PC from any phone, tablet, or modern browser.
- No mobile app-store install required.
- Local-first. No account. No cloud needed. No paywall.

## Best for

- PC connected to TV/stereo.
- Sofa/bed control.
- Broken or annoying wireless keyboard/trackpad replacement.
- Presentations where basic keyboard/trackpad control is enough.
- Quick typing/search from phone.
- Local-network control without a cloud account.

## Not intended for

- Remote support over the internet.
- Full remote-desktop replacement.
- Phone notification sync.
- File backup/sync.
- Serious gaming-quality controller replacement.
- Cloud clipboard synchronization.

---

## Windows host

### App shell

- Runs as a Windows tray application.
- Uses a WPF host UI.
- Provides pages for:
  - Connect.
  - Devices.
  - Connection.
  - Preferences.
  - Diagnostics.
- Provides tray actions for opening the app, product page, and exit.
- Supports light, dark, and system theme modes.
- Supports per-user installation without administrator rights.
- Supports portable zip packaging.
- Supports NSIS installer packaging.

### Local hosting and connection

- Hosts the mobile web app on the local network.
- Accepts WebSocket connections on `/ws`.
- Uses QR pairing links for first-time setup.
- Supports saved reconnects after pairing.
- Supports manual host entry on mobile for recovery.
- Supports automatic network adapter selection.
- Supports manual network adapter selection.
- Supports automatic port selection from the preferred Voltura Air port.
- Supports manual port settings with validation.
- Shows network/port warnings when automatic choices may be stale or unreachable.

### Pairing and trust

- Requires every WebSocket session to start with `pair.hello`.
- Uses short-lived QR pairing tokens.
- Uses stored reconnect secrets after first pairing.
- Stores only a hash of the reconnect secret on the host.
- Rotates the secret when a valid token is accepted for an already-known client.
- Keeps a paired-device record instead of creating duplicate browser/home-screen entries for the same client ID.
- Tracks active connected devices.
- Supports revoking/disconnecting paired devices.
- Closes active sockets when a device is revoked.
- Applies pairing attempt rate limiting.
- Rejects unrelated public WebSocket origins before accepting the socket.
- Validates protocol messages before dispatching input.

### Device management

- Shows paired devices.
- Shows active connection count/state.
- Stores device name.
- Stores platform/browser/display-mode metadata.
- Stores added/last connected/last disconnected/last renamed timestamps.
- Supports device rename.
- Supports disconnect/remove.
- Supports duplicate cleanup.
- Supports per-device permission overrides.

### Permissions and capabilities

- Reports host capabilities to the mobile client.
- Supports host-enforced permission for PC sleep.
- Supports host-enforced permission for volume control.
- Combines global defaults with per-device overrides.
- Hides or disables unsupported actions on mobile through capability reporting.
- Ignores unauthorized sleep/volume commands even if a client sends them manually.

### Input injection

- Moves the Windows pointer.
- Sends left/right mouse button down/up/click.
- Sends vertical and horizontal wheel scroll.
- Sends pinch zoom as Ctrl + mouse wheel.
- Sends Unicode text input.
- Sends special keys and modifier shortcuts.
- Supports common virtual keys:
  - Backspace.
  - Tab.
  - Enter.
  - Escape.
  - Arrow keys.
  - Delete.
  - Home.
  - End.
  - Page Up.
  - Page Down.
  - Space.
  - Win/Windows.
  - F1-F12.
- Translates shortcut aliases:
  - Undo -> Ctrl+Z.
  - Redo -> Ctrl+Y.

### Audio and system actions

- Reads default Windows output device volume/mute state.
- Sends current audio state after pairing.
- Sends current audio state after heartbeat/status pings.
- Sets default output device volume.
- Toggles mute.
- Supports PC sleep when allowed by host permissions.

---

## Mobile web client

### App model

- Runs as a React/TypeScript PWA.
- Works in modern mobile and desktop browsers.
- Can be installed to a phone/tablet home screen where the browser supports it.
- Stores local device identity.
- Stores local device name.
- Stores saved PC profiles.
- Stores per-saved-PC/client trackpad preferences.
- Stores keyboard preferences.
- Stores theme preference.
- Supports light, dark, and system theme modes.
- Provides app refresh/cache reset flow for installed PWA cases.

### Pairing and reconnect UX

- Opens from a QR pairing link.
- Can take a photo of a QR code for pairing/re-pairing.
- Can confirm/change device name before pairing.
- Can reconnect to saved PCs.
- Can manually enter:
  - full origin,
  - address plus port,
  - full Voltura Air pairing link,
  - port number resolved against the current host.
- Shows explicit pairing states:
  - needs pairing,
  - connecting,
  - paired,
  - rejected,
  - unavailable,
  - disconnected.
- Maps pairing failures to user-friendly recovery messages.
- Handles unreadable QR codes.
- Handles non-Voltura QR codes.
- Handles expired/stale/invalid tokens.
- Handles revoked stored credentials.
- Handles host unavailable/network failures.
- Provides recovery actions:
  - take photo of new QR code,
  - reconnect,
  - enter host manually,
  - copy diagnostics.
- Copies redacted diagnostics without pairing secrets or full tokens.

### Trackpad mode

- One-finger pointer movement.
- Tap-to-click.
- Long press/right click.
- Two-finger tap/right click.
- Physical left/right click buttons.
- Hold left/right button while moving pointer for drag/resize.
- Two-finger vertical scroll.
- Two-finger horizontal scroll.
- Optional pinch zoom.
- Pointer speed setting.
- Optional pointer smoothing.
- Optional pointer acceleration.
- Optional scroll acceleration.
- Natural/traditional scroll direction setting.
- Optional haptic feedback where browser vibration support allows it.
- Optional left-handed button layout.
- Optional large click buttons.
- Optional volume/mute control.
- Expanded full-screen trackpad mode.
- Browser page scrolling is suppressed on the trackpad surface.
- Accidental text/image selection is suppressed on the trackpad surface.
- Optional gesture debug screen when the host enables it.

### Keyboard mode

- Live typing mode.
- Buffered send mode.
- Send button is hidden when Live typing is enabled.
- Text/numeric mobile keyboard toggle.
- Composition/IME handling foundation.
- Repeatable Backspace.
- Repeatable Enter.
- Repeatable Tab.
- Esc.
- Win.
- Sleep button when host capability and local setting allow it.
- Space button.
- Optional F1-F12 row.
- Optional arrow pad.
- Optional control/shortcut row.
- Current visible shortcuts:
  - Ctrl+A.
  - Ctrl+C.
  - Ctrl+V.
  - Ctrl+Z.
  - Ctrl+Y.
- Keyboard settings:
  - show function keys,
  - show control keys,
  - show arrow keys,
  - show sleep button,
  - enable split mode.

### Dictation mode

- Uses browser speech recognition API when supported by the browser and origin.
- Captures final speech recognition text.
- Sends dictated text to Windows through the same text input path.
- Allows dictation text to be edited/cleared on mobile.

### Split mode

- Enables a side-by-side keyboard + trackpad layout.
- Intended for tablet/landscape use.
- Activates in landscape at wider viewport sizes.
- Can be enabled from Trackpad settings.
- Can be enabled from Keyboard settings.
- Hides app chrome/mode tabs/status row in split mode to maximize usable space.
- Hides volume control in split mode.
- Keeps the keyboard pane scrollable while keeping the trackpad pane fixed.

---

## Current documentation

- `README.md` - product promise, best-for/not-intended sections, requirements, build/test, release links.
- `docs/features.md` - high-level current feature map.
- `docs/protocol.md` - WebSocket protocol and message examples.
- `docs/pairing-feedback.md` - pairing failure UX and diagnostics model.
- `docs/manual-network-selection.md` - network, port, and manual host behavior.
- `docs/release.md` - release packaging and asset upload flow.
- `CONTRIBUTING.md` - contribution quickstart.
- `SECURITY.md` - security reporting policy.
- `CODE_OF_CONDUCT.md` - project conduct policy.
- `LICENSE` - MIT license.
- `.github/FUNDING.yml` - Ko-fi and PayPal support links.

---

## Current release/distribution state

- Freeware.
- Open source under MIT license.
- GitHub releases are the expected distribution channel.
- Release packaging creates:
  - portable zip,
  - NSIS setup executable.
- Installer is per-user.
- Installer does not require administrator rights.
- Installer keeps pairing/settings data on uninstall.
- Release assets are currently not code-signed.
- Documentation explicitly warns not to claim the installer is signed.

---

## Not implemented yet / do not promise as current features

These are roadmap candidates, not current product capabilities.

- Dedicated Couch Remote mode.
- Dedicated Presentation mode.
- Dedicated Paste Text to PC / clipboard-transfer screen.
- PC-to-phone clipboard read/sync.
- Open URL on PC.
- Custom shortcut panels.
- Host-approved custom app launch buttons.
- Preferred mode per device.
- Host-managed pointer/layout profiles per paired device.
- Wake-on-LAN.
- Screen viewer / screen preview / remote desktop.
- File transfer.
- Gyroscope/air mouse.
- Gamepad mode.
- Native iOS/Android apps.
- Auto-update.
- Signed installer.
- Microsoft Store distribution.
- Website demo GIF/video.
- Full public FAQ/privacy page if not maintained outside this repository.

---

## Strongest current selling points

- Browser-based phone/tablet control: no mobile app-store install.
- Local-first: no account, no cloud relay, no paywall.
- Practical couch-PC use: trackpad, keyboard, dictation, volume, and sleep.
- QR pairing with saved reconnect.
- Recovery UX for stale QR codes, changed IP/port, and unreachable host.
- Stronger trust baseline than a quick LAN remote: pairing tokens, hashed reconnect secret, origin checks, message validation, permissions, and redacted diagnostics.
- Split keyboard + trackpad mode for landscape/tablet use.
