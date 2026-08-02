# Release notes

Before starting a release, add a new `## v<version>` section at the top with
concise, observable user-facing changes. `npm run release:full` and
`npm run release:draft` validate the exact target-version section and do not
create one. Keep the shared notices in
`## General notices` unchanged; the release command includes them automatically.

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
- Added clear privacy controls: Screen viewing is off by default, requires its
  Developer tools toggle and device permission, allows only one viewer, shows a
  persistent Windows viewing indicator, and stops immediately when access or
  the session ends.

## v0.8.3

- Graduated Custom screens from alpha. The Windows editor, mobile screens,
  preview, and commands are now always available subject to their existing
  action permissions.
- Removed the global alpha-features switch. Future experimental features use
  explicit, feature-owned toggles under Developer tools.
- Unsupported Custom screens store versions are now left unchanged and reported
  for recovery instead of being deleted.
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
