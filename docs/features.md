# Implemented product capabilities

This document defines observable product capabilities and guarantees. See
[protocol.md](protocol.md) for wire schemas, [setup.md](setup.md) for procedures,
[architecture.md](architecture.md) for structure, and [todo.md](todo.md) for
approved unfinished work.

## Product scope and guarantees

- The host runs on Windows 11 and is controlled from a phone, tablet, or other
  browser-capable device on the same Wi-Fi or LAN.
- The mobile client is served as a web app by the host; normal use does not
  require a mobile app-store installation.
- Voltura Air has no user account, subscription, trial limit, feature lock,
  cloud relay, or internet input-forwarding service.
- It is a local PC control surface, not a remote-desktop, file-sync, backup,
  phone-notification-sync, or cloud-clipboard product.
- A client cannot control or wake the PC while the Windows host is sleeping,
  shut down, or otherwise unreachable.

## Windows host

### App shell

- Runs as a Windows tray application.
- Allows one host process per signed-in Windows user; another launch focuses the existing host window.
- Uses a WPF host UI.
- Provides pages for:
  - Connect.
  - Devices.
  - Connection.
  - Preferences.
  - Diagnostics.
- Provides tray actions for opening the app, controlling Keep awake, opening the product page, and exit.
- Starts with a neutral tray badge while paired devices reconnect, and holds the connected badge through the short automatic-reconnect grace period; it shows the disconnected badge only when a device remains offline.
- Keeps the host window hidden when the last paired device disconnects by default; reopening it on disconnect is an opt-in preference.
- Supports light, dark, and system theme modes.
- Supports per-user installation without administrator rights.
- Supports portable zip packaging.
- Supports NSIS installer packaging.

### Local hosting and connection

- Hosts the mobile web app on the local network.
- Accepts WebSocket connections on `/ws`.
- Bounds WebSocket resource use with a 64-session limit, a 10-second initial
  pairing deadline, a 2-minute authenticated inactivity deadline, and a 64 KiB
  text-message limit across fragments.
- Serializes sends per authenticated socket, bounds send and close operations,
  and coalesces repeated host-status updates through one owned worker.
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
- Keeps one paired-device record for each client ID.
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
- Supports a default-off host-wide Custom pointer with a colour and size selected in Windows Preferences and on/off control from paired devices. Its separate cursor recovery watchdog is on by default and can be disabled only with a visible warning.

### Permissions and capabilities

- Reports host capabilities to the mobile client.
- Reports the host default Remote mode to the mobile client.
- Supports host-enforced permission for PC sleep.
- Supports host-enforced permission for volume control.
- Supports the reusable, default-off **Enable alpha features** host gate. An alpha feature advertises its capability only while enabled and is also rejected at its production command boundary while disabled.
- Supports an effective global/per-device Presentation control permission while Presentation's alpha gate is enabled.
- Supports host-enforced permission for fixed Remote launch actions and host-configured application buttons.
- Supports a separate default-off host permission for opening reviewed HTTP and HTTPS web addresses, with per-device overrides.
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
- Sends Unicode text to Windows in bounded batches of up to 64 code units without splitting surrogate pairs. Partial native batches report failure, and an unmatched accepted Unicode key-down is released before the failure reaches the client.
- Reports input dispatch failures to the mobile client.
- Moves the Windows pointer.
- Applies the optional host-wide Custom pointer across Windows. Its size and color are chosen in host Preferences, and paired devices can turn it on or off. The default-on cursor recovery watchdog restores the configured Windows cursor scheme after unexpected host termination, including forced development-host process-tree shutdown; normal shutdown performs the same restoration in the host.
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
- Offers opt-in daily JSON Lines application logging for troubleshooting. Logging
  is off by default and records sanitized remote-command and local-host outcomes
  without typed text, opened web addresses, pointer coordinates, or pairing
  credentials. Retention is configurable from 1 to 30 days with a 2-day default.
