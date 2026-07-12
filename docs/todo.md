# Voltura Air - Product TODO / Roadmap

Updated: 2026-07-05  
Scope: current `voltura/voltura-air` `main` branch plus recent completed work. This file should track implemented behavior in the repository, not stale PR or connector state.

This file separates the current implemented baseline from remaining roadmap work. It is written for a solo developer building a freeware/open-source Windows utility. Prioritize adoption, reliability, trust, and visible user value over cloning every competitor.

## Status legend

- `[x]` Implemented enough to count as done on `main`.
- `[ ]` Still open.
- `Partial:` Some foundation exists, but the roadmap item is not fully done.
- `Manual:` Requires real-device/manual app testing, not just code.

---

## P1 - Make the current input experience excellent

### P1.1 Trackpad polish

- [ ] Verify pinch zoom in common apps:
  - [ ] Chrome/Edge. Manual.
  - [ ] Photos. Manual.
  - [ ] PDF viewer. Manual.
  - [ ] Office/PowerPoint. Manual.
- [ ] Test on:
  - [ ] iPhone Safari. Manual.
  - [ ] Android Chrome. Manual.
  - [ ] iPad Safari. Manual.
  - [ ] Android tablet Chrome. Manual.
  - [ ] ChromeOS browser. Manual.

### P1.2 Keyboard correctness

- [ ] Add Swedish keyboard layout verification.
  - Partial: Unicode text injection should help normal Swedish text, but this still needs explicit test cases and real app verification.
- [ ] Add AltGr support tests.
- [ ] Add dead-key support tests.

- [ ] Add compact vs full keyboard controls.
  - Partial: current settings can show/hide function, control, arrow, and sleep buttons; there is no single compact/full preset.

- [ ] Improve wording/help text for:
  - [ ] Live typing.
  - [ ] Buffered send.
  - [ ] Function keys.
  - [ ] Split mode.

### P1.3 Split mode as signature feature

- [ ] Ensure split mode works well in landscape on phones. Manual.
- [ ] Ensure split mode works well on tablets. Manual.
- [ ] Add setting: left/right trackpad placement.
- [ ] Add setting: show/hide mode buttons in split mode.
  - Partial: currently hidden automatically, not user-configurable.
- [ ] Add setting: show/hide status row in split mode.
  - Partial: currently hidden automatically, not user-configurable.
- [ ] Add tests for split-mode layout breakpoints.
- [ ] Add copy:
  - [ ] "Use a tablet like a full couch keyboard and trackpad."

---

## P1 - Add visible adoption features

### P1.4 Couch / TV remote mode

- [ ] Add utility buttons:
  - [ ] Show desktop.
- [ ] Add settings:
  - [ ] Hide dangerous power actions.
  - [ ] Choose which buttons are visible.
- [ ] Add manual tests with:
  - [ ] YouTube in browser.
  - [ ] Netflix/streaming site in browser if available.
  - [ ] VLC or Windows Media Player if available.
  - [ ] PowerPoint presentation.

### P1.5 Phone-to-PC text and clipboard transfer

Start with phone-to-PC only.

- [ ] Add dedicated "Paste text to PC" screen.
- [ ] Add send modes:
  - [ ] Paste as text.
  - [ ] Send Enter after paste.
- [ ] Add safety:
  - [ ] Show target warning: "Text goes to the focused app on your PC."
  - [ ] Confirm before sending very long text.
- [ ] Add common snippets:
  - [ ] Email address.
  - [ ] URL.
  - [ ] One-time code.
- [ ] Add optional setting to clear text after send.
  - Partial: buffered keyboard send currently clears after sending.
- [ ] Later:
  - [ ] Optional PC-to-phone clipboard read behind explicit host permission.
  - [ ] Optional clipboard sync toggle.
- [ ] Tests:
  - [ ] Notepad.
  - [ ] Browser address bar.
  - [ ] Login/password field.
  - [ ] Teams/Slack text input.
  - [ ] Apps that block paste but allow typing.

### P1.6 Power and session controls

- [ ] Add safe action: Lock PC.
- [ ] Add safe action: Turn off display if feasible.
- [ ] Add dangerous actions with hold-to-confirm:
  - [ ] Restart PC.
  - [ ] Shut down PC.
  - [ ] Sign out.
