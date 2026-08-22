# Product capabilities

Current user-visible capabilities, permissions, limits, states, and guarantees.
Installation and connection: [README](../README.md#download-and-install).
Development: [setup](setup.md). Wire detail: [protocol](protocol.md).

## Scope and guarantees

- A Windows 11 host accepts a paired browser-based PWA on a phone, tablet, or
  computer over local Wi-Fi/LAN or the internet. Standard Local serves the PWA
  from the host; Enhanced Direct and Relay use the first-party hosted PWA.
- No app-store install, account, subscription, or trial is required. 
  Standard Local connections use no cloud service. Enhanced Direct uses the
  first-party hosted PWA and bounded setup signaling before established control
  traffic stays on the selected private LAN. The optional Cloud Relay connects
  over the internet without opening an inbound connection to the PC.
- **View PC screen** lets a paired device view one selected Windows display;
  touch devices use its touch controls, while another computer can use direct mouse and keyboard control.
- **Phone webcam** turns a selected paired-phone camera into an optional-audio
  `Voltura Air Webcam` virtual camera for Windows applications.
- It is not a file-sync, backup, notification-sync, or cloud clipboard service.
- The client cannot control or wake a sleeping, shut-down, or unreachable PC.
- One host runs per signed-in Windows user. A second launch focuses it.

## Windows host

### Shell, connection, and pairing

- The Windows tray app provides Connect, Devices, Custom screens,
  Presentations, Connection, Preferences, and Diagnostics. Closing the window
  leaves the host running.
- Light, dark, system, Windows High Contrast, per-user installation, portable
  ZIP, and installer packages are supported.
- Connect shows a short-lived QR code, refresh countdown, **New code**, and
  **Copy link**. Mobile can reconnect to saved PCs or enter a host manually.
- Adapter and port selection are automatic by default. Connection allows a
  saved adapter and validated custom port; pending changes require **Save and
  restart** and remain visually distinct from active settings.
- Pairing creates one remembered relationship per client ID. Removing a device
  revokes it immediately and requires fresh pairing.
- Connection offers exclusive **Direct local network** and **Cloud relay
  through Voltura** methods. Direct remains the default. Relay binds the local
  host to loopback and makes the PC and device connect outward without opening
  the LAN listener; it never falls back to Direct automatically.
- Direct offers a default-off **Enhanced capabilities** preference. When
  enabled, the primary QR opens the first-party `/s` HTTPS controller and uses
  bounded hosted signaling to establish an authenticated WebRTC DataChannel
  over the selected private IPv4 LAN. Established commands do not pass through
  the hosted service. The local listener and `/ws` remain active, and Connect
  retains an explicit **Copy Standard Local link** action. Loading the hosted
  controller and completing setup require internet access. There is no probing,
  silent fallback, or automatic transport switching.
- Relay pairing uses the short first-party `https://voltura.se/a/<route>` QR.
  Its 32-character token stays in the URL fragment. Existing pairing,
  reconnect, identity pinning, permissions, revocation, and rate limits are
  reused inside an additional end-to-end encrypted relay session. Relay always
  includes enhanced browser capabilities because its controller already runs
  in Voltura Air's secure hosted app; the separate opt-in applies only to
  Direct connections using `/s`.
- Reconnect uses proof of possession; the private reconnect key remains on the
  client. Fresh pairing uses the single short QR token to authenticate and pin
  the PC's persistent public identity after opening; no host identity or second
  identifier is added to the QR. Pairing, reconnect, and commands are
  authenticated, bounded, and validated.

### Devices, permissions, and settings

- Devices shows name, platform/browser metadata, connection/activity state, and
  per-device settings. Users can rename/remove one device or remove all.
- Global defaults combine with per-device overrides for pointer speed,
  permissions, mode-button visibility, and the optional 3D control effect.
- The Windows host's 3D control effect is a separate appearance preference and
  defaults off. The mobile/device default is on; each paired device can inherit,
  enable, or disable it.
- Host permissions, network selection, and Awake state each persist as one
  bounded exact-shape registry JSON value. A failed write leaves the last cached
  state unchanged. Missing values use defaults; malformed permissions deny
  remote capabilities, malformed network state returns to automatic Direct,
  and malformed Awake state is Off. Superseded individual registry fields are
  not read.
- Host permissions cover sleep, volume, Screen viewing, Phone webcam, Presentation, file browsing/opening, file changes, application launch, web
  addresses, PC clipboard reads, Lock, Blackout, display off, screen saver,
  sign out, restart, shutdown, Keep awake, and interaction with the host UI.
- Unsupported actions are omitted; host-disabled actions explain the relevant
  permission. Manually sent unauthorized commands are rejected.
- Custom screens and Presentation are supported capabilities and remain
  available subject to their action permissions.
- Browser, Spotify, VLC, PowerPoint, and custom executable buttons are
  configured and tested locally. Mobile receives only an opaque action ID and a
  1–10-character label; paths and arguments stay on the PC.
- Custom executable add/edit requires a local warning confirmation and each
  launch revalidates its target.
- Custom pointer is host-wide, off by default, and configurable by size/color.
  Paired devices may toggle it. Cursor overrides return to the configured
  Windows scheme when Voltura Air exits.

### Live screen mirror

- **View PC screen** is a lazy-loaded mobile tool. It remains visible on hosts
  that support it and explains whether the effective Screen viewing permission
  or a fresh identity-pinning pairing is required.
- Direct viewing uses LAN ICE. Relay viewing uses relay-only TURN candidates,
  15-minute credentials renewed with a fresh negotiation, and **High** (8 Mbps),
  **Standard** (4 Mbps), or **Data saver** (2 Mbps). At 750 GB estimated monthly
  TURN transfer the service forces Data saver; at 850 GB it stops issuing
  credentials while command relay remains available. The host shows the
  provider's current used-versus-remaining allowance and thresholds on Connection.
- The bundled Windows libdatachannel peer retains libjuice as its ICE and TURN
  owner. In relay mode a bounded loopback bridge carries libjuice's TURN
  messages over certificate-validated TLS/TCP 443, including the stream framing
  and ChannelData padding required by RFC 8656. The PC therefore needs no
  outbound UDP for relay screen viewing; Direct LAN behavior is unchanged.
- Screen viewing is denied by default through its global permission and has an
  inheritable per-device override.
- One authorized device can view one selected display at a time. Multiple
  displays are selectable before or during viewing; another device receives a
  busy result. Leaving the workspace stops viewing.
- Windows Desktop Duplication supplies GPU display frames and cursor metadata.
  D3D11 converts frames to NV12 and a capability-selected hardware Media
  Foundation transform encodes baseline H.264 using the selected display's
  native aspect ratio and resolution/frame-rate combinations within the sender's
  advertised H.264 level, up to 60 frames per second. A direct LAN WebRTC peer
  sends video over DTLS-SRTP and cursor/status over a DTLS data channel.
  Monotonic capture pacing drops early frames from higher-refresh desktops before
  GPU conversion and encoding, so the selected profile's frame rate is a real
  wall-clock ceiling rather than only an encoder timestamp.
  Direct starts at the selected display's native resolution at 30 fps. Automatic
  derives a readable resolution floor from that display's physical pixels and
  effective Windows scaling; it reduces frame rate and generated intermediate
  resolutions without crossing that floor. Three bounded sender-pressure events
  or sustained receiver decoder loss can lower one profile. Fifteen seconds of
  healthy receiver decoding probes one profile upward, and a failed probe rolls
  back and cools down before retry. An encoder-rejected profile uses the same
  bounded fallback and later becomes eligible again.
- Direct Screen View has **Automatic**, **Quality**, and **Data saver** host
  settings. Quality retains the full selected-display resolution while adapting
  frame rate. Data saver is limited to 4 Mbps and starts within 1920 x 1080; it is
  the explicit mode allowed to cross the readability floor. Relay uses the same
  adaptive engine within the selected 8/4/2 Mbps ceiling. The mobile view shows
  its actual received resolution, frame rate, and bitrate from WebRTC statistics
  and sends bounded aggregate decoder-health counters while viewing.
- Screen media is independent of the JSON command socket, so a slow viewer
  cannot delay trackpad or keyboard commands. The WebRTC sender bounds queued
  media, supports packet retransmission and receiver keyframe requests, and
  starts capture only after its video track and event channel connect.
- One-finger movement controls the relative pointer. The compact two-finger
  switch defaults to **Zoom**, where spread/pinch locally magnifies the mirror
  from 1× to 10× around the
  gesture midpoint and two-finger drag pans locally. Switching modes preserves
  the current magnification and position. In **Scroll**, two-finger drag scrolls
  the PC; the separate scale action returns the mirror to 1x. A compact corner
  action available in both
  orientations expands the mirror edge-to-edge across the device viewport and
  remains expanded across orientation changes; its explicit exit action restores
  the normal workspace.
  Compact Click, keyboard, display, and Stop controls sit around the responsive
  canvas. V1 is video-only and excludes audio, absolute touch, windows,
  all-monitor composition, multiple viewers, and game optimization.
- On browsers with an available fine hovering pointer, supported hosts add a
  session-only mouse-and-keyboard icon action beside **Scroll/Zoom**. It starts
  inactive and directly maps movement, left/right click, drag, and wheel over
  the displayed image while excluding letterbox bars and overlay controls.
  Local zoom and pan remain usable. While the action is active, physical-key
  presses reuse the existing keyboard protocol; printable text, supported keys,
  Escape, and delivered modifier shortcuts are forwarded, while browser-reserved
  shortcuts may remain local. A second activation, navigation, reconnect,
  display or permission change, pointer removal, and stream termination disable
  it and release held buttons. Browser fullscreen may reserve Escape to exit
  fullscreen; leaving fullscreen does not otherwise disable the action. The
  local cursor is hidden only over the image while the mirrored Windows cursor
  remains visible. **Pointer and keyboard** permission is required; a supported
  but blocked device keeps a disabled explanatory action. Click, Keys, and all
  touch gestures are unchanged.
- The Windows tray shows a persistent viewing indicator with the paired device
  name and an immediate Stop action. Its submenu can stop the stream and set
  that device's **View PC screen** permission to **Block** so it cannot reconnect
  until the permission is changed. Both tray actions close the complete menu and
  tell the viewer whether the PC stopped or disallowed it. Permission/toggle
  revocation, disconnect, lock or session loss, host exit, display removal, and
  capture-device loss stop the stream and release native/network resources.
  Protected content and secure desktop are never replaced with another capture
  method.

### Phone webcam

- Phone webcam is a normal app tool and is not behind Developer mode. The Windows
  **Phone webcam** page reports protected-component status and active-phone state,
  previews the same camera exposed to other Windows applications, and directs
  unavailable or mismatched installations to installer maintenance. Windows
  **Installed apps → Voltura Air → Modify** reopens that same retained installer,
  preselects Phone Webcam from its protected installation state, and applies checking
  or unchecking through the existing install/repair/removal transaction. The optional
  installer component owns those changes through an explicit UAC boundary; the
  per-user host never elevates its LocalAppData executable.
- The global **Allow paired devices to use Phone webcam** permission defaults off
  and combines with an inheritable per-device **Use phone as webcam** override.
  Removing a pairing, revoking permission, stopping from the tray, host shutdown,
  capture loss, or transport loss terminates the owned session and returns the
  virtual camera to its waiting frame.
- The paired PWA asks for camera permission explicitly, releases the temporary
  permission probe, lists the cameras returned by the browser, and lets the user
  select one before **Start webcam**. It requests the best practical video up to
  1920 x 1080 at 30 frames per second and shows actual capture and encoded quality.
  **Use microphone** is off by default and appears only when the PC has a ready
  base VB-CABLE endpoint. Selecting it requests microphone permission from that
  gesture. Mute appears only after permission and an audio track are available.
  While that audio track is actively streaming, the Windows page exposes an
  explicit **Test audio** monitor that reads the same `CABLE Output` capture endpoint
  used by receiving apps and plays it through the default Windows speakers. The
  monitor is never automatic and stops with the page or session. Because it plays
  the phone microphone locally, use headphones or keep the phone away from the
  speakers during the test to avoid acoustic echo or feedback.
- Camera switching replaces the video track on the current healthy peer. After a
  device rotation, or when outbound encoded frames stop advancing while local
  capture remains live, the selected camera track is refreshed on that same peer
  so the Windows consumer does not remain frozen. **Stop
  webcam** and page hiding release every phone camera track immediately. If iOS
  closes the backgrounded peer, returning to the visible paired PWA makes one fresh
  authenticated session; it never appends work to the dead peer.
- Enhanced Direct sends DTLS-SRTP media on the selected private LAN and is free and
  unlimited. Relay uses relay-only candidates, the existing 15-minute TURN
  credentials and quota-derived 8/4/2 Mbps policy, and refreshes with a fresh
  session before credentials expire. Voltura-operated Relay use is initially free,
  shares the existing aggregate 750 GB Data Saver threshold and 850 GB cutoff, and
  adds no webcam-specific account, billing, entitlement, or quota.
- The host receives one H.264 track, bounds encoded and decoded work to latest-frame
  capacity, decodes through Media Foundation, and normalizes portrait, landscape,
  and lower-resolution input into fixed NV12 1920 x 1080 output. The native camera
  advertises only NV12 1920 x 1080 at 30 fps. A transient camera handoff retains the
  last valid frame; explicit stop, session loss, removal, and shutdown clear it
  immediately to the waiting frame.
- Optional audio adds one bounded Opus track, decoded in managed code and written
  to the exact detected VB-CABLE endpoint. VB-CABLE is third-party donationware,
  is not included or distributed with Voltura Air, and must be obtained directly
  from VB-Audio under the licence applicable to the user's use.

### Input and Windows actions

- Pointer movement, tap/click, held-button drag, right click, vertical/horizontal
  wheel, pinch zoom, Unicode text, special keys, function keys, browser/media/
  volume keys, and common modifier shortcuts are supported.
- Dispatch failures are reported; stale pointer movement stops after touch ends.
- Unicode text is sent in bounded batches without splitting surrogate pairs.
- The host reads/sets default output volume and mute.
- Allowed actions include sleep, Lock, Blackout, screen saver, display off, sign
  out, restart, and shutdown. Display off can suspend some PCs and requires
  physical input to wake them; it does not sign out the user.
- Blackout covers connected monitors without changing power state and ends on
  local or remote input.
- Keep awake offers Off, timed, date/time, or indefinite modes plus optional
  **Keep screen on**, without changing the selected Windows power plan.
- The host-only **Simulate activity every 59 seconds** option is independent of
  Keep awake modes and sends only an F15 key-up in the signed-in Windows
  session. It never moves or clicks the pointer, and applications that handle
  F15 or use their own presence rules can still react differently.
- Optional JSON Lines application logging is off by default, retained 1–30 days
  (2 days by default), and omits typed text, URLs, pointer coordinates, and
  pairing credentials. Diagnostics provides filters, copy, folder, delete, and
  session-only automatic refresh.

### Usage statistics

- Usage statistics are optional and off until explicitly allowed. A normal
  installer shows equal **Allow usage statistics** and **Do not allow** actions
  only when the installed choice is unset. The safe choice has initial keyboard
  focus and ordinary Next navigation stays disabled until one action is
  activated. Silent installation leaves the choice unset/off. An existing
  Allow or Do not allow choice survives upgrades and reinstalls and skips the
  page; an upgrade from a version without a choice prompts once.
- The installer holds an unset choice only in memory and saves the single
  installed-consent registry value after the verified install transaction
  commits. Cancel, rollback, or pre-commit health failure writes nothing. A
  failed write leaves statistics off and directs the user to Diagnostics. The
  installer never creates the telemetry identifier.
- Installed execution is selected only when the normalized host executable
  directory exactly matches the existing uninstall entry's `InstallLocation`.
  Every other execution is portable. Installed and portable choices and random
  identifiers are separate. A portable copy starts off without a first-run
  popup and preserves a later explicit choice for that Windows account.
- **Diagnostics → Usage statistics** is separate from Application log. It shows
  On/Off, the applicable installed/portable profile, collected categories,
  prohibited categories, the privacy policy, and one serialized Enable/Disable
  action. It never displays the identifier. Enable is effective only after a
  new identifier and Allow are durably saved. Disable becomes locally effective
  first, cancels and clears all telemetry work, saves Do not allow, and removes
  the identifier. Save or identifier-cleanup failures remain visible with an
  exact retry instruction.
- While allowed, the Windows host counts one telemetry-active start per consent-enabled local-identifier lifetime (normally once per process; disabling discards that lifetime, and re-enabling starts a new unlinkable one),
  successful authenticated sessions by Standard Local, Enhanced Direct, or
  Relay, and at most one use per authenticated session in each consent-enabled
  identifier lifetime of Trackpad, Keyboard, Dictation, Media controls,
  Presentation, Custom screens, Files, Screen
  viewing, Phone webcam, and Gyro mouse. These are feature-using sessions, not
  clicks or guaranteed downstream outcomes. The closed content exclusions and
  server retention are owned by the [privacy policy](../PRIVACY.md#optional-usage-statistics).
- The PWA has no telemetry endpoint, identifier, consent state, queue, or
  persistence. It sends only capability-gated functional input context through
  the existing authenticated PC connection. Telemetry is a no-op when disabled
  and never delays input, media, UI, rendering, or connection processing.

### Custom screens

- The Windows editor creates reusable, explicitly saved control screens and
  assigns each screen to any number of paired devices. The library supports
  native-window Preview, duplicate, delete, assignment, and drag-handle ordering;
  screen order is the order shown on mobile. Preview is read-only and uses the
  saved mobile rendering through the host's loopback address beneath fixed
  themed WPF device, orientation, and Rotate controls. The editor also provides
  Preview beside Save; it becomes available after Save and disables again when
  the draft has unpublished changes. Preview device choices include Generic
  phone/tablet, the selected paired Mobile device dimensions when applicable,
  and the maintained phone/tablet sizes used by UI validation. Leaving Custom
  screens closes every open preview window.
- The editor calls its responsive containers panels and uses them rather than
  free coordinates. Regular and collapsible button panels use a 12-column outer
  grid, six snapped widths, content or weighted fill height, and automatic or
  one-to-six explicit button rows. Each button panel places its intrinsic
  button widths using Start, Center, End, Space between, Space around, or Space
  evenly; Start is the default. Fill-height button panels grow to contain their
  configured rows, and the workspace scrolls vertically when those rows exceed
  the available client height. A collapsible panel requires its name as a
  toggle header while retaining the regular panel properties. Its folded state
  in the preview is saved as the device default. Regular and collapsible
  trackpad panels use those same snapped widths, wrapping, content/fill height,
  fill-weight, and orientation rules. They offer optional Left/Right buttons
  beneath the surface in either order, an optional fullscreen/restore control,
  and an optional Touch/Gyro movement selector.
- A standalone Volume slider component reuses the normal mobile volume control
  and existing device volume permission. It occupies 25%, 50%, 75%, or 100% of
  the custom-screen row and may use independent orientation width, order, and
  visibility.
- A Navigation ring component reuses the Remote ring's repeatable directions
  and places it on the regular gridded trackpad surface under the existing remote-input
  permission. The center and surrounding surface both accept pointer gestures.
  It uses 50%, 67%, 75%, or 100% width so every ring zone remains usable, and
  supports content/fill height plus independent orientation width, order, and
  visibility.
- Buttons have separate editor names and visible labels, bundled icons,
  icon/label presentation, compact/standard/wide/fill sizing, and optional
  repeat for actions explicitly marked repeatable. Actions are short literal
  text, a single key or modifier shortcut, a host-local application action, an
  HTTP(S) website, a portable known-application profile, an allow-listed
  host/system action, or a curated media/navigation/browser/Windows action.
  Literal text and custom
  key/shortcut actions are label-only; built-ins and approved applications may
  use bundled icons. Screen and button editor
  names are limited to 24 characters, panel, trackpad, and navigation-ring names to 20, and
  visible button labels to 16.
- The dedicated **Laser pointer** palette item creates a normal button with a
  fixed Laser pointer action, a non-repeatable default configuration, and a
  Default, Red, Green, or Blue color choice. Default follows the current
  Presentation laser color; explicit colors override only that activation.
  Laser buttons retain the normal name, label, icon, size, row, orientation,
  movement, and delete properties and cannot be created by changing a generic
  button's action type.
- The shortcut builder stages a command before applying it. Selected modifiers
  leave the available choices and appear in the command preview in selection
  order. A selected letter or number remains visibly selected. The editor does
  not offer AltGr together with Ctrl or Alt because those choices overlap on
  Windows keyboard layouts. The Letter
  or number selector provides A-Z and 0-9. Dedicated Function
  and Special key selectors provide F1-F12,
  Backspace, Delete, Enter, Insert, Page up/down, Home, End, and the four arrow
  keys. A Symbol key selector provides common punctuation including period,
  comma, and semicolon. A Numpad or media selector covers Numpad0-9, arithmetic
  and decimal keys, media transport, and volume keys. Reset clears the staged command, and **Save command** is
  available once any non-modifier final key completes it; for example,
  `CTRL + ALT + Escape` and `CTRL + SHIFT + Escape` are valid while
  `CTRL + ALT` is not.
- Optional portrait and landscape layouts begin as peer copies of the
  responsive layout; neither orientation becomes the master. Component
  identity, names, labels, and actions remain shared, while each orientation
  independently controls visibility, order, section width, button size, and
  button row. Components added after orientation layouts are enabled start
  visible only in the active orientation. **Hidden controls** lists components
  hidden from the active canvas and can show and select them there.
  Showing a hidden panel also restores all of its contained controls in that
  orientation. Hidden button entries identify their containing panel.
- Drag/drop and the properties panel move components and explicit button rows.
  The selected row is the Add-button target. Dragging follows the pointer with
  a scaled, translucent snapshot that preserves the original grab point while retaining the
  destination marker. Dropping a new or existing button on open workspace
  creates a regular panel and places the button in it. Panels and buttons use a
  compact themed context menu. Dragging a nested button moves only that button;
  panel dragging begins from panel space outside nested interactive controls.
  With orientation layouts enabled, **Hide in Portrait/Landscape** affects only
  that layout and **Delete everywhere** removes the shared component. Delete
  and Hide confirmations are independently configurable; both operations
  remain undoable until Save.
- The component palette scrolls when its available height is constrained.
  Palette labels wrap instead of clipping. The themed dividers around the
  preview resize the component and properties columns from their default
  minimum widths, persist both widths for the signed-in user, and leave the
  remaining space to the uniformly scaled device preview.
  Available components starts expanded, while Layout, Hidden controls, and
  Editing start collapsed; all use the same compact `+`/`−` disclosure rows as
  the properties inspector and support header-level expand-all and collapse-all.
  Hidden controls is a separate row shown when orientation layouts are enabled.
  Layout also controls whether the mobile workspace shows its Back/title row.
  The Action group starts open, and controls
  belonging to the selected action type share a subtle framed background.
  Generated Name and Label groups start open, and the inspector header can
  expand or collapse every property group.
- Definitions and assignments use the exact version-4 lower-camel JSON shape. They are
  stored atomically under the signed-in
  user's application-data folder. Invalid current-format files are preserved
  and reported instead of replaced. Other versions are rejected without
  migration or fallback. A themed recovery dialog can keep the invalid file or
  delete it and start with an empty library.
- The editor's **Validate** action checks the unsaved draft without changing it
  or disabling Save. It uses the real mobile renderer at 360 x 640 and 640 x
  360 to report clipped labels and horizontal overflow, checks shortcut and URL
  validity, and reports unavailable applications or disabled permissions.
  Findings explain potential resolutions and can select the affected panel or
  button; intentional clipping and other warnings remain allowed. Validation
  runs entirely as the signed-in user and never requests administrator rights.
- Successful and rejected Save, assignment, duplicate, reorder, delete,
  preview, and validation operations use the optional Application log. Entries contain
  the operation and outcome only, never screen names, labels, literal text,
  shortcuts, executable details, or drag activity.
- Each saved screen can be exported as a versioned `.volturascreen` package to
  a local file or prepared for submission through the authenticated community
  library upload page. Portable packages reject host-local application actions,
  executable paths, commands, alternate JSON shapes, and device assignments;
  known applications use fixed portable profiles. Imports show panels, buttons,
  and action types, never retain device assignments, and generate new local IDs.
- `npm run screens:official` deterministically generates 14 official Windows 11
  screens plus `catalog.json` and one ZIP bundle from concise source definitions.
  The collection covers VLC, Spotify, Web Browser, Netflix, Prime Video,
  Disney+, Twitch, Plex, Zoom, Windows, Power, Displays, Windows Photos, and
  Blender Numpad. A screen with exactly one known-application target disables
  every control while that application is unavailable. Windows Photos requires
  a usable Microsoft Photos URI handler and never falls back to File Explorer.
  Regeneration is byte-identical when definitions do not change.
- Reviewed community packages are available from
  `https://voltura.se/air/screens/`. Catalog installation opens the same import
  review and never executes actions automatically. The library supports search,
  previews, downloads, account-based ratings, and reports emailed to Voltura Air
  for review. Authors can open
  their submission history directly, remove rejected entries from that history
  without permanently deleting their records, receive approval or rejection
  email, read optional approval or required rejection feedback, and resubmit
  edited metadata for review. Administrators can permanently delete an
  approved entry from the library after confirmation. Administrators can also
  import the complete official ZIP atomically by stable official ID after
  confirming the current Windows 11 smoke matrix; updates preserve package IDs,
  ratings, and download counters.
- The Windows tray menu links directly to the community library.
  The Custom Screens library also provides a **Browse library** button beside
  local import and creation actions.

## Mobile PWA

The mobile web app runs in modern browsers and can be installed where
supported. Its browser profile stores device identity/name, saved PCs, local UI
preferences, text snippets, and theme. It provides a cache-reset flow and can
refresh its installed shell once after reconnect.

### Pairing and connection states

QR open/photo scanning, HTTPS live camera scanning, device-name confirmation,
saved-PC reconnect, and manual origin/address/port/link entry are supported.
Live scanning starts only after the user requests it, prefers the rear camera,
decodes transient frames locally, and returns to photo capture when camera
access is declined, cancelled, unavailable, or interrupted. HTTP keeps photo
capture as the QR option. The UI distinguishes needs
pairing, connecting, paired, rejected, unavailable/retrying, and disconnected.
It explains unreadable/non-Voltura QR codes, expired codes, revoked devices,
invalid reconnect proof, unreachable hosts, and input acknowledgement failures.
Diagnostics copies redact tokens, private keys, challenges, and proofs.

### Trackpad

- One-finger movement; tap, long-press, and two-finger right click; physical
  left/right buttons; held-button drag; two-axis scroll; optional **Pinch zoom**.
  When Pinch zoom is enabled, a compact Trackpad switch chooses explicit
  **Scroll** or **Zoom** behavior so one gesture cannot be mistaken for the other.
- The main Trackpad and Presentation's embedded Trackpad offer **Touch** and
  **Gyro** movement. Gyro mouse uses motion sensors in a phone or tablet while
  the user holds the trackpad surface or either mouse button. A surface tap
  clicks, and a double-tap uses the PC's configured double-click behavior. It
  keeps the pointer still during short taps so normal hand movement does not
  break double-click recognition, then begins pointer movement when the surface
  is held. It sends ordinary pointer movement, so Presentation's separately
  controlled laser cursor moves naturally without any sensor-specific host
  behavior. Custom Screen trackpads can also provide the same Touch/Gyro
  selector. Screen View trackpads remain touch-only.
- Gyro mouse requires Enhanced capabilities over HTTPS (always present for
  Relay and available through Secure Direct). Motion permission is requested
  from the user's Gyro action and is never remembered as an active mode. Gyro
  stops on release and is disabled when its Trackpad closes, the page becomes
  hidden, or the connection changes. Unsupported, denied, insecure, and
  no-sensor-data states remain visible with recovery guidance.
- Pointer speed, smoothing, acceleration, scroll acceleration/direction,
  gyro sensitivity, haptics, handedness, large buttons, and volume controls.
- Full-screen trackpad and an optional host-enabled gesture debug surface.
- Touch ownership suppresses page scrolling, callouts, and accidental selection
  on the control surface.

### Keyboard

- Live typing or buffered multiline send, mobile text/numeric keyboard choice,
  IME composition, and repeatable editing/navigation keys.
- Optional F1–F12, arrow, control/shortcut, and sleep rows. Sleep requires host
  permission, a local setting, and confirmation.
- Visible shortcuts include select/cut/copy/paste, undo/redo, and forward/reverse
  app switching.

### Remote

- Standard, YouTube, and Kodi mappings cover media, seek, navigation, volume,
  mute, fullscreen, app switching, task view, desktop/window, browser-tab/page,
  and mode-specific actions.
- The default navigation ring includes repeatable directions and a center
  mini-trackpad; an alternative D-pad with OK is available.
- **Power & session** provides Keep awake, Lock, Blackout, screen saver, display
  off, sign out, restart, and shutdown according to host capability/permission.
  Disruptive actions require a 1.6-second confirmation hold.
- An Fn panel opens validated HTTP/HTTPS addresses and host-approved application
  buttons. Pending/result feedback stays with the action; URL drafts survive
  failure.
- Compact layouts move secondary Windows/browser actions behind Fn. Remote
  settings control mappings, helper visibility, and allowed application
  shortcuts.

### Custom screens

- Assigned screens appear in a **Custom screens** Menu group. Opening one uses
  the main workspace and hides the top and bottom mode-button rows. The quick
  mode selector remains available and selecting a standard mode exits the
  custom screen. The saved layout may show or omit its Back/title row; the main
  app header remains available.
- Mobile renders the responsive or active orientation layout against its real
  viewport, retains minimum touch targets, and scrolls when the content cannot
  fit. Optional regular-panel headers, required collapsible-panel headers,
  bundled icons, labels, button rows, regular/collapsible trackpad panels, and
  navigation rings
  inherit the normal theme and control-depth treatment. Button placement
  preserves intrinsic compact/standard/wide widths and applies the saved flex
  distribution. Navigation rings provide repeatable directions on the regular gridded
  trackpad surface under the existing remote-input availability state. Standalone
  volume sliders reuse the normal mute, slider,
  current-value, and permission behavior. Content rows take their
  required height; fill rows divide the remaining workspace by fill weight.
  A collapsible panel starts in its host-saved default state and can then be
  folded or unfolded locally. A trackpad's optional fullscreen control overlays
  the workspace and Restore returns it to the same responsive row and size.
- The PC resolves every opaque button ID at invocation time. Missing approved
  applications and denied permissions leave the control in place but disabled
  with a reason. A stale revision cannot execute; mobile refetches the screen.
- Laser pointer buttons use Presentation-control permission, not Remote input.
  The host remains authoritative for their pressed color and single-device
  ownership. Buttons resolving to the same concrete color appear pressed
  together; leaving the screen sends an idempotent owner-only off request.
- Repeatable arrows, seek, and volume controls use the standard 400 ms initial
  delay and 55 ms cadence and stop on release, cancellation, lost capture,
  visibility loss, unmount, or disconnect.

### Presentation

- The fourth mode controls PowerPoint, Google Slides, or PDF/browser
  presentations. PowerPoint control enumerates open presentations and a
  host-derived, deduplicated list of still-existing PowerPoint files linked by
  saved reports. The mobile chooser receives opaque IDs, titles, and filenames,
  never local paths. Back retains an allowed open or saved selection without
  starting it. A retained saved file can be started from the main controls, and
  **Open and present** remains the chooser shortcut; either explicit start
  starts PowerPoint when necessary, opens that exact host-validated path,
  starts its slideshow, and begins the authoritative session. Starting from any
  authorized phone takes control: the same deck continues its existing session,
  while a different deck saves the previous session automatically before
  starting. Ordinary discovery and control commands never launch a file or fall
  back to global input.
- A sole open PowerPoint presentation is selected automatically. Multiple open
  presentations require an opaque runtime selection. Direct automation supports
  Start from beginning/current, Next, Previous, First, Last, numbered slide,
  End, black/white screen, and explicit automatic-playback pause/resume. While
  already presenting, Start from beginning returns to slide 1 and Start from
  current foregrounds the existing slideshow.
  the selected presentation is Ready, Voltura Air reports the current editor
  slide when PowerPoint exposes it. Start and numbered navigation start its
  slideshow; Previous and Next start from that known editor slide and then
  navigate once. They remain unavailable rather than guessing when the editor
  slide cannot be read. The same black/white controls use Voltura Air's
  full-display overlay. While Presenting, black/white use PowerPoint's native
  slideshow states. Each slideshow command resolves and brings the selected
  slideshow window to the foreground before it operates.
  Explicitly entering PowerPoint Presentation mode also foregrounds the selected
  open presentation or running show; reconnects, app refreshes, and pointer
  cleanup do not.
- Presentation discovery and saved-file launch live in a dedicated chooser.
  The controller retains only the selected name, slide/state summary, and
  Change action. The chooser remains available during a session and explains
  that starting another deck saves the current session automatically.
- Google Slides and PDF/browser retain their reviewed shortcut controls and
  local timer/report path.
- The laser is Voltura Air's custom cursor, not PowerPoint's native laser.
  PowerPoint's arrow visibility is adjusted on a best-effort basis and restored
  to automatic on disable or mandatory cleanup; a native pointer-option failure
  does not disable Voltura Air's cursor. Explicitly starting a presentation
  disables any existing laser as part of the takeover. Presentation mode always
  uses the current default Presentation color. A Custom screen can temporarily request Red, Green, or Blue without
  changing Preferences; its size still follows the global laser size, Default
  follows later preference changes, and an explicit color remains explicit.
- A PowerPoint session starts when mobile starts the slideshow or explicitly
  chooses **Start tracking**. The host owns monotonic timing, manual breaks,
  current/total slide state, and a bounded ordered visit timeline. Black, white,
  and paused slideshow states do not create breaks.
- Any authorized phone may manage breaks and Save/Discard during active or
  paused tracking. Explicitly starting
  the same presentation transfers control while preserving the report; starting
  another presentation saves the prior report automatically. Slideshow exit
  pauses tracking and offers Continue presentation as the primary action, with
  Save/Discard secondary. When PowerPoint is in edit mode, the paused session reconciles
  its current position to PowerPoint's current editor slide while preserving
  the completed visit history. Continue starts from that slide and resumes the
  existing report, visits, and elapsed time; time with the slideshow
  closed is excluded. A different presentation never inherits the paused
  session. The trusted local Presentations page may also save or discard it,
  and the atomic local draft survives disconnect or host restart. Existing
  per-slide totals are derived from the visit timeline.
- Starting a manual break shows a Voltura Air blackout on every display with a
  live break duration and resume guidance. Any local or remote input may dismiss
  the blackout for safety; the host then reminds the presenter that timing
  remains on break until **Resume presentation** is pressed. Resume removes any
  remaining overlay, returns the slideshow to focus, and, if necessary, reopens
  the exact tracked file and restores the last slide before reporting failure.
- The Windows archive filters by title, type, device, and date; shows totals,
  timelines, and detail; and supports rename, file/URL links, deletion, HTML,
  XLSX, PDF, formula-safe CSV, text export, and email drafts. Reports saved
  from authoritative PowerPoint sessions use the selected presentation's
  PowerPoint name and retain its host-only canonical file link.
- Saved reports stay in the signed-in user's local application data.
  A bounded manifest is the sole archive inventory: at most 1,000 report entries
  name content-addressed files and record their length and SHA-256. Store mutations
  are serialized and journaled across artifact and atomic-manifest replacement;
  recovery touches only recorded paths. Unknown files are never imported, shown,
  or deleted, and an unrecognized recovery state leaves the archive unavailable.
  Effective global and per-device Presentation permission gates control,
  session tracking, saved-file launch, and report saves.

### Files

- **Files** is a lazy-loaded, touch-first view of files that remain on the Windows PC or its mapped drives. It is an optional fourth-mode choice and a Menu tool; Presentation remains the default fourth mode. A saved Files choice falls back to Presentation on an older host, then to Dictation when Presentation is also unavailable.
- Below 640 CSS pixels Files shows one panel; at 640 pixels or wider it shows two equal, independently scrollable panels. Each panel has a drive selector, its own location, selection, sort, and scroll state. The active panel receives the shared Windows-known-folder menu. Valid panel locations are restored per paired device, initially preferring Downloads and Documents.
- Folders sort before files. `..` is first when a parent exists. Name, Size, Type, and Modified are host-sorted across the complete directory; ordinary hidden-only or system-only items, read-only, archive, and reparse-point attributes remain visible. **Hide protected operating system files and folders (recommended)** is enabled globally by default with a per-device Use global/Hide/Show override. The host excludes items carrying both Windows Hidden and System attributes before counts, sorting, pagination, selection, or operations; disabling the effective setting exposes them with their attributes.
- Directory responses contain at most 100 entries and an opaque continuation. Near-end scrolling loads and appends the next page; a failed page preserves loaded rows and shows Retry. Rows are virtualized. Select all represents the complete current directory revision with explicit exclusions, including entries not loaded when Select all was pressed.
- Folder taps navigate; file taps select; checkboxes explicitly multi-select files or folders. A long press on a file, folder, or the current location opens Properties. Names use up to two lines before truncating, and the `..` navigation row carries no file metadata. Navigation is atomic: an unavailable or Windows-protected destination leaves the current panel and connection unchanged and reports the local folder error. File operations distinguish a missing or unavailable item from Windows denying the PC account permission to change it. In one-panel layouts Cut and Copy target the Windows clipboard. In two-panel layouts the primary Copy and Move actions target the other panel's current folder, while a secondary Clipboard menu retains Windows Cut and Copy. Paste, Properties, Delete, Rename, View, Open, Select all, and Unselect all remain directly available.
- Cut, Copy, and Paste use the real Windows Shell file clipboard and interoperate with Explorer. **Open** delegates to the Windows default application and stays in Files; opening a folder launches Explorer. **View** is available for files when both effective Browse/open and PC Screen authorization permit it: the host first confirms the Shell open, then mobile enters the independently authorized encrypted PC Screen mirror. A failed Shell open or unavailable/denied Screen permission leaves Files active with guidance. With one display the mirror starts automatically; multiple displays retain the Screen chooser. Back returns to Files. Delete is confirmed on mobile and uses only the Recycle Bin; the host rejects the whole request before queueing when any item cannot be recycled.
- Copy, Move, Paste, Rename, and Delete return a host job immediately. One mutation runs host-wide while the rest queue within a bounded, fully inspectable operation window. Direct Copy/Move binds both rendered panel revisions before queueing, so later navigation cannot redirect an operation. Jobs expose preparing, running, pause/resume/cancel, conflict attention with Replace/Skip/Cancel and apply-to-all, byte/item progress, rate, ETA, completion/failure, and restart interruption. Every completed, failed, or canceled tracked operation refreshes each panel that may already have changed; refreshed revisions reset their selections, while a failed Copy can preserve the unchanged source selection. Jobs continue across mobile mode changes and reconnects and are visible and controllable only to their originating paired device. The newest completed, failed, canceled, and interrupted history remains available and can be removed individually or cleared together; dismissing history never discards the host's private recovery responsibility. Partial-copy and replacement paths are created only after their recovery records are durably saved. Restart recovery removes journaled partial destinations, restores an original destination interrupted during replacement, retains temporarily unavailable cleanup for a later retry, and does not automatically resume work.
- Files keeps the app header and compact mode selector fixed and hides the large mode rows. File lists own ordinary native touch scrolling, including two-finger scrolling, without feature-specific magnification or transformed layout state.
- Separate default-off **Browse and open files** and **Change files** permissions have global defaults and per-device overrides. The default-on protected-operating-system-item filter uses the same global plus per-device policy model. A supported but denied device keeps Files visible with permission guidance. Revocation closes opaque navigation sessions and cancels that device's active mutation work.

### Dictation and text transfer

- Dictation uses browser speech recognition when available, lets users
  edit final text, and sends it through the normal Windows text path.
- **Send text to PC** handles up to 4,096 characters. Destinations include the
  focused app, clipboard only, configured fresh document/app targets, a new
  text draft, or an email draft. Windows focus determines the target; delivery
  to the protected host UI is refused. On a supporting HTTPS controller,
  explicit **Paste from this device's clipboard** reads plain text from the device clipboard and
  inserts it at the current selection without changing the configured PC
  destination. Denial, failure, empty text, or an oversized result preserves
  the current draft.
- Multiline input preserves line breaks. **Send text + Enter** adds Enter only
  after complete delivery. Sending 2,000+ characters requires confirmation.
- Pending, success, timeout, and delivery failure are explicit. Drafts remain
  after failure or when clear-after-send is off.
- Up to 20 browser-local snippets of 4,096 characters have unique
  case-insensitive names and can be loaded, reordered, renamed, updated, or
  deleted. Loading never sends automatically.
- **Get text from PC** requests at most 4,096 clipboard characters only after
  explicit activation and requires the default-off host permission. **Get PC
  clipboard text into this box** updates the visible field; copy selected text,
  select, cut, clear, and local snippets operate on that field. On a supporting
  HTTPS controller, **Get PC clipboard text into this device's clipboard**
  requests fresh Windows clipboard text on every press and writes it directly
  to the current phone, tablet, or computer clipboard without changing the
  visible field. Both actions remain independently reusable, and failed reads
  or copies keep existing visible text. Voltura Air never monitors or
  synchronizes either clipboard.
- Managed destinations never expose executable paths, process/window IDs,
  matching rules, or clipboard content to mobile. Generated drafts expire after
  24 hours unless **Keep generated draft files** is enabled.

### Navigation and split layout

Trackpad, Keyboard, and Remote are fixed primary modes. The configurable fourth
mode is Presentation, Files, Dictation, Send text, or Get text and defaults to
Presentation. An unavailable Files choice falls back to Presentation, then
Dictation; an unavailable Presentation choice falls back to Dictation.
All tools remain directly available from Menu.

Wide landscape can show keyboard and trackpad side by side with selectable pane
order, a scrollable keyboard, fixed trackpad, optional header, and
host/per-device mode-button visibility. Volume is hidden in split mode.
