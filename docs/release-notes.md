# Release notes

Before starting a release, add a new `## v<version>` section at the top with
concise, observable user-facing changes. `npm run release:full` and
`npm run release:draft` validate the exact target-version section and do not
create one. Keep the shared notices in
`## General notices` unchanged; the release command includes them automatically.

## v1.0.0

- Fixed uninstall and upgrade failing when Windows camera services were using
  Phone Webcam. Setup now asks for administrator approval only when needed and
  no longer opens a terminal window for Phone Webcam maintenance.

## v0.9.9

- Fixed Remote, Kodi, Keyboard, and Custom Screen controls sending the same
  command twice from a single tap.

## v0.9.8

- Reworked Windows setup and uninstall recovery. Setup verifies the complete new
  app before replacement, failed upgrades preserve a recoverable installation,
  and interrupted uninstalls can be retried safely. Setup now also reliably
  closes a running 64-bit Voltura Air host before updating it.
- Moved **Phone Webcam** installation, repair, and removal into an optional
  installer component with an explicit administrator prompt. The Windows Phone
  webcam page now reports component status without showing a disabled
  maintenance button when the camera is ready.
- Improved Phone webcam reliability when camera permission is still opening, the
  PC does not answer a start request, or a selected replacement camera fails.
- Improved **View PC screen** startup, failure recovery, display validation, and
  mouse-wheel scrolling. A delayed stop from an earlier viewing session can no
  longer close a newer active session.
- Improved **Files on PC** so delayed pages and properties cannot replace newer
  results, and Delete or Rename always applies to the items shown when its
  confirmation dialog opened.
- Fixed held Keyboard, Remote, and Custom Screen navigation controls so repeating
  actions stop when the app loses focus without swallowing the next tap. Keyboard
  Backspace and Delete now remove complete emoji and combined characters.
- Added confirmation before forgetting a saved PC, and improved mobile behavior
  when browser storage is unavailable or full so current preferences remain usable
  for the open session.
- Strengthened the Presentation archive so saves, renames, links, and deletions
  recover safely after interruption. Corrupt, replaced, or unrelated files are
  never silently imported or deleted.
- Improved Application Log maintenance so oversized damaged records are skipped,
  partial deletion is reported accurately, and failed automatic cleanup can retry.
- Community Custom Screen accounts now require email verification with expiring
  links and resend support. Login, registration, and resend limits provide more
  consistent abuse protection without revealing whether an account exists.
- Improved Community Custom Screen upload, download, deletion, and official-library
  updates so completed downloads are counted accurately, retained ratings stay
  attached to official screens, and uncertain cleanup never guesses which file to
  delete.
- **Compatibility:** Windows permissions return to their secure defaults, network
  selection returns to automatic Direct, and Keep awake returns to Off because
  v0.9.8 uses one new atomic settings format. Review these settings after updating.
  Earlier Presentation report files are not imported into the new archive.
- **Catalog compatibility:** deploying the v0.9.8 Community Custom Screen catalog
  requires a fresh database. Existing catalog accounts, submissions, ratings, and
  reports are not migrated, so catalog users must register again after deployment.

## v0.9.7

- Improved Phone webcam camera switching so changing between available phone
  cameras keeps the active webcam session connected.
- Fixed cases where the Windows webcam video could freeze while the camera view
  continued moving on the phone, including after rotating the phone. Voltura Air
  now refreshes the active camera automatically without replacing the connection.

## v0.9.6

- Fixed an issue that could send Relay and Enhanced Direct pairing links to the
  Voltura website instead of opening Voltura Air.
- Added Phone webcam to the website feature comparison.

## v0.9.5

- Added the opt-in, video-only **Phone webcam** tool. Choose a camera on a
  paired phone and use it as `Voltura Air Webcam` in Windows applications over
  Enhanced Direct or Relay, with live camera switching and foreground recovery.
- Added a normal Windows **Phone webcam** page for enabling, removing, and
  previewing the virtual camera, plus global and per-device permissions and a
  tray action that stops the active phone immediately.
- Phone webcam over Enhanced Direct is free and unlimited. Voltura-operated
  Relay is initially free and uses the existing aggregate Data Saver and
  service cutoff limits without a webcam-specific account or quality tier.

## v0.9.4

- Custom Screen trackpads can now include a Gyro option, so you can switch
  between touch and motion control directly from your custom layout.