- Opens Diagnostics directly on an Application log view, with System details
  available from a top-level switch. The log view distinguishes disabled logging
  from an empty result and provides date, event, source, action, and client
  filters plus copy, open-folder, confirmed delete, and optional session-only
  automatic refresh.
- Blacks out every connected monitor with a topmost black WPF curtain without
  changing display power state. Windows, networking, and remote control remain
  active; local or remote mouse/keyboard input removes the curtain, as does local
  touch or pen input.
- Starts the native Windows screen saver only when Windows reports screen saving enabled and a configured `.scr` program exists. The action is omitted from host and mobile UI when unavailable.
- Turns off connected displays through the Windows monitor-power command when allowed, including HDMI output to TVs and receivers. The mobile client requires confirmation and explains that some PCs treat this command as sleep or Modern Standby. On those systems the host and network connection can suspend, Voltura Air cannot wake the PC remotely, and physical keyboard or mouse input is required. Windows may then require PIN, fingerprint, or another configured sign-in method; the action does not sign out the user.
- Signs out, restarts, or shuts down through fixed Windows system commands when explicitly allowed.
- Keeps Windows awake without changing the selected power plan, with Off, timed, date/time expiration, and indefinite modes plus an optional host-owned Keep screen on setting. Timed deadlines survive host restarts, expired modes return to Off, and exit releases the Windows request.

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
- Prevents accidental selection and touch callouts on static app text and
  control chrome while preserving normal selection in inputs, textareas,
  selects, content-editable fields, and explicitly copyable text surfaces.
- Provides app refresh/cache reset flow for installed PWA cases.
- Uses versioned QR links and service-worker caches so fresh host QR codes
  refresh stale mobile app shells.
- Can automatically refresh the installed web app once after reconnecting to a PC.

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
  - show sleep button, with mandatory confirmation before the command is sent.

### Remote mode

- A compact Power entry opens the responsive Power & session sheet.
- The Power & session sheet shows one basic Keep awake toggle when the host reports Awake state. It uses the host's Keep screen on setting and cannot alter it.
- Lock PC, Blackout display, and an available Screen saver are direct actions.
  Turn off display, Sign out, Restart PC, and Shut down PC require a 1.6-second
  confirmation hold.
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
- The Fn panel includes an **Open URL** dialog. Bare addresses default to HTTPS;
  explicit HTTP remains HTTP. The host accepts absolute HTTP/HTTPS URLs and opens
  them through the signed-in user's default browser.
- Invalid input and launch failures preserve the draft and offer Retry. Success
  confirms that Windows accepted the open request. Application logging records
  the outcome without the address.
- Host-approved application buttons in a responsive Fn grid, with complete non-ellipsized labels, pending feedback, and PC result feedback that clears after four seconds.
- Application launch can inherit the global host permission or be explicitly allowed/blocked per paired device.
- Compact phone layouts move Windows and browser helpers behind an Fn switch and
  keep the primary remote controls within the viewport.
- Tapping the active mode collapses the mode row into the header selector.
- Remote settings:
  - navigation ring.
  - Remote mode: Standard, YouTube, or Kodi.
  - grouped visibility toggles for extra window actions and extra browser tab/page actions; Start, Switch app, Task view, and Browser Back remain available as essential helpers.
  - optional client-local launch toggles for Open YouTube and Start Kodi when the host allows paired devices to start applications.
  - selecting YouTube or Kodi from settings closes settings and opens the Remote screen for that mode.

### Presentation mode (alpha, default off)

- Appears only after **Preferences > Developer tools > Enable alpha features** is enabled on the host. While the setting is off, the host advertises no Presentation capability, rejects direct Presentation commands without injecting input, and the mobile app hides Presentation from Menu and fourth-mode choices.
- Provides a dedicated high-contrast, large-target presenter surface that is then reachable from Menu and can occupy the configurable fourth mode button.
- Uses a user-selected PowerPoint, Google Slides, or PDF/browser profile and fixed
  shortcuts. It does not detect the focused application or current slide.
