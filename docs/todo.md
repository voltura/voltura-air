# Voltura Air - Product TODO / Roadmap

Updated: 2026-07-05  
Scope: current `voltura/voltura-air` `main` branch plus recent completed work.

This file separates the current implemented baseline from remaining roadmap work. It is written for a solo developer building a freeware/open-source Windows utility. Prioritize adoption, reliability, trust, and visible user value over cloning every competitor.

## Status legend

- `[x]` Implemented enough to count as done on `main`.
- `[ ]` Still open.
- `Partial:` Some foundation exists, but the roadmap item is not fully done.
- `Manual:` Requires real-device/manual app testing, not just code.

---

## Already implemented baseline

### Product positioning and docs

- [x] README product promise:
  - Control your Windows PC from any phone, tablet, or modern browser.
  - No mobile app-store install required.
  - Local-first. No account. No cloud needed. No paywall.
- [x] README "Best for" section.
- [x] README "Not intended for" section.
- [x] High-level feature map in `docs/features.md`.
- [x] Protocol documentation in `docs/protocol.md`.
- [x] Pairing feedback documentation in `docs/pairing-feedback.md`.
- [x] Manual network/host selection documentation in `docs/manual-network-selection.md`.
- [x] Release packaging documentation in `docs/release.md`.
- [x] Contributing, code of conduct, security policy, license, and funding files.

### Windows host foundation

- [x] WPF tray application.
- [x] Connect page.
- [x] Devices page.
- [x] Connection page.
- [x] Preferences page.
- [x] Diagnostics page.
- [x] Light/dark/system theme support.
- [x] Per-user installer packaging.
- [x] Portable zip packaging.
- [x] NSIS DPI-awareness work.
- [x] Local web host for the mobile PWA.
- [x] WebSocket command handling.
- [x] Authenticated pairing before input dispatch.
- [x] Short-lived QR pairing token.
- [x] Stored reconnect secret hashed on the host.
- [x] Saved paired devices.
- [x] Active connection tracking.
- [x] Device disconnect/remove.
- [x] Duplicate device cleanup.
- [x] Device rename.
- [x] Device metadata: platform, browser, display mode.
- [x] Host-side capability reporting.
- [x] Host-side permission model for sleep and volume.
- [x] Global/default permission settings.
- [x] Per-device permission overrides.
- [x] WebSocket origin restrictions.
- [x] Pairing attempt rate limiting.
- [x] Protocol validation before input dispatch.
- [x] Manual network adapter selection.
- [x] Automatic network adapter selection with warnings.
- [x] Manual port setting with validation.
- [x] Automatic port fallback from preferred port.

### Mobile web foundation

- [x] React/TypeScript PWA.
- [x] Browser use without app-store install.
- [x] Home-screen install support where browsers allow it.
- [x] Saved PC profiles.
- [x] Manual host entry.
- [x] Pairing from QR photo.
- [x] Friendly pairing/reconnect feedback.
- [x] Copyable redacted diagnostics.
- [x] Troubleshooting/recovery UI.
- [x] Device rename.
- [x] PC rename.
- [x] Forget saved PC.
- [x] Refresh installed PWA cache.
- [x] Light/dark/system theme support.

---

## P1 - Make the current input experience excellent

### P1.1 Trackpad polish

- [x] Pointer speed setting.
- [x] Pointer smoothing option.
- [x] Pointer acceleration option.
- [x] Scroll acceleration option.
- [x] Scroll direction wording:
  - [x] Natural scrolling.
  - [x] Traditional scrolling.
- [x] Gesture test/debug screen.
- [x] Prevent browser page scrolling while using trackpad.
- [x] Prevent accidental text/image selection while using trackpad.
- [x] Haptic feedback where browser support allows it.
- [x] Left-handed button layout option.
- [x] Large click buttons option.
- [x] Physical left/right click buttons.
- [x] Hold left/right mouse button while moving pointer for drag/resize.
- [x] Two-finger vertical scroll.
- [x] Horizontal scroll.
- [x] Optional pinch zoom gesture.
- [x] Expanded full-screen trackpad mode.
- [x] Per saved-PC/client local trackpad preferences.
- [ ] True host-managed per-device pointer speed profile.
  - Partial: mobile stores trackpad settings by local client/saved PC, but the host device manager does not own a pointer-speed profile.
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