- Custom Screen button panels can now use up to six rows, with scrolling on
  smaller screens so every button remains easy to reach.
- Gyro mouse now supports double-clicking. Double-tap the Gyro area in Trackpad
  or Presentation to double-click. Tapping is also more reliable, with small
  hand movements less likely to move the pointer by accident.
- Improved Gyro recovery guidance when motion access is denied, including when
  the app must be fully reopened before trying again.
- Custom Screens that the loaded web app cannot display now show a clear prompt
  to refresh the app instead of remaining stuck loading.
- Fixed an issue where the Custom pointer setting could show the wrong state
  after the pointer had been turned off.
- Improved updating and uninstalling Voltura Air when the app is already running.

## v0.9.3

- Added **Gyro mouse** for supported phones and tablets using **Enhanced
  capabilities**. Point the top of the device and hold the Trackpad surface to
  move the PC cursor, or tap it for a one-handed left click. Existing Left and
  Right buttons remain available for clicking and dragging, with handedness,
  large-button, and adjustable Gyro sensitivity settings preserved.
- Open Gyro mouse directly from **Tools**, switch between Touch and Gyro in the
  main Trackpad, or use it from Presentation's embedded Trackpad. Motion data
  is processed on the device and Gyro turns off automatically when its surface
  is left, hidden, or disconnected.
- Added optional **Enhanced capabilities** for Direct connections. The primary
  QR opens Voltura Air's secure HTTPS app, while authenticated controller
  traffic travels directly over the selected private LAN. This provides the
  secure browser foundation needed by more advanced device capabilities
  without routing established controls through the cloud.
- Presentation mode can no longer be blocked by an unfinished session from
  another phone. Any authorized phone can manage breaks or save and discard the
  session; starting the same deck takes control, while starting another deck
  automatically saves the previous presentation.
- Updated the Windows **Connect** and **Connection** pages for the new secure
  Direct path. The existing local HTTP connection remains available through an
  explicit **Copy Standard Local link** action, Relay includes enhanced browser
  capabilities automatically, and pairing QR codes remain unobstructed for
  reliable scanning.
- Improved connection and pairing reliability. Opening or refreshing an
  unsuccessful hosted pairing link no longer replaces a working saved PC,
  foreground events no longer restart an in-progress secure connection, and
  input acknowledgement and health checks now follow their correct deadlines.
- Improved connection issue feedback with a complete dismissible message and
  diagnostic code while controls remain available, plus dedicated Secure
  Direct recovery guidance when its required private-LAN path cannot be made.
- Fixed saved-device removal so the app confirms that the PC durably revoked
  the pairing before deleting local credentials, including over Secure Direct.

## v0.9.2

- Added direct mouse and physical keyboard control inside **View PC screen**
  when using a browser with a mouse or trackpad. Move, click, right-click, drag,
  scroll, and type—including printable non-ASCII characters—anywhere on the
  mirrored display while keeping the existing touch, zoom, and pan controls.
  The mode respects **Pointer and keyboard** permission and safely releases
  held buttons when viewing, permission, or display state changes.
- Added the host-only **Simulate activity every 59 seconds** option under
  **Keep awake** in the Windows tray and Preferences. It remembers the user's
  choice and sends only an F15 key release without moving or clicking the
  pointer; applications may handle F15 differently, so presence results are
  not guaranteed.
- Improved **View PC screen** reliability. Direct control now covers the full
  mirrored image, a failed display switch stays on the working display, expiring
  Relay view credentials renew promptly, and switching to another mobile mode
  cleanly closes the mirror.
- Fixed Direct and Relay connection edge cases. Direct pairing links with a
  trailing slash now work, Windows-hosted web assets honor browser compression
  settings, and Relay-connected apps remain on the hosted web version instead
  of trying to refresh from the PC. Self-hosted Relay now rejects invalid port
  settings at startup.
- Improved saved-device and Custom Screen library maintenance. Duplicate saved
  pairing records no longer displace other retained devices, failed
  Custom Screen catalog import requests clean up temporary files, and
  community-library sign-out is protected against cross-site requests.

## v0.9.1

- Fixed navigation from **View PC screen** so selecting a Custom Screen,
  YouTube or Kodi remote, or gesture diagnostics now closes the live mirror and
  opens the selected tool.
