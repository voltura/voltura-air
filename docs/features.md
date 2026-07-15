# Voltura Air - Features

Updated: 2026-07-13
Scope: current `voltura/voltura-air` `main` branch. This file describes current product capabilities.

Voltura Air turns any phone, tablet, or modern browser into a local-network remote control surface for a Windows PC.

## Product promise

- Control your Windows PC from any phone, tablet, or touch browser.
- No mobile app-store install required.
- Works on your own Wi-Fi/LAN. No account. No cloud needed.
- Freeware with no trial limits or feature locks.

## Best for

- PC connected to TV/stereo.
- YouTube, Kodi, and other couch/TV control.
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
- Allows one host process per signed-in Windows user; another launch shows and focuses the existing host window instead of starting a second server.
- Uses a WPF host UI.
- Provides pages for:
  - Connect.
  - Devices.
  - Connection.
  - Preferences.
  - Diagnostics.
- Organizes Preferences as themed accordion sections that are collapsed on entry and allow only one expanded section at a time.
- Provides tray actions for opening the app, controlling Keep awake, opening the product page, and exit.
- Keeps the host window hidden when the last paired device disconnects by default; reopening it on disconnect is an opt-in preference.
- Supports light, dark, and system theme modes.
- Prevents accidental selection of static app text and button labels during touch gestures while preserving normal selection in inputs, textareas, selects, content-editable fields, and explicitly copyable text surfaces.
- Supports per-user installation without administrator rights.
- Supports portable zip packaging.
- Supports NSIS installer packaging.

### Local hosting and connection

- Hosts the mobile web app on the local network.
- Accepts WebSocket connections on `/ws`.
- Bounds WebSocket resource use with a 64-session limit, a 10-second initial
  pairing deadline, a 2-minute authenticated inactivity deadline, and a 64 KiB
  text-message limit across fragments.
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
- Applies pairing attempt rate limiting with bounded, expiring per-address state.
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
- Supports a host default pointer speed with per-device overrides.
- Supports a default-off pointer highlight with independent enabled/off overrides per paired device.

### Permissions and capabilities

- Reports host capabilities to the mobile client.
- Reports the host default Remote mode to the mobile client.
- Supports host-enforced permission for PC sleep.
- Supports host-enforced permission for volume control.
- Supports host-enforced permission for fixed Remote launch actions and host-configured application buttons.
- Configures optional Browser, Spotify, VLC, and PowerPoint launch presets in Windows Preferences.
- Lets the host choose a 1–10 character mobile button label for every enabled preset and custom application command; preset label edits save automatically and inputs stop accepting text at 10 characters.
- Configures custom `.exe` launch buttons with optional arguments after a local host warning confirmation on every add or edit.
- Keeps custom paths and arguments on the PC; paired devices receive and send only opaque action IDs and display labels.
- Revalidates custom paths before every launch and reports start, permission, stale-button, invalid-target, not-found, and launch failures to mobile.
- Supports separate host-enforced permissions for Lock PC, Blackout display, Turn off display, Screen saver, Sign out, Restart PC, and Shut down PC.
- Supports a default-off global Keep awake control permission with per-device overrides.
- Detects an explicit current-user Windows workstation-lock block and reports it separately from the Lock PC permission; a missing value is not treated as proof that locking works.
- Lets the signed-in user explicitly enable and test Windows locking locally without elevation or UAC, then broadcasts a Windows policy refresh.
- Supports a global permission for client-injected input to interact with the Voltura Air host UI and tray menu; when disabled, clients can still control the PC while host minimize, maximize, and close controls remain available. The Remote mode **Minimize** button may also minimize the focused Voltura Air window without granting broader host-UI control.
- Combines global defaults with per-device overrides.
- Hides or disables unsupported actions on mobile through capability reporting.
- Ignores unauthorized sleep, volume, launch, and power/session commands even if a client sends them manually.

### Input injection