- [ ] Add mobile confirmation UI for power actions.
  - Partial: Sleep button exists behind host capability/permission, but no confirmation screen.
- [x] Add host-side confirmation/allowlist setting.
  - Partial: host-side permission/capability exists; there is no separate confirmation prompt.
- [x] Add docs explaining how to disable power controls.
  - Partial: protocol/docs describe capability/permission behavior, but a user-facing power-controls doc could still be clearer.

---

## P2 - Presentation and productivity

### P2.1 Presentation mode

- [ ] Add mode: "Presentation".
- [ ] Add controls:
  - [ ] Next slide.
    - Partial: keyboard arrow/space/enter may work in some presentation apps.
  - [ ] Previous slide.
    - Partial: keyboard arrow keys may work in some presentation apps.
  - [ ] Start slideshow.
  - [x] End slideshow / Esc.
    - Partial: Esc exists in keyboard mode, not dedicated presentation mode.
  - [ ] Black screen.
  - [ ] White screen if useful.
  - [ ] Pointer mode.
- [ ] Add optional timer.
- [ ] Add optional vibration on timer milestone where browser support allows it.
- [ ] Add large no-look buttons.
- [ ] Add manual tests:
  - [ ] PowerPoint.
  - [ ] Google Slides.
  - [ ] Browser PDF presentation.
- [ ] Add website use case:
  - [ ] "Forgot your presenter remote? Use your phone."

### P2.2 App and window helper

- [ ] Add shortcuts:
  - [ ] Show desktop.
  - [ ] Close window.
  - [ ] Minimize window.
  - [x] Open Start menu.
    - Partial: Win key exists.
  - [x] Open Windows Search.
    - Partial: Win key exists; no dedicated search button.
- [ ] Add browser shortcuts:
  - [ ] New tab.
  - [ ] Close tab.
  - [ ] Reopen closed tab.
  - [ ] Next tab.
  - [ ] Previous tab.
  - [ ] Reload.
  - [ ] Fullscreen.
- [ ] Add optional host-configured app launch buttons:
  - [ ] Browser.
  - [ ] Spotify.
  - [ ] VLC.
  - [ ] PowerPoint.
  - [ ] Custom path/command.
- [ ] Require explicit host enablement for custom commands.

### P2.3 Link sharing

- [ ] Add "Open URL on PC" feature.
- [ ] Validate URL before sending.
- [ ] Add manual text URL entry on mobile.
- [ ] Add QR-friendly demo:
  - [ ] Open website from phone on PC.
- [ ] Later:
  - [ ] PWA share target support if practical.
  - [ ] Browser extension only if there is strong demand.

---

## P2 - Customization

### P2.4 Custom shortcut panels

- [ ] Add user-defined shortcut buttons.
- [ ] Button action types:
  - [ ] Keystroke.
  - [ ] Key sequence.
  - [ ] Type text.
  - [ ] Open URL.
  - [ ] Host-approved command.
- [ ] Add built-in presets:
  - [ ] Media PC.
  - [ ] Browser.
  - [ ] Presentation.
  - [ ] Developer.
  - [ ] Swedish keyboard.
- [ ] Add import/export JSON.
- [ ] Add reset to defaults.
- [ ] Add tests for serialization and old settings migration.
- [ ] Add warning for host-approved commands.

### P2.5 Per-device profiles

- [ ] Store settings per paired device on host.
  - Partial: host stores permission overrides and pointer speed per paired device; mobile stores remaining UI/input preferences locally.
- [ ] Store preferred mode per device.
- [ ] Store layout preference per device on host.

---

## P2 - Trust, distribution, and marketing

### P2.6 Website and public documentation

- [ ] Add demo GIF/video:
  - [ ] QR scan.
  - [ ] Cursor move.
  - [ ] Typing.
  - [ ] Split mode.
  - [ ] Media remote.
- [ ] Add comparison table:
  - [ ] Voltura Air.
  - [ ] Unified Remote.
  - [ ] Remote Mouse.
  - [ ] KDE Connect.
  - [ ] Chrome Remote Desktop.
- [ ] Add full public FAQ.
  - Partial: `docs/site/index.php` has a connection FAQ covering network reachability, stale QR codes, changed IP/port, VPN/guest Wi-Fi, diagnostics, and stale home-screen app shells. It does not yet cover every product/privacy/device-management question.