- Improved the compact **View PC screen** layout by keeping the live status
  beside the controls instead of over the mirrored screen.
- Improved **Files on PC** when opening slow folders or network locations. File
  loading no longer delays other controls, survives a brief reconnect, and
  stops safely if file access is removed.

## v0.9.0

- Added **Find a setting** to Windows Preferences. Search by any part of a
  setting name, then select a result to open its section and jump directly to
  the control.
- Added a dedicated **Laser pointer** component to Custom Screens. Choose the
  default Presentation color, red, green, or blue in the Windows editor, then
  use the button from mobile to turn the pointer on, change color, or turn it
  off.

## v0.8.10

- Added a generated official Custom Screen library with 14 Windows 11 remotes
  for media, streaming, meetings, Windows controls, displays, photos, and
  Blender, plus a deterministic one-command catalog bundle and direct browsing
  of the community library from the Windows Custom Screens page.
- Added portable HTTP(S) website, known-application, and permissioned
  host/system actions to the Custom Screen editor. Restart and shutdown retain
  hold-to-confirm; sleep, hibernate, and display-off require confirmation.
- Added numpad and media shortcut keys, more reusable Windows controls, and one
  strict, exact Custom Screen JSON format. Portable packages exclude host-local
  executable actions, arbitrary commands, and device assignments. Invalid local
  Custom Screen data now opens a themed keep-or-delete recovery dialog, and
  app-dependent screens disable all controls when their target is unavailable.
- Corrected the official VLC, Plex, Zoom, Netflix, Prime Video, Disney+, and
  Twitch control maps, made Windows Photos require a usable Microsoft Photos
  handler without a folder fallback, and verified all 14 screens in portrait
  and landscape with no clipped button labels or page overflow.
- Added advisory Custom Screen validation for unsaved drafts, including real
  compact-phone portrait and landscape rendering, clipped labels, shortcuts,
  web addresses, application availability, and current permissions. Reports
  suggest resolutions and select affected controls without modifying the draft
  or preventing Save for warnings. Validation runs without administrator rights.
- Added an administrator-only atomic official-library import that updates by
  stable official ID and preserves ratings and download counters after the
  Windows 11 smoke-test matrix is confirmed. Imports are serialized and retain
  package files whenever MariaDB commit outcome is uncertain.

## v0.8.9

- Added **Files on PC**, a touch-first host file manager with one panel in
  narrow views and two independent panels whenever the screen is wide enough,
  including phones in landscape. Browse local and mapped
  drives, use Windows locations, sort and select files or folders, inspect
  properties, and open items with their Windows apps without transferring file
  content to the mobile device.
- Added direct two-panel Copy and Move, Windows clipboard Cut/Copy/Paste, safe
  Recycle Bin deletion, rename, background progress, pause/resume/cancel,
  conflict handling, reconnect recovery, and removable operation history.
- Added separate host and per-device permissions for browsing/opening and
  changing files. Protected operating-system items are hidden by default, with
  a global setting and per-device override.
- Added **View** in Files to open a document on the PC and continue into the
  independently authorized encrypted PC screen mirror. PC screen viewing now
  starts in Zoom mode for two-finger gestures.
- Improved **Third-party notices** on mobile with a readable in-app view,
  component summaries, source links, and clear loading or failure feedback.
- Gave Cloud relay connections more startup time before reporting the PC as
  unavailable, improving reliability on VPNs and managed networks that add
  connection delay. Direct local connections remain fast as before.

## v0.8.8

- Fixed live PC screen viewing in phone and tablet landscape mode, including the
  missing mirror, overlapping status and controls, and unintended page zooming.
- Added **Disallow device** under the active **Stop screen viewing** tray action,
  so the PC owner can stop the mirror and block that device from immediately
  starting it again.
- Improved host-initiated screen stopping with a menu that closes completely,
  a clear message on the viewing device, and immediate removal of stale video
  and screen input.
- Full-screen PC viewing now remains full screen when the phone or tablet is
  rotated.

## v0.8.7

- Improved relay paring connection issue feedback and error handling

## v0.8.6

- Added **Cloud relay through Voltura** as an optional connection method for
  company and guest networks that block devices from connecting directly to a
  PC. Direct local network remains the default and continues to work as before.