- Acknowledges dispatched input events when the host advertises input acknowledgement support.
- Coalesces active movement to animation frames and uses low-rate acknowledgement barriers plus WebSocket-buffer limits to prevent delayed pointer queues without adding per-move replies or idle polling.
- Reports input dispatch failures to the mobile client instead of silently leaving dead controls.
- Moves the Windows pointer.
- Optionally overlays a larger custom pointer with a teal glow during client-initiated movement, clicks, two-finger scrolling, and pinch zoom. It yields to the normal Windows cursor over taskbars and while a higher-integrity application is foreground, using one foreground-change event hook rather than polling. A completed remote taskbar click also schedules one short, coalesced foreground recheck for shell activation. A blocked episode shows one concise PC notification and a mobile recovery dialog with **Show desktop** and **Continue**; Continue returns to the client controls while a compact toast remains available to reopen recovery until normal foreground control returns, and Show desktop minimizes desktop windows through the Windows shell. Startup performs lightweight cursor recovery and launches a single minimal watchdog that blocks without polling until the host exits, restores the configured Windows cursor scheme, and exits. If the watchdog exits unexpectedly, the host restores cursors immediately, hides and disables the overlay for the rest of that session, and does not restart the watchdog. If the watchdog cannot start, system cursors are left unchanged. The dedicated overlay thread and hidden window are created only on first use. The host temporarily makes standard system cursors transparent and clears the active application-defined cursor shape, restores the user's configured cursor scheme and previous cursor on idle/shutdown, aligns the replacement hotspot at the active display DPI, and never routes high-rate input through the main WPF UI thread.
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
  - Single-letter shortcuts such as F.
  - F1-F12.
  - Browser Back.
  - Media previous/play-pause/next/stop.
  - Volume mute/down/up.
- Translates shortcut aliases:
  - Undo -> Ctrl+Z.
  - Redo -> Ctrl+Y.

### Audio and system actions

- Reads default Windows output device volume/mute state.
- Sends current audio state after explicit audio requests and accepted audio commands.
- Sets default output device volume.
- Toggles mute.
- Supports PC sleep when allowed by host permissions.
- Locks the current Windows session with `LockWorkStation` when permission and the current-user policy allow it.
- Reports accepted, denied, unsupported, policy-disabled, policy-unavailable, and failed power/session requests without disconnecting the client.
- Offers opt-in daily JSON Lines application logging for troubleshooting. Logging is off by default, keeps files normally shareable, and allows reads without holding the protocol writer lock. It records remote command flow plus local host actions such as pointer-highlight default/override changes, Windows-lock policy writes, readback failures, and native lock tests without typed text, pointer coordinates, or pairing credentials.
- Opens Diagnostics directly on a dedicated Application log view, with System details available from a clear top-level switch. The log view exposes its **Write application log** toggle directly and clearly distinguishes disabled logging from an empty filtered result. It uses themed activity cards, a two-month date-range picker plus event, source, action, and client filters, filtered copy, open-folder, confirmed delete actions, and an optional session-only **Automatic log refresh** toggle that is off by default. Log reads run off the WPF dispatcher, repeated requests are coalesced, and unchanged results retain their existing visuals. Automatic refresh reacts to successful host log writes only while the view is visible instead of polling a timer. Retention is configurable from 1 to 30 days with a 2-day default. Input commands record both receipt and their sanitized executed or blocked outcome, including the Remote mode minimize action, without logging typed text. Only the record area scrolls, so the filter and action controls remain reachable.
- Blacks out every connected monitor with a topmost black WPF curtain without changing display power state. Windows, networking, and remote control remain active; any local or remote mouse/keyboard interaction removes the curtain, and touch or pen input also dismisses it locally. Ordinary remote movement bypasses the WPF dispatcher when no curtain is active.
- Starts the native Windows screen saver only when Windows reports screen saving enabled and a configured `.scr` program exists. The action is omitted from host and mobile UI when unavailable.
- Turns off connected displays through the Windows monitor-power command when allowed, including HDMI output to TVs and receivers. The mobile client requires confirmation and explains that some PCs treat this command as sleep or Modern Standby. On those systems the host and network connection can suspend, Voltura Air cannot wake the PC remotely, and physical keyboard or mouse input is required. Windows may then require PIN, fingerprint, or another configured sign-in method; the action does not sign out the user.
- Signs out, restarts, or shuts down through fixed Windows system commands when explicitly allowed.
- Keeps Windows awake without changing the selected power plan, with Off, timed, date/time expiration, and indefinite modes plus an optional host-owned Keep screen on setting. Timed deadlines survive host restarts, expired modes return to Off, and exit releases the Windows request.

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
- Stores per-saved-PC/client remote preferences.
- Stores per-saved-PC/client app preferences.
- Stores the configurable fourth mode and clear-after-send preference locally.
- Stores saved text snippets locally on the current browser profile.
- Stores theme preference.
- Supports light, dark, and system theme modes.
- Provides app refresh/cache reset flow for installed PWA cases.
- Uses versioned QR links and service-worker caches so fresh host QR codes
  refresh stale mobile app shells.