- Sends one acknowledged command at a time and clears pending work on disconnect.
- Uses Right/Left for Next/Previous and Escape for End. PowerPoint also offers F5
  Start and Ctrl+L laser pointer; Google Slides offers L laser pointer.
  **Blackout** uses the separate system-wide display curtain. Browser targets
  hide Start, and PDF/browser targets hide Blackout and laser.
- Reports host permission denial, unsupported target actions, host-focus protection, native input failure, response timeout, and success without disconnecting the client.
- Includes a device-local elapsed timer with Start, Pause, and Reset. Reloading
  resets it.
- Lets the presenter choose a 10, 15, 30, 45, or 60 minute plan. Visible live-region text announces five minutes remaining and planned time elapsed; browsers that expose `navigator.vibrate` can optionally add vibration at those same milestones.
- Uses a single-column portrait layout and a compact two-column landscape layout while retaining the normal mode navigation as an obvious exit.

### Dictation mode

- Uses browser speech recognition API when supported by the browser and origin.
- Captures final speech recognition text.
- Sends dictated text to Windows through the same text input path.
- Allows dictation text to be edited/cleared on mobile.

### Menu and text transfer

- The hamburger drawer is a **Menu** with separate **Tools** and **Settings** groups.
- Dictation, **Send text to PC**, and **Get text from PC** can be opened directly from Menu without changing the fourth-mode preference. Presentation is added only while the host advertises its enabled alpha capability.
- Trackpad, Keyboard, and Remote remain fixed primary modes. The fourth mode can
  be configured as Dictation, Send text to PC, or Get text from PC and defaults
  to Dictation. Presentation is available while its alpha capability is enabled.
- **Send text to PC** composes or pastes up to 4,096 characters. Focused application input remains the default; the host can instead use clipboard-only delivery, a configured fresh Notepad, Notepad++, Word, Visual Studio Code, Excel, or classic Outlook compose item, a new `.txt` draft in the Windows default text-file app, or a `mailto:` draft in the Windows default email client.
- **Get text from PC** starts empty and fetches the current PC clipboard only after the user presses its button. It shows the returned maximum-4,096-character text in a selectable, read-only field, never writes to the phone/tablet clipboard, keeps prior fetched text after a failed request, and explains when the host has blocked the default-off **Read PC clipboard** permission. The permission can inherit the host global setting or be allowed/blocked per paired device. **Show snippets** reveals the existing local snippet controls for loading or saving text; they appear below the field in portrait and in a side panel in landscape.
- The editor switches between Keyboard and Touchpad input. Touchpad mode uses
  the trackpad grid and Left/Right buttons, including the saved left-handed
  layout.
- The destination warning is shown before delivery. Changing Windows focus changes the destination; the host refuses delivery while its own protected UI has focus.
- Preserves multiline text by delivering each LF, CRLF, or CR line break as one Enter key event.
- Offers **Send text** and **Send text + Enter**. The final Enter is sent only
  after Windows accepts the complete text.
- Confirms before sending 2,000 or more characters.
- Shows pending, success, timeout, and native-delivery failure feedback from a host-acknowledged operation.
- Supports an optional clear-after-send preference. The draft remains available after failure or when clearing is disabled.
- Stores up to 20 local snippets of up to 4,096 characters with unique,
  case-insensitive names. Snippets can be loaded, renamed, updated, deleted, and
  reordered with a 450 ms hold gesture. Loading never sends text automatically.
- The authenticated host status advertises only the safe destination mode, display name, and current availability. Executable paths, process IDs, window handles, matching rules, and clipboard contents are not exposed.
- Managed text transfer creates a local draft or stages text on the Windows
  clipboard. Paste-driven destinations paste only after the intended window is
  foreground and not elevated; otherwise the user receives a clipboard result
  for manual paste. Generated drafts are removed after 24 hours by default.
  **Keep generated draft files** retains them.

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