- Added a short `voltura.se` pairing QR for Cloud relay. It opens the hosted
  Voltura Air app, needs no Voltura account, and keeps the same trusted-device,
  reconnect, permission, and removal controls as Direct mode.
- Added end-to-end encrypted remote commands and live PC screen viewing through
  the relay. The PC and phone both connect outward, so Relay mode does not need
  an incoming Windows Firewall exception.
- Added **Standard** and **Data saver** relay screen quality choices, a monthly
  usage estimate with a used-versus-remaining allowance bar, and automatic
  safeguards that reduce or pause screen traffic before the configured relay
  allowance is exceeded. Commands remain available when screen relay is paused.
- Added an advanced custom-relay option and a portable self-hosting deployment
  for people who want to operate their own relay later.
- Improved relay screen viewing in installed iPhone and iPad web apps, including
  reliable connection when iOS has usable relay routes but keeps reporting that
  network discovery is still in progress. Also fixed Screen Start/Stop and the
  Keyboard and Trackpad shortcuts while the live mirror is open.

## v0.8.5

- Removed the Developer tools checkbox for PC screen mirroring. **View PC
  screen** now depends only on the denied-by-default global or per-device Screen
  viewing permission and a current identity-pinning pairing.
- Simplified `release:full` to package the portable ZIP and both installers once
  from the final local release commit, validate them before pushing, and recover
  automatically from an old zero-byte Git index lock.

## v0.8.4

- Added **View PC screen**, an optional encrypted live mirror for viewing and
  controlling one Windows display from a paired phone or tablet.
- Added smooth WebRTC video up to 1080p/30 fps, automatic bandwidth adjustment,
  display switching, and a separate cursor for responsive desktop interaction.
- Added pinch-to-zoom up to 5x with two-finger panning, plus compact mouse,
  keyboard, and Stop controls that make text and desktop apps practical on a
  small screen. Viewing can expand edge-to-edge in either orientation and
  restores automatically when device orientation changes.
- Added explicit **Scroll** and **Zoom** two-finger modes to the live mirror and
  regular Trackpad, eliminating accidental zoom while scrolling. Switching the
  live mirror mode preserves its current view, and the Trackpad switch appears
  when **Pinch zoom** is enabled.
- Added clear privacy controls: Screen viewing requires device permission,
  allows only one viewer, shows a
  persistent Windows viewing indicator, and stops immediately when access or
  the session ends.

## v0.8.3

- Graduated Custom screens from alpha. The Windows editor, mobile screens,
  preview, and commands are now always available subject to their existing
  action permissions.
- Removed the global alpha-features switch. Future experimental features use
  explicit, feature-owned toggles under Developer tools.
- Improved the Custom screens community library with tag-pill editing and
  display, a clearable search field, and a custom sort picker.
- Added report delivery to Voltura Air by email, with a confirmation toast that
  keeps visitors on the reported screen.
- Added email notifications for new submissions and moderation decisions,
  including the reviewer feedback sent to screen authors.
- Added a **Remove rejected** action to submission history, so authors can hide
  rejected submissions without permanently deleting their stored records.
- Administrators can now permanently delete an approved custom screen from its
  detail page as well as from the library list.

## v0.8.2

- Added versioned `.volturascreen` export and reviewed import so Custom screens
  can be backed up, shared, and added with fresh local IDs and no device
  assignments.
- Added the Custom screens community library with search, mobile-accurate
  previews, downloads, ratings, reports, moderated submissions, administrator
  deletion, reviewer feedback, and approval or rejection email.
- Added direct **Install in Voltura Air** links with a file-download fallback;
  every catalog package still opens a local review and is validated by the
  Windows host before it is saved.
- Added responsive navigation-ring and D-pad panels with repeatable directions
  and a central mini-trackpad.
- Added a tray-menu shortcut to the Custom screens community library.

## v0.8.1

- Renamed Fn button to Functions in remote screens. Also adjusted font to match Power button, both Functions and Main button.

## v0.8.0

- Graduated Presentation from alpha. PowerPoint, Google Slides, and PDF/browser
  control, session tracking, breaks, laser settings, saved-file launch, and
  reports now remain available when alpha features are disabled.
- Added Custom screens (alpha), a Windows editor for building reusable,
  responsive control surfaces and assigning them to paired phones and tablets.
- Added panels, collapsible panels, buttons, trackpads, collapsible trackpads,
  and standalone volume sliders with responsive widths, fill/content height,
  button rows and placement, optional click controls, and full-screen trackpad
  expansion.
