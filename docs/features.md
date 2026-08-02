# Product capabilities

Current user-visible capabilities, permissions, limits, states, and guarantees.
Installation and connection: [README](../README.md#download-and-install).
Development: [setup](setup.md). Wire detail: [protocol](protocol.md).

## Scope and guarantees

- A Windows 11 host serves a phone/tablet PWA on the same Wi-Fi or LAN.
- Normal use needs no mobile app-store install, account, subscription, trial,
  cloud relay, or internet input-forwarding service.
- Voltura Air includes an optional local live display mirror, but is not a
  general remote-desktop, file-sync, backup, notification-sync, or cloud
  clipboard service.
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
- Host permissions cover sleep, volume, Screen viewing, Presentation, application launch, web
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
- Screen viewing is denied by default through its global permission and has an
  inheritable per-device override.
- One authorized device can view one selected display at a time. Multiple
  displays are selectable before or during viewing; another device receives a
  busy result. Leaving the workspace stops viewing.
- Windows Desktop Duplication supplies GPU display frames and cursor metadata.
  D3D11 converts frames to NV12 and a capability-selected hardware Media
  Foundation transform encodes baseline H.264 at up to 1920 x 1080 and 30
  frames per second. A direct LAN WebRTC peer sends video over DTLS-SRTP and
  cursor/status over a DTLS data channel. Receiver bandwidth estimates adjust
  the bounded bitrate automatically; v1 exposes no quality control.
- Screen media is independent of the JSON command socket, so a slow viewer
  cannot delay trackpad or keyboard commands. The WebRTC sender bounds queued
  media, supports packet retransmission and receiver keyframe requests, and
  starts capture only after its video track and event channel connect.
- One-finger movement controls the relative pointer. A compact two-finger mode
  switch defaults to **Scroll**, where two-finger drag only scrolls the PC. In
  **Zoom**, spread/pinch locally magnifies the mirror from 1x to 5x around the
  gesture midpoint and two-finger drag pans locally. Switching modes preserves
  the current magnification and position; the separate scale action returns the
  mirror to 1x. A compact corner action available in both
  orientations expands the mirror edge-to-edge across the device viewport; its
  explicit exit action or any orientation change restores the normal workspace.
  Compact Click, keyboard, display, and Stop controls sit around the responsive
  canvas. V1 is video-only and excludes audio, absolute touch, windows,
  all-monitor composition, multiple viewers, and game optimization.
- The Windows tray shows a persistent viewing indicator with the paired device
  name and an immediate Stop action. Permission/toggle revocation, disconnect,
  lock or session loss, host exit, display removal, and capture-device loss stop
  the stream and release native/network resources. Protected content and secure
  desktop are never replaced with another capture method.

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
- Optional JSON Lines application logging is off by default, retained 1–30 days
  (2 days by default), and omits typed text, URLs, pointer coordinates, and
  pairing credentials. Diagnostics provides filters, copy, folder, delete, and
  session-only automatic refresh.

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
  one-to-three explicit button rows. Each button panel places its intrinsic
  button widths using Start, Center, End, Space between, Space around, or Space
  evenly; Start is the default. A collapsible panel requires its name as a
  toggle header while retaining the regular panel properties. Its folded state
  in the preview is saved as the device default. Regular and collapsible
  trackpad panels use those same snapped widths, wrapping, content/fill height,
  fill-weight, and orientation rules. They offer optional Left/Right buttons
  beneath the surface in either order and an optional fullscreen/restore
  control.
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
  text, a single key or modifier shortcut, an approved application action, or
  a curated media/navigation/browser/Windows action. Literal text and custom
  key/shortcut actions are label-only; built-ins and approved applications may
  use bundled icons. Screen and button editor
  names are limited to 24 characters, panel, trackpad, and navigation-ring names to 20, and
  visible button labels to 16.
- The shortcut builder stages a command before applying it. Selected modifiers
  leave the available choices and appear in the command preview in selection
  order. A selected letter or number remains visibly selected. The editor does
  not offer AltGr together with Ctrl or Alt because those choices overlap on
  Windows keyboard layouts. The Letter
  or number selector provides A-Z and 0-9. Dedicated Function
  and Special key selectors provide F1-F12,
  Backspace, Delete, Enter, Insert, Page up/down, Home, End, and the four arrow
  keys. A Symbol key selector provides common punctuation including period,
  comma, and semicolon. Reset clears the staged command, and **Save command** is
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
- Definitions and assignments are stored atomically under the signed-in
  user's application-data folder. Invalid current-format files are preserved
  and reported instead of replaced. Unsupported store versions are also left
  unchanged and reported so they can be recovered with a compatible version.
- Successful and rejected Save, assignment, duplicate, reorder, delete, and
  preview operations use the optional Application log. Entries contain
  the operation and outcome only, never screen names, labels, literal text,
  shortcuts, executable details, or drag activity.
- Each saved screen can be exported as a versioned `.volturascreen` package to
  a local file or prepared for submission through the authenticated community
  library upload page. Imports show panels, buttons, action types, and host-local
  application-action warnings; they never retain device assignments and
  generate new local IDs.
- Reviewed community packages are available from
  `https://voltura.se/air/screens/`. Catalog installation opens the same import
  review and never executes actions automatically. The library supports search,
  previews, downloads, account-based ratings, and reports emailed to Voltura Air
  for review. Authors can open
  their submission history directly, remove rejected entries from that history
  without permanently deleting their records, receive approval or rejection
  email, read optional approval or required rejection feedback, and resubmit
  edited metadata for review. Administrators can permanently delete an
  approved entry from the library after confirmation.
- The Windows tray menu links directly to the community library.

## Mobile PWA

The mobile web app runs in modern browsers and can be installed where
supported. Its browser profile stores device identity/name, saved PCs, local UI
preferences, text snippets, and theme. It provides a cache-reset flow and can
refresh its installed shell once after reconnect.

### Pairing and connection states

QR open/photo scanning, device-name confirmation, saved-PC reconnect, and manual
origin/address/port/link entry are supported. The UI distinguishes needs
pairing, connecting, paired, rejected, unavailable/retrying, and disconnected.
It explains unreadable/non-Voltura QR codes, expired codes, revoked devices,
invalid reconnect proof, unreachable hosts, and input acknowledgement failures.
Diagnostics copies redact tokens, private keys, challenges, and proofs.

### Trackpad

- One-finger movement; tap, long-press, and two-finger right click; physical
  left/right buttons; held-button drag; two-axis scroll; optional **Pinch zoom**.
  When Pinch zoom is enabled, a compact Trackpad switch chooses explicit
  **Scroll** or **Zoom** behavior so one gesture cannot be mistaken for the other.
- Pointer speed, smoothing, acceleration, scroll acceleration/direction,
  haptics, handedness, large buttons, and volume controls.
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
  starts its slideshow, and begins the authoritative session. Active sessions
  and laser ownership keep the current deck selected and disable alternatives
  until the ownership is resolved. Ordinary discovery and control commands
  never launch a file or fall back to global input.
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
  Change action. Active sessions and laser ownership allow browsing but block
  switching until the current ownership is completed.
- Google Slides and PDF/browser retain their reviewed shortcut controls and
  local timer/report path.
- The laser is Voltura Air's custom cursor, not PowerPoint's native laser.
  PowerPoint's arrow visibility is adjusted on a best-effort basis and restored
  to automatic on disable or mandatory cleanup; a native pointer-option failure
  does not disable Voltura Air's cursor. An active laser prevents presentation
  switching.
- A PowerPoint session starts when mobile starts the slideshow or explicitly
  chooses **Start tracking**. The host owns monotonic timing, manual breaks,
  current/total slide state, and a bounded ordered visit timeline. Black, white,
  and paused slideshow states do not create breaks.
- The starting device owns mobile breaks and Save/Discard. Slideshow exit pauses
  tracking and offers Continue presentation as the primary action, with
  Save/Discard secondary, without blocking the same presentation from
  restarting. When PowerPoint is in edit mode, the paused session reconciles
  its current position to PowerPoint's current editor slide while preserving
  the completed visit history. Continue starts from that slide and resumes the
  existing report, visits, ownership, and elapsed time; time with the slideshow
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
  Effective global and per-device Presentation permission gates control,
  session tracking, saved-file launch, and report saves.

### Dictation and text transfer

- Dictation uses browser speech recognition when available, lets users
  edit final text, and sends it through the normal Windows text path.
- **Send text to PC** handles up to 4,096 characters. Destinations include the
  focused app, clipboard only, configured fresh document/app targets, a new
  text draft, or an email draft. Windows focus determines the target; delivery
  to the protected host UI is refused.
- Multiline input preserves line breaks. **Send text + Enter** adds Enter only
  after complete delivery. Sending 2,000+ characters requires confirmation.
- Pending, success, timeout, and delivery failure are explicit. Drafts remain
  after failure or when clear-after-send is off.
- Up to 20 browser-local snippets of 4,096 characters have unique
  case-insensitive names and can be loaded, reordered, renamed, updated, or
  deleted. Loading never sends automatically.
- **Get text from PC** requests at most 4,096 clipboard characters only after
  explicit activation and requires the default-off host permission. Copy,
  select, cut, clear, and local snippets operate on the returned field; failed
  fetch/copy keeps retryable text.
- Managed destinations never expose executable paths, process/window IDs,
  matching rules, or clipboard content to mobile. Generated drafts expire after
  24 hours unless **Keep generated draft files** is enabled.

### Navigation and split layout

Trackpad, Keyboard, and Remote are fixed primary modes. The configurable fourth
mode is Presentation, Dictation, Send text, or Get text and defaults to
Presentation; Dictation is the fallback when Presentation capability is absent.
All tools remain directly available from Menu.

Wide landscape can show keyboard and trackpad side by side with selectable pane
order, a scrollable keyboard, fixed trackpad, optional header, and
host/per-device mode-button visibility. Volume is hidden in split mode.
