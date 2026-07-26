# Product capabilities

Current user-visible capabilities, permissions, limits, states, and guarantees.
Installation and connection: [README](../README.md#download-and-install).
Development: [setup](setup.md). Wire detail: [protocol](protocol.md).

## Scope and guarantees

- A Windows 11 host serves a phone/tablet PWA on the same Wi-Fi or LAN.
- Normal use needs no mobile app-store install, account, subscription, trial,
  cloud relay, or internet input-forwarding service.
- Voltura Air is not remote desktop, file sync, backup, notification sync, or a
  cloud clipboard.
- The client cannot control or wake a sleeping, shut-down, or unreachable PC.
- One host runs per signed-in Windows user. A second launch focuses it.

## Windows host

### Shell, connection, and pairing

- The Windows tray app provides Connect, Devices, Presentations, Connection,
  Preferences, and Diagnostics. Closing the window leaves the host running.
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
  client. Pairing, reconnect, and commands are authenticated, bounded, and
  validated.

### Devices, permissions, and settings

- Devices shows name, platform/browser metadata, connection/activity state, and
  per-device settings. Users can rename/remove one device or remove all.
- Global defaults combine with per-device overrides for pointer speed,
  permissions, and mode-button visibility.
- Host permissions cover sleep, volume, Presentation, application launch, web
  addresses, PC clipboard reads, Lock, Blackout, display off, screen saver,
  sign out, restart, shutdown, Keep awake, and interaction with the host UI.
- Unsupported actions are omitted; host-disabled actions explain the relevant
  permission. Manually sent unauthorized commands are rejected.
- **Enable alpha features** defaults on. Explicit off removes Presentation
  capability and blocks commands/saves while keeping existing reports readable.
- Browser, Spotify, VLC, PowerPoint, and custom executable buttons are
  configured and tested locally. Mobile receives only an opaque action ID and a
  1–10-character label; paths and arguments stay on the PC.
- Custom executable add/edit requires a local warning confirmation and each
  launch revalidates its target.
- Custom pointer is host-wide, off by default, and configurable by size/color.
  Paired devices may toggle it. Cursor overrides return to the configured
  Windows scheme when Voltura Air exits.

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
  left/right buttons; held-button drag; two-axis scroll; optional pinch zoom.
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

### Presentation (alpha, enabled by default)

- The fourth mode controls PowerPoint, Google Slides, or PDF/browser
  presentations. PowerPoint control enumerates open presentations and a
  host-derived, deduplicated list of still-existing PowerPoint files linked by
  saved reports. The mobile chooser receives opaque IDs, titles, and filenames,
  never local paths. Choosing a saved file explicitly starts PowerPoint when
  necessary, opens that exact host-validated path, starts its slideshow, and
  begins the authoritative session. Ordinary discovery and control commands
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
- Saved reports stay in the signed-in user's local application data. Starting
  the host with alpha features off does not create the PowerPoint automation
  worker. Alpha-setting changes require a host restart to apply automation
  resource changes; disabling immediately hides/blocks new controls and saves
  while preserving archive access.

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