- Added separate portrait and landscape arrangements, per-orientation
  visibility, drag-and-drop placement and reordering, adjustable editor columns,
  undo/redo, configurable delete/hide confirmations, and themed quick actions.
- Added button actions for literal text, single keys, keyboard shortcuts,
  approved applications, and curated media, navigation, browser, and Windows
  controls. Custom text and shortcut buttons use clear text labels while
  host permissions continue to govern every action.
- Added a Custom screens workspace to the mobile Menu with responsive wrapping,
  collapsible content, unavailable-action feedback, and press-and-hold repeat
  for supported controls.
- Added a read-only Custom screens Preview with fixed themed Windows controls
  for device, orientation, and rotation above a clean embedded mobile surface.
  Its device choices include the maintained phone/tablet validation sizes, and
  leaving Custom screens closes its preview windows. Previewed controls never
  invoke screen actions.
- Added privacy-safe Custom screens activity to the Application Log without
  recording typed text, shortcut payloads, executable details, or pointer
  coordinates.

## v0.7.9

- Improved the standard Windows installer so it verifies downloaded .NET 10
  runtime installers are valid Microsoft-signed files before requesting
  administrator approval.
- Added explicit restart-required handling for .NET runtime installation,
  including a restart-later default and no immediate Voltura Air launch when a
  restart is pending.
- Improved prerequisite failure handling and cleanup so setup finishes runtime
  checks before replacing an existing Voltura Air installation.
- Added early rejection of unsupported Windows architectures, NSIS-capacity
  checks for prerequisite commands, and warning-free installer compilation.
- Improved the Windows host on low-resolution displays so its title bar remains
  reachable and the full interface can be accessed with fallback scrollbars.

## v0.7.8

- Added an optional 3D effect for buttons, checkboxes, sliders, selected
  controls, and expandable sections across the Windows host and mobile app.
- Added separate appearance preferences for the Windows host and mobile
  controls. Mobile controls default to the 3D effect, and each paired device can
  inherit the global preference or override it from the Windows Devices page.
- Improved pressed, selected, disabled, and expanded control states across light
  and dark themes, while Windows High Contrast keeps the flat system treatment.
- Fixed the mobile Remote layout so it uses the available space when mode
  buttons are hidden.

## v0.7.7

- Rebuilt PowerPoint Presentation mode around direct PowerPoint automation for
  open presentations, including start from beginning/current slide, next,
  previous, first, last, numbered slide navigation, end slideshow, black screen,
  white screen, and pause/resume.
- Added open PowerPoint discovery, refresh, single-presentation auto-selection,
  multi-presentation selection, and a Focus PPT action so the selected
  presentation can be brought back to the foreground from mobile.
- Replaced the native PowerPoint laser with Voltura Air's custom laser pointer
  while keeping PowerPoint's pointer visible when possible and cleaning up the
  cursor after disconnects, slideshow closure, permission changes, or shutdown.
- Made PowerPoint timing host-authoritative, with recoverable session drafts,
  Continue presentation after interrupted slideshows, manual break ownership,
  and saved reports that keep the selected presentation name and local file link
  on the PC.
- Added break blackout and whiteout overlays that show break status, can be
  dismissed safely by local input, and resume the selected presentation from the
  current slide.
- Improved the mobile Presentation layout for portrait and landscape phones,
  including the compact trackpad states, responsive command panels, Go to slide
  dialog, consistent controls, and non-selectable app chrome.
- Improved PowerPoint error handling and logging so unavailable, busy, stale,
  invalid-slide, pointer, and automation failures are reported without falling
  back to blind keyboard input.
- Improved the Windows Presentations archive so PowerPoint reports use the
  actual presentation title when available and summary cards fit ordinary window
  sizes without clipping.
- Kodi open / activate now stable

## v0.7.6

- Automatically minimizes the Windows host to the tray after a device connects
  from the Connection screen.
- Cursor overrides now return to the Windows cursor scheme when Voltura Air
  exits unexpectedly.
- Added a reminder to email drafts that they may contain sensitive information.
- Improved foreground activation for the Windows host after startup and for the
  installer when it opens from SmartScreen.

## v0.7.5

- Added Presentation as the default fourth mobile mode for PowerPoint, Google
  Slides, and PDF/browser presentations.