- [ ] Add full privacy page.
  - Partial: `docs/site/index.php` includes local-network privacy copy. A standalone maintained privacy page is still open if the public site needs one.

### P2.7 Release trust

- [ ] Investigate code-signing certificate cost/options.
- [ ] Add signed installer when practical.
- [ ] Add SHA256 checksums to releases.
- [ ] Add release notes template.
- [ ] Add "known limitations" section per release.
- [ ] Add update notification in host.
- [ ] Later:
  - [ ] Auto-update.
  - [ ] Microsoft Store distribution.

### P2.8 Open-source growth

- [ ] Add issue templates:
  - [ ] Bug report.
  - [ ] Feature request.
  - [ ] Connection problem.
- [ ] Add labels:
  - [ ] good first issue.
  - [ ] pairing.
  - [ ] keyboard.
  - [ ] trackpad.
  - [ ] website.
  - [ ] security.
  - [ ] docs.
- [ ] Add architecture overview diagram.
- [ ] Add "roadmap" section that links to this TODO.

---

## P3 - Future / experimental

### P3.1 Wake-on-LAN

- [ ] Add setting to store MAC address.
- [ ] Add WOL packet sender.
- [ ] Add "Wake PC" button on saved PC profile.
- [ ] Add docs:
  - [ ] BIOS/UEFI requirement.
  - [ ] Network adapter requirement.
  - [ ] Windows power settings.
- [ ] Add clear warning that WOL depends on hardware/network setup.

### P3.2 Screen viewer

Do not start until core features are solid.

- [ ] Research feasibility:
  - [ ] Capture API.
  - [ ] Encoding.
  - [ ] Browser display latency.
  - [ ] Privacy warning.
  - [ ] Local network bandwidth.
- [ ] Decide if feature should be:
  - [ ] Still image refresh.
  - [ ] Low-FPS screen preview.
  - [ ] Full remote desktop.
- [ ] Add explicit opt-in.
- [ ] Add red privacy warning in host.
- [ ] Avoid internet relay.

### P3.3 File transfer

- [ ] Start with small explicit sends only.
- [ ] Consider phone-to-PC file upload.
- [ ] Consider PC download folder.
- [ ] Add file size limits.
- [ ] Add malware/security warning.
- [ ] Avoid automatic sync.

### P3.4 Gyroscope / air mouse

- [ ] Prototype with browser device motion APIs.
- [ ] Check iOS permission requirements.
- [ ] Check Android behavior.
- [ ] Add calibration screen.
- [ ] Treat as experimental until it feels good.

### P3.5 Gamepad mode

- [ ] Add basic virtual controller layout.
- [ ] Evaluate Windows input injection limitations.
- [ ] Avoid promising serious gaming quality unless latency and compatibility are proven.

### P3.6 Native mobile apps

Only consider if PWA limitations block important features.

- [ ] Identify exact limitation first:
  - [ ] Physical volume buttons.
  - [ ] Background reconnect.
  - [ ] Share target.
  - [ ] Clipboard access.
  - [ ] Device motion permissions.
- [ ] Decide whether a thin native wrapper is enough.
- [ ] Avoid maintaining full separate native apps unless adoption justifies it.

---

## Recommended next issue order

3. Add dedicated Paste Text to PC screen with warning, long-text confirm, snippets, and send-enter option.
4. Polish split mode as a signature feature: tests, tablet/phone manual verification, and optional layout settings.
5. Add presentation mode.
6. Add website/FAQ/privacy page and demo assets.
7. Add release trust improvements: checksums, release notes template, known limitations.
8. Add issue templates and labels.
9. Investigate code signing.
10. Later: Wake-on-LAN, screen preview, file transfer, gyroscope mouse, gamepad mode.

---

## Suggested GitHub milestones

### Milestone 3: Productivity

- Presentation mode.
- Dedicated text/clipboard transfer.
- Shortcut panels.
- Per-device preferences.

### Milestone 4: Public trust

- Code signing investigation.
- Release checksums.
- Privacy page.
- Troubleshooting docs.
- Update notification.

### Milestone 5: Experiments

- Wake-on-LAN.
- Screen preview.
- File transfer.
- Gyroscope mouse.
- Gamepad mode.