- [x] Live typing mode.
- [x] Buffered send mode.
- [x] Send button hidden when Live typing is enabled.
- [x] Mobile composition/IME handling foundation.
- [x] Old stored keyboard settings migration tests.
- [x] Optional function keys.
- [x] Optional arrow keys.
- [x] Optional control/shortcut row.
- [x] Shortcut button: Ctrl+C.
- [x] Shortcut button: Ctrl+V.
- [ ] Shortcut button: Ctrl+X.
- [x] Shortcut button: Ctrl+A.
- [x] Shortcut button: Ctrl+Z.
- [x] Shortcut button: Ctrl+Y.
- [ ] Shortcut button: Alt+Tab.
- [ ] Shortcut button: Shift+Alt+Tab.
- [x] Shortcut button: Win.
- [x] Shortcut button: Esc.
- [x] Button: Enter.
- [x] Button: Tab.
- [x] Button: Backspace.
- [ ] Button: Delete.
  - Partial: host supports `Delete`, but the mobile keyboard UI does not expose a Delete button yet.
- [ ] Button: Home.
  - Partial: host supports `Home`, but the mobile keyboard UI does not expose a Home button yet.
- [ ] Button: End.
  - Partial: host supports `End`, but the mobile keyboard UI does not expose an End button yet.
- [ ] Button: Page Up.
  - Partial: host supports `PageUp`, but the mobile keyboard UI does not expose a Page Up button yet.
- [ ] Button: Page Down.
  - Partial: host supports `PageDown`, but the mobile keyboard UI does not expose a Page Down button yet.
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

- [x] Split mode exists.
- [x] Split mode can be enabled/disabled from Trackpad settings.
- [x] Split mode can be enabled/disabled from Keyboard settings.
- [x] Split mode activates in landscape when wide enough.
- [x] Split mode shows keyboard and trackpad side-by-side.
- [x] Split mode hides mode tabs/status/top chrome automatically to maximize usable space.
- [x] Split mode hides volume control automatically.
- [ ] Treat tablet/landscape split mode as a signature product feature in public copy.
- [ ] Add a product screenshot/demo of split mode.
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

Goal: a user should instantly understand why the app is useful from one screenshot.

- [ ] Add new mode: "Remote" or "Couch Remote".
  - Partial: current app has trackpad, keyboard, dictation, sleep, and volume/mute controls, but not a dedicated remote mode.
- [ ] Add large controls:
  - [ ] Play / pause.
  - [ ] Previous.
  - [ ] Next.
  - [ ] Seek backward.
  - [ ] Seek forward.
  - [x] Volume down/up.
    - Partial: current UI has a volume slider, not dedicated large buttons.
  - [x] Mute.
  - [ ] Fullscreen.
  - [x] Space.
  - [x] Esc / Back.
- [ ] Add D-pad:
  - [ ] Up.
    - Partial: keyboard arrow pad exists, but not a remote-mode D-pad.
  - [ ] Down.
    - Partial: keyboard arrow pad exists, but not a remote-mode D-pad.
  - [ ] Left.
    - Partial: keyboard arrow pad exists, but not a remote-mode D-pad.
  - [ ] Right.
    - Partial: keyboard arrow pad exists, but not a remote-mode D-pad.
  - [x] Enter / OK.
    - Partial: keyboard Enter exists, but not a remote-mode OK button.
- [ ] Add utility buttons:
  - [ ] Show desktop.
  - [x] Start/Search.
    - Partial: Win key exists; not a dedicated remote search button.
  - [ ] Alt+Tab.
  - [ ] Browser Back.
- [ ] Add settings:
  - [ ] Hide dangerous power actions.
  - [ ] Choose default mode after reconnect.
  - [ ] Choose which buttons are visible.
- [ ] Add automated tests for remote button message mapping.
- [ ] Add manual tests with:
  - [ ] YouTube in browser.
  - [ ] Netflix/streaming site in browser if available.
  - [ ] VLC or Windows Media Player if available.
  - [ ] PowerPoint presentation.

### P1.5 Phone-to-PC text and clipboard transfer

Start with phone-to-PC only.