- Can automatically refresh the installed web app once after reconnecting to a PC.
- Host developer mode makes auto refresh track the current host run instead of the release version.

### Pairing and reconnect UX

- Detects stale health checks or missing input acknowledgements and moves to unavailable/retrying.
- Uses faster health checks during active input, slower checks while idle, and closes the mobile WebSocket while backgrounded.
- Treats host `connected: false` status as a real unavailable state.
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
- Pointer speed uses the Windows host default unless the paired device has an override; changing it on the phone updates that device override on the host.
- Optional pointer smoothing.
- Optional pointer acceleration.
- Optional **Highlight pointer**, using the host global default unless this paired device explicitly enables or disables it.
- Optional scroll acceleration.
- Natural/traditional scroll direction setting.
- Optional haptic feedback on trackpad taps and click-button presses, with an immediate preview when enabled and a clear unsupported state when the browser cannot vibrate.
- Optional left-handed button layout.
- Optional large click buttons.
- Optional volume/mute control.
- Expanded full-screen trackpad mode.
- Browser page scrolling is suppressed on the trackpad surface.
- Accidental text/image selection is suppressed on the trackpad surface.
- Optional gesture debug screen when the host enables it.

### Keyboard mode

- Live typing mode.
- Uses the shorter `Back` label for the Backspace key in split or width-constrained layouts while preserving the Backspace action.
- Buffered send mode preserves multiline line breaks through the same newline-aware host input path.
- Optional **Paste to PC** native-paste target, disabled by default, that uses the acknowledged text-transfer path.
- Send button is hidden when Live typing is enabled.
- Text/numeric mobile keyboard toggle.
- Composition/IME handling foundation.
- Single-key app shortcuts such as `F` are sent as virtual key presses in live typing.
- Repeatable Backspace.
- Repeatable Delete.
- Repeatable Enter.
- Repeatable Tab.
- Repeatable Home and End.
- Repeatable Page Up and Page Down.
- Repeatable arrow keys.
- Esc.
- Win.
- Sleep button when host capability and local setting allow it.
- Space button.
- Optional F1-F12 row.
- Optional arrow pad.
- Optional control/shortcut row.
- Current visible shortcuts:
  - Ctrl+A.
  - Ctrl+X.
  - Ctrl+C.
  - Ctrl+V.
  - Ctrl+Z.
  - Ctrl+Y.
  - Alt+Tab.
  - Shift+Alt+Tab.
- Keyboard settings:
  - show function keys,
  - show control keys,
  - show arrow keys,
  - show sleep button.
  - show Paste to PC button.

### Remote mode

- A single compact Power entry opens a responsive Power & session sheet instead of adding permanent power buttons to the main grid.
- The Power & session sheet shows one basic Keep awake toggle when the host reports Awake state. It uses the host's Keep screen on setting and cannot alter it.
- Lock PC, Blackout display, and an available Screen saver use direct action rows. Blackout closes the Power sheet immediately so it does not obscure the restored display; its result remains available when Power is reopened. Lock and Screen saver remain open while awaiting the host. Turn off display, Sign out, Restart PC, and Shut down PC open a dedicated warning screen and require an uninterrupted 1.6-second hold.
- Host-disabled actions remain visible with a host-disabled explanation, except Screen saver, which is omitted when Windows does not expose an enabled, configured saver.
- Media previous/play-pause/next.
- Repeatable seek backward/forward through arrow-key shortcuts.
- Space.
- Esc/Back.
- Video/app fullscreen through `F`.
- Browser fullscreen through `F11`.
- Repeatable Windows volume down/up keys and mute key.
- Standard mode uses Windows media, volume, fullscreen, and navigation keys.
- YouTube mode maps remote controls to browser player shortcuts:
  - previous/next video through Shift+P/Shift+N,
  - play/pause through `K`,
  - seek through `J`/`L`,
  - volume through Arrow Down/Arrow Up,
  - mute through `M`.