- Added an integrated presentation trackpad, volume and blackout controls, and
  a native red, green, or blue laser pointer with adjustable size.
- Added live timing for presentation sessions, breaks, slides, and per-slide
  activity, with a save prompt when a presentation ends.
- Added a Windows Presentations archive with search, filters, aggregate
  statistics, detailed timelines, and session/break breakdowns.
- Added report rename, presentation file and URL links, HTML, Excel, PDF, CSV,
  and text export, plus email drafts with optional presentation attachments.
- Improved responsive mobile Presentation layouts and the consistency of
  Windows filters, dialogs, tooltips, keyboard focus, and report actions.
- Improved recovery of the normal Windows cursor after an unexpected host exit.
- Presentation remains optional and can be turned off under Developer tools.

## v0.7.4

- Redesigned the Windows Connection screen to make network setup and recovery easier to understand.
- Made it simpler to choose the Wi-Fi or LAN adapter your device can reach when pairing fails.
- Moved custom port controls into its own section and clearly shows when connection changes need a restart.
- Made Windows settings checkboxes compact and responsive so more preferences can share a row without wrapping their labels.
- Added consistent themed information buttons, tooltips, and dialogs for optional setting guidance while keeping important privacy and recovery guidance visible.
- Made the complete checkbox card clickable and ensured an information dialog accepts its first button click after regaining activation.

## v0.7.3

- Improved Windows host stability around busy and long-running operations.
- Made pairing and status updates more resilient while the host UI is under load.
- Fixed a hang that could occur when deleting application logs.
- Improved Awake reliability around queued work, timeouts, and late native completion.
- Made diagnostics refreshes safer and recoverable after a log read failure.

## v0.7.1

- Fixed an issue where deleting application logs from Diagnostics could make the Windows app stop responding and close unexpectedly.

## v0.7.0

- Added Test buttons for enabled preset and custom applications in Windows Preferences.
- Made mobile mode switching easier to discover when bottom mode buttons are hidden.
- Simplified mobile mode names to Trackpad, Keyboard, and Remote.
- Improved Application Log, startup recovery, pairing feedback, and settings-failure layouts.
- Added a clear disable-and-restart recovery when cursor protection cannot start.

## v0.6.7

- Added quicker mode switching from the Menu, including the selected fourth mode.
- Added global and per-device control over mode-button visibility.
- Improved Split mode navigation and paired-device appearance settings.
- Added confirmation before opening YouTube or Kodi on the PC.
- Made the installer window come forward more reliably.

## v0.6.6

- Improved device management, trackpad-speed controls, and per-device permissions.
- Clarified that removing paired devices requires pairing them again.
- Improved pairing and network-setup feedback.
- Added a notification explaining that Voltura Air keeps running after its window closes.
- Strengthened reconnect security so private pairing keys are not sent to the PC.
- Required devices to pair again after updating to this release.

## v0.6.5

- Added the complete local Windows control experience across trackpad, keyboard, remote, clipboard, applications, media, and power actions.
- Added landscape tablet Split mode, saved reconnects, per-device permissions, and High Contrast support.
- Added standard, offline-ready full, and portable Windows distributions.

## v0.6.4

- Published Windows release assets for Voltura Air v0.6.4.

## v0.6.3

- Published Windows release assets for Voltura Air v0.6.3.

## v0.6.2

- Published Windows release assets for Voltura Air v0.6.2.

## v0.6.1

- Published Windows release assets for Voltura Air v0.6.1.

## v0.6.0

- Published Windows release assets for Voltura Air v0.6.0.

## v0.5.0

- Published Windows release assets for Voltura Air v0.5.0.

## v0.4.0

- Published Windows release assets for Voltura Air v0.4.0.

## v0.3.0

- Published Windows release assets for Voltura Air v0.3.0.

## v0.2.0

- Published Windows release assets for Voltura Air v0.2.0.

## v0.1.0

- Published the first Windows release assets for Voltura Air.

## General notices

Voltura Air is free software from Voltura AB. If it helps you, optional support is available through [Ko-fi](https://ko-fi.com/voltura) or [PayPal](https://www.paypal.me/voltura).

Release binaries are not code-signed. Windows may show an unknown-publisher or Microsoft Defender SmartScreen warning. Download release files only from the official Voltura Air website or GitHub release page.
