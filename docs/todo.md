# Voltura Air - TODO / Roadmap

Updated: 2026-07-14
Scope: work that is not yet implemented on the current `voltura/voltura-air` `main` branch.

This file tracks remaining product, quality, documentation, and distribution work only. Implemented capabilities belong in [features.md](features.md) and should be removed from this roadmap when completed.

Prioritize reliability, usability, trust, and visible user value. Manual and real-device verification listed here is optional unless it is needed to reproduce a bug or make a specific release decision.

## Status legend

- `[ ]` Open work.
- `Optional verification:` Useful manual coverage that is not a release requirement by itself.

---

## 3. Text, clipboard, and link transfer

### 3.1 Configured text destination

- [ ] Add a host preference that can activate or start a locally approved target application before text delivery.
- [ ] Detect suitable installed desktop applications and allow a separately approved custom executable.
- [ ] Keep paths, arguments, process IDs, and window matching rules inside the Windows host.
- [ ] Add bounded activation/startup timeouts and elevation-mismatch guidance.
- [ ] Preserve the existing focused-application path as the default.

### 3.2 Clipboard expansion

- [ ] Investigate PC-to-phone clipboard reading behind explicit host permission.
- [ ] Consider an optional clipboard synchronization mode only if it can remain predictable and secure.

### 3.3 Open URL on PC

- [ ] Add Open URL on PC.
- [ ] Validate and normalize URLs before opening them.
- [ ] Add manual URL entry on mobile.
- [ ] Consider PWA share-target support if browser support and complexity are acceptable.
- [ ] Consider a browser extension only if there is clear demand.

---

## 4. Presentation mode

- [ ] Add a dedicated Presentation mode.
- [ ] Add large no-look controls for:
  - [ ] Next slide.
  - [ ] Previous slide.
  - [ ] Start slideshow.
  - [ ] End slideshow.
  - [ ] Black screen.
  - [ ] White screen, if useful.
  - [ ] Pointer mode.
- [ ] Add an optional presentation timer.
- [ ] Add optional vibration at timer milestones where supported.
- [ ] Add website copy for using a phone as an emergency presenter remote.

Optional verification:

- Microsoft PowerPoint.
- Google Slides.
- Browser PDF presentation mode.

---

## 5. Customization and device preferences

### 5.1 Custom shortcut panels

- [ ] Add user-defined shortcut buttons.
- [ ] Support action types for:
  - [ ] Keystroke.
  - [ ] Key sequence.
  - [ ] Type text.
  - [ ] Open URL.
  - [ ] Host-approved command.
- [ ] Add built-in presets for:
  - [ ] Media PC.
  - [ ] Browser.
  - [ ] Presentation.
  - [ ] Developer.
  - [ ] Swedish keyboard.
- [ ] Add JSON import and export.
- [ ] Add reset to defaults.
- [ ] Add clear warnings for host-approved commands.

### 5.2 Remaining per-device preferences

- [ ] Store the preferred mobile mode per paired device.
- [ ] Store host-managed layout preferences per paired device.

---

## 6. Public project and release trust

### 6.1 Website and public documentation

- [ ] Add a demo GIF or video covering:
  - [ ] QR scan.
  - [ ] Cursor movement.
  - [ ] Typing.
  - [ ] Split mode.
  - [ ] Remote mode.
- [ ] Add a comparison table covering relevant alternatives without overstating Voltura Air.
- [ ] Expand the public FAQ to cover privacy, permissions, device management, installation, and common recovery cases.
- [ ] Add a standalone privacy page if the public site needs one.
- [ ] Add a README roadmap section that links to this file.

### 6.2 Release trust and distribution

- [ ] Add SHA256 checksums to releases.
- [ ] Add a release-notes template.
- [ ] Add a known-limitations section to each release.
- [ ] Add an update notification in the Windows host.
- [ ] Investigate code-signing certificate cost and maintenance.
- [ ] Sign installer and executable assets when practical.
- [ ] Consider auto-update only after update notification and release integrity are reliable.
- [ ] Consider Microsoft Store distribution only if maintenance and signing requirements are justified.

### 6.3 Open-source project setup

- [ ] Add issue templates for:
  - [ ] Bug report.
  - [ ] Feature request.
  - [ ] Connection problem.
- [ ] Add and maintain labels for:
  - [ ] Good first issue.
  - [ ] Pairing.
  - [ ] Keyboard.
  - [ ] Trackpad.
  - [ ] Website.
  - [ ] Security.
  - [ ] Documentation.

---

## 7. Future and experimental work

Do not start these items until the core input, connection, and remote experience is stable.

### 7.1 Wake-on-LAN

- [ ] Add a setting to store a PC MAC address.
- [ ] Add a Wake-on-LAN packet sender.
- [ ] Add a Wake PC action to saved PC profiles.
- [ ] Document BIOS or UEFI, network adapter, and Windows power requirements.
- [ ] Explain clearly that Wake-on-LAN depends on hardware and network configuration.

### 7.2 Screen preview

- [ ] Research Windows capture APIs, encoding, latency, browser rendering, privacy, and LAN bandwidth.
- [ ] Decide between still-image refresh, low-FPS preview, or a broader remote-desktop feature.
- [ ] Require explicit opt-in and prominent host-side privacy indication.
- [ ] Keep the feature local-network only.

### 7.3 File transfer

- [ ] Start with small, explicit phone-to-PC sends only.
- [ ] Define a safe PC download folder.
- [ ] Add file-size limits.
- [ ] Add malware and security warnings.
- [ ] Avoid automatic file synchronization.

### 7.4 Gyroscope or air mouse

- [ ] Prototype browser device-motion input.
- [ ] Check iOS permission requirements.
- [ ] Check Android browser behavior.
- [ ] Add calibration.
- [ ] Keep the feature experimental until it is reliable.

### 7.5 Gamepad mode

- [ ] Prototype a basic virtual controller layout.
- [ ] Evaluate Windows controller-input injection options and limitations.
- [ ] Avoid promising gaming-grade latency or compatibility until proven.

### 7.6 Native mobile apps

- [ ] Identify the exact PWA limitation that would justify native work.
- [ ] Evaluate whether a thin native wrapper is sufficient.
- [ ] Avoid separate full iOS and Android apps unless adoption clearly justifies the maintenance cost.

---

## Recommended next issue order

1. Add Presentation mode.
2. Add an optional host-configured text destination.
3. Expand the public FAQ, privacy information, and demo assets.
4. Add release checksums, release-note structure, and known limitations.
5. Add issue templates and project labels.
6. Investigate code signing.
7. Evaluate future features only after the core roadmap is stable.

---

## Suggested milestones

### Milestone 2: Productivity

- Configured text destinations and link actions.
- Presentation mode.
- Custom shortcut panels.
- Remaining per-device preferences.

### Milestone 3: Public trust

- Website demos and expanded public documentation.
- Release checksums and release-note structure.
- Update notification.
- Code-signing investigation.
- Issue templates and labels.

### Milestone 4: Experiments

- Wake-on-LAN.
- Screen preview.
- File transfer.
- Gyroscope or air mouse.
- Gamepad mode.
- Native mobile apps.