- Kodi mode maps remote controls to common Kodi keyboard shortcuts:
  - previous/next item or chapter through Windows previous/next media keys,
  - play/pause through `Space`,
  - seek/navigation through arrow keys,
  - stop playback through `X`,
  - info through `I`,
  - subtitles through `T`,
  - power menu through `S`,
  - back through `Backspace`,
  - toggle between the Kodi UI over the playing video and video-only playback through `Tab`,
  - toggle Kodi between fullscreen and windowed mode through `\`,
  - volume through `-`/`+`,
  - mute through `F8`.
- Default navigation ring:
  - repeatable up/left/right/down ring zones,
  - center mini-trackpad for pointer movement,
  - center single tap for left click in Standard and YouTube modes,
  - center single tap for Enter in Kodi mode,
  - center double tap for right click in Standard and YouTube modes,
  - Kodi Subtitles, Fullscreen/Windowed, and Info icon buttons around the navigation panel,
  - navigation panel background drag for pointer movement,
  - navigation panel background single tap for left click,
  - navigation panel background double tap for right click.
- Optional legacy D-pad with OK.
- Start.
- Switch app: tap for an ordinary Alt+Tab, or hold, slide left/right through the visual Windows switcher, and release to open the selected app.
- Task view through Win+Tab.
- Show desktop, close the focused window, and minimize the focused top-level window directly, including when it is maximized.
- Browser Back, new tab, close tab, reopen closed tab, next/previous tab, and reload.
- Host-approved application buttons in a responsive Fn grid, with complete non-ellipsized labels, pending feedback, and PC result feedback that clears after four seconds.
- Application launch can inherit the global host permission or be explicitly allowed/blocked per paired device.
- Compact phone layouts keep the main remote surface within the viewport and move Windows and browser helper controls behind an Fn switch. Taller portrait phones retain a compact navigation ring below the Fn helpers while volume remains hidden; shorter portrait phones use the helpers-only view. In phone landscape, helpers replace the media column while volume and navigation remain visible.
- Browser tabs use the dynamic viewport height, while installed/standalone PWAs use the large viewport height to avoid stale browser-chrome sizing reducing the app surface.
- Portrait-tablet layouts keep the mode row at its natural button height so the trackpad or active mode retains the remaining space.
- Tapping the active mode hides the full mode row and exposes the compact header selector without reducing the active mode's usable area.
- Remote settings:
  - navigation ring.
  - Remote mode: Standard, YouTube, or Kodi.
  - grouped visibility toggles for extra window actions and extra browser tab/page actions; Start, Switch app, Task view, and Browser Back remain available as essential helpers.
  - optional client-local launch toggles for Open YouTube and Start Kodi when the host allows paired devices to start applications.
  - selecting YouTube or Kodi from settings closes settings and opens the Remote screen for that mode.

### Dictation mode

- Uses browser speech recognition API when supported by the browser and origin.
- Captures final speech recognition text.
- Sends dictated text to Windows through the same text input path.
- Allows dictation text to be edited/cleared on mobile.

### Menu and text transfer

- The hamburger drawer is a **Menu** with separate **Tools** and **Settings** groups.
- Dictation and **Send text to PC** can be opened directly from Menu without changing the fourth-mode preference.
- Trackpad, Keyboard, and Remote remain fixed primary modes. The fourth mode can be configured as Dictation or Send text to PC, defaults to Dictation, and uses one shared label/icon definition across navigation surfaces.
- Dictation and Send text use the persistent Menu and mode navigation without separate page-level Back controls.
- **Send text to PC** composes or pastes up to 4,096 characters. Focused application input remains the default; the host can instead use clipboard-only delivery, a configured fresh Notepad, Notepad++, Word, Visual Studio Code, Excel, or classic Outlook compose item, a new `.txt` draft in the Windows default text-file app, or a `mailto:` draft in the Windows default email client.
- The editor opens in **Keyboard** mode with a plain input background. Its left-aligned Keyboard/Touchpad switch shares one compact toolbar with a right-aligned ABC/123 selector; the active mode collapses to its icon without reserving label space while the inactive alternative keeps its text label. Touchpad mode uses the trackpad grid, hides the draft and keyboard-only send, clear, and snippet controls, and replaces the send row with Left/Right buttons that follow the trackpad's left-handed button-layout setting.
- The top app-mode button row is hidden on the Send text page in landscape to leave more room for the editor. As viewport height becomes constrained in either orientation, the page progressively hides the destination warning, the **Text to send** field label, and finally the explanatory line below the page title.
- The destination warning is shown before delivery. Changing Windows focus changes the destination; the host refuses delivery while its own protected UI has focus.
- Preserves multiline text by delivering each LF, CRLF, or CR line break as one Enter key event.
- Keeps **Send text** and **Send text + Enter** side by side in both portrait and landscape. The optional final Enter is sent only after Windows accepts the complete text.
- Confirms before sending 2,000 or more characters.
- Shows pending, success, timeout, and native-delivery failure feedback from a host-acknowledged operation.
- Supports an optional clear-after-send preference. The draft remains available after failure or when clearing is disabled.
- Supports up to 20 locally stored snippets of up to 4,096 characters in a section that is folded by default. Names must be unique after trimming and without regard to letter case; duplicate saves and renames show inline feedback. Each snippet uses a compact bordered card with its name and a reorder-grip glyph on the first row and Rename, Update, and Delete together below it. Cards declare their custom touch behavior before contact begins: a normal vertical swipe scrolls the list under app control, while holding for 450 ms switches the same whole-card gesture into scroll-locked reordering. Releasing a drag automatically saves the new order. Loading a snippet briefly highlights the editor border and announces that its text was copied. Snippets are sent only after an explicit send action, and one whose text exactly matches the current draft remains visually neutral.
- The authenticated host status advertises only the safe destination mode, display name, and current availability. Executable paths, process IDs, window handles, matching rules, and clipboard contents are not exposed.
- Managed text transfer either creates a self-identifying local draft or stages text on the Windows clipboard. Paste-driven destinations paste only after the host has confirmed the intended new-item window is foreground and not elevated. Startup/activation uncertainty is a clipboard-only success for manual paste, never a blind retry or paste into another foreground window. Notepad++ opens a generated text draft directly rather than relying on a new-document shortcut. Generated Notepad++, Word, Excel, and text-file drafts are automatically removed after 24 hours by default. Their notice gives the exact removal date and points to **Preferences > Text destination > Keep generated draft files**, which can be enabled to retain new drafts; use Save As with a different filename to preserve important content.

### Split mode

- Enables a side-by-side keyboard + trackpad layout.
- Intended for tablet/landscape use.
- Activates in landscape at wider viewport sizes.
- Has a dedicated Split mode settings category for enabling and configuring the layout.
- Lets the user place the trackpad on the left or right.
- Lets the user independently show or hide the mode buttons and connection status row.
- Hides mode buttons and the status row by default to maximize usable space.
- Hides volume control in split mode.
- Keeps the keyboard pane scrollable while keeping the trackpad pane fixed.

---

## Current documentation

- `README.md` - product promise, best-for/not-intended sections, requirements, build/test, release links.
- `docs/features.md` - detailed current-state capability inventory and explicit list of features that must not be promised yet.
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

## Strongest current selling points

- Browser-based phone/tablet/touch-browser control: no mobile app-store install.
- Local Wi-Fi/LAN control: no account, no cloud relay, no paywall.
- Practical couch-PC use: trackpad, keyboard, dictation, YouTube/Kodi remote modes, volume, and power/session controls.
- Reviewed, acknowledged text transfer to the focused PC application with optional local snippets and Enter.
- Host-managed Keep awake modes with an optional Keep screen on setting and a simple permissioned mobile toggle.
- QR pairing with saved reconnect.
- Recovery UX for stale QR codes, changed IP/port, and unreachable host.
- Stronger trust baseline than a quick LAN remote: pairing tokens, hashed reconnect secret, origin checks, message validation, permissions, and redacted diagnostics.
- Split keyboard + trackpad mode for landscape/tablet use.