- [x] Phone-to-PC text input foundation.
  - Current keyboard mode supports live typing and buffered text send.
- [ ] Add dedicated "Paste text to PC" screen.
- [x] Add large text area on mobile.
  - Existing keyboard/dictation text areas cover this partly.
- [ ] Add send modes:
  - [ ] Paste as text.
  - [x] Type text slowly.
    - Partial: host sends Unicode input events; there is no explicit slow typing mode.
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

- [x] Add host setting/permission: enable/disable PC sleep.
- [x] Add safe action: Sleep PC.
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
- [x] Add tests/logic that unpaired devices cannot trigger power actions.
  - Authentication is required before `system.sleep` is processed.
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
  - [ ] Alt+Tab.
  - [ ] Shift+Alt+Tab.
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

- [x] Store saved PC profiles on mobile.
- [x] Store device identity on mobile.
- [x] Store device name.
- [x] Store per-saved-PC/client trackpad preferences.
- [x] Show device type/browser/display mode in host paired device records.
- [x] Rename device option.
- [x] Rename saved PC option.
- [ ] Store settings per paired device on host.
  - Partial: host stores permission overrides per paired device; mobile stores UI/input preferences locally.
- [ ] Store preferred mode per device.
- [ ] Store pointer speed per device on host.
- [ ] Store layout preference per device on host.

---

## P2 - Trust, distribution, and marketing

### P2.6 Website and public documentation

- [x] Add landing/product promise copy to README.
- [x] Add high-level feature documentation.
- [x] Add pairing/security/protocol docs.
- [x] Add release packaging docs.
- [ ] Confirm/update static product website source.
  - Note: `docs/site/index.html` was not found through the GitHub connector when checked against `main`; if the website lives outside this repo, update it separately.
- [ ] Add landing page sections:
  - [ ] What it does.
  - [ ] Why use it.
  - [ ] How pairing works.
  - [ ] Privacy/security.
  - [ ] Troubleshooting.
  - [ ] Download.
  - [ ] GitHub/source link.
  - [ ] Support development links.
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
- [ ] Add FAQ:
  - [ ] Do I need to install an app on my phone?
  - [ ] Does it work outside my home network?
  - [ ] Does it use the cloud?
  - [ ] Does it log what I type?
  - [ ] Why does Windows warn about the installer?
  - [ ] How do I remove a paired device?
  - [ ] Why can't my phone connect?
- [ ] Add privacy page:
  - [ ] No account.
  - [ ] No default analytics.
  - [ ] No cloud relay for local use.
  - [ ] Paired devices stored locally.
  - [ ] No intentional input logging.

### P2.7 Release trust

- [x] Document unsigned installer status.
- [x] Document release build/package/upload workflow.
- [x] Package portable zip.
- [x] Package NSIS installer.
- [x] Add optional support/funding links.
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

- [x] Add CONTRIBUTING quickstart.
- [x] Add security policy.
- [x] Add code of conduct.
- [x] Add MIT license.
- [x] Add funding file.
- [x] Add protocol examples.
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

1. Dedicated Couch Remote mode.
2. Add missing keyboard/navigation buttons: Ctrl+X, Alt+Tab, Shift+Alt+Tab, Delete, Home, End, Page Up, Page Down.
3. Add dedicated Paste Text to PC screen with warning, long-text confirm, snippets, and send-enter option.
4. Polish split mode as a signature feature: screenshots, website copy, tests, tablet/phone manual verification.
5. Add presentation mode.
6. Add website/FAQ/privacy page and demo assets.
7. Add release trust improvements: checksums, release notes template, known limitations.
8. Add issue templates and labels.
9. Investigate code signing.
10. Later: Wake-on-LAN, screen preview, file transfer, gyroscope mouse, gamepad mode.

---

## Suggested GitHub milestones

### Milestone 1: Reliable local control

Status: mostly done, but keep polishing.

- Trackpad polish.
- Keyboard correctness.
- Pairing/reconnect reliability.
- Manual host/network recovery.
- Security baseline.

### Milestone 2: Couch remote

Status: next strongest adoption milestone.

- Dedicated media remote.
- D-pad.
- App/window helper.
- Safe power/session controls.
- Website screenshots/demo.

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
