# Release notes

Before starting a release, add a new `## v<version>` section at the top with
concise, observable user-facing changes. `npm run release:full` and
`npm run release:draft` validate the exact target-version section and do not
create one. Keep the shared notices in
`## General notices` unchanged; the release command includes them automatically.

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
