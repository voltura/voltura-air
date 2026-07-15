# Voltura Air - Remaining roadmap

Updated: 2026-07-15
Scope: work not implemented on the current `voltura/voltura-air` `main` branch.

This is a continuation-ready roadmap, not a feature wish list. Current
capabilities belong in [features.md](features.md); move completed work there and
remove it from this file. Prefer the earliest item that removes a real user
friction point. Do not begin experimental work while a release-blocking
connection, input, pairing, or recovery defect is open.

## How to use an item

Before starting an item, turn its deliverables, decision gates, and validation
requirements into one focused issue. Preserve the stated boundaries unless a
product decision explicitly changes them. New protocol, native, filesystem, or
network work must also satisfy the acceptance-path test gate in the repository
`AGENTS.md`.

- `[ ]` Open work.
- `Decision gate:` a question that must be answered before implementation.
- `Optional verification:` useful manual coverage; it is not a release gate by
  itself.

---

## 1. Open a URL on the PC

### 1.1 Mobile entry and safe host launch

- [ ] Add an **Open URL on PC** action reachable from the mobile Keyboard or
  Remote experience without changing the existing text-transfer flow.
  - Deliverable: a compact URL field, an explicit Open button, pending/result
    feedback, and a retryable error that never exposes raw process errors.
  - The draft must remain after validation or launch failure; a successful open
    must not also type the URL into the focused Windows application.
- [ ] Add host validation and launch support.
  - Accept only absolute `https://` and `http://` URLs after trimming. Do not
    infer or open file paths, `javascript:`, `data:`, `mailto:`, custom URI
    schemes, or executable commands.
  - Decide whether a bare domain is normalized to `https://` or rejected; use
    one documented rule in both mobile and host validation.
  - Open the validated URL through the signed-in user's default browser. Report
    accepted, denied, invalid, and native-launch-failed outcomes through the
    existing acknowledged command/result pattern.
- [ ] Add a dedicated host permission for arbitrary URL opening, default off,
  with the normal global-default and per-device override model. It must be
  separately visible from fixed host-approved application launch buttons.
- [ ] Add focused protocol/host tests for valid HTTPS and HTTP URLs, rejected
  schemes and malformed input, denied devices, and a launcher failure. Add a
  mobile test for preserving the draft and showing the returned result.

Decision gate: settle whether entering `example.com` means `https://example.com`
or is an error before exposing the field. Do not silently search a browser or
reuse the text-destination feature as a workaround.

### 1.2 Share target (only after 1.1)

- [ ] Research PWA Web Share Target support on the browsers Voltura Air supports
  and whether the host-served LAN origin can meet its installation, HTTPS, and
  service-worker requirements.
- [ ] Implement only if a shared URL can land in the same reviewed Open URL
  draft and still requires an explicit Open tap. Never auto-launch a shared URL
  on the PC.

Decision gate: record the browser-support and LAN-origin result in the issue. If
the result needs a public service, account, cloud relay, or a less secure origin
model, decline this feature.

Browser extensions are out of scope unless user research shows a repeated,
extension-specific workflow that the explicit URL action cannot cover.

---

## 2. Presentation mode

### 2.1 Dedicated presenter surface

- [ ] Add a selectable **Presentation** mode; do not describe existing generic
  keyboard shortcuts or the PowerPoint application-launch preset as this mode.
  - Deliverable: a one-screen, high-contrast, large-target control surface that
    works in portrait and landscape and retains an obvious exit back to normal
    modes.
  - Primary controls: Next, Previous, Start slideshow, End slideshow, Black
    screen, and Pointer/laser-pointer toggle where the target application
    supports it.
  - Use conventional keyboard mappings behind a small, host-acknowledged command
    set. The first version must not inspect presentation files, automate Office
    through COM, screen-scrape, or claim awareness of the current slide.
- [ ] Decide and document the first-version mappings and their target scope.
  - Default candidate mappings are Right/Space/Enter for Next, Left/Backspace
    for Previous, F5 for Start, Esc for End, and `B` for Black screen.
  - Treat White screen and a laser-pointer action as optional only after their
    mappings behave consistently in supported targets; hide unavailable actions
    rather than sending an uncertain shortcut.
- [ ] Protect presentation input with a host capability and a permission that
  follows the existing remote-input permission model. Prevent queues of repeated
  slide changes: one press produces one acknowledged command, and a disconnected
  client clearly stops sending.
- [ ] Add an optional elapsed timer with Start/Pause/Reset on the phone. It is a
  local presentation aid, not a host-synchronized meeting timer. Persist its
  simple local state only if that produces a clear resume behavior.
- [ ] Add optional vibration milestones (for example 5 minutes remaining and
  time elapsed) behind feature detection and an accessible visual/audio-neutral
  alternative. Vibration must not be required to use or understand the timer.
- [ ] Add focused tests for the command mappings, permission denial, result
  feedback, timer lifecycle, and compact portrait/landscape layout. Verify the
  whole primary path from a Presentation tap to host input injection and cleanup.

Optional verification:

- Microsoft PowerPoint in slideshow mode.
- Google Slides in a current desktop browser.
- A browser PDF viewer in presentation/full-screen mode.
- A presenter moving through slides after a temporary network interruption.

---

## 3. Custom panels and device preferences

### 3.1 Host-managed custom shortcut panels

- [ ] Define a small host-owned panel model before building UI: panel ID, display
  name, ordered buttons, opaque action ID, label, optional icon, and enabled
  state. Mobile clients receive only renderable metadata and opaque IDs; no
  command paths, arguments, or arbitrary script text leave the PC.
- [ ] Add panel management in Windows Preferences and a responsive panel picker
  in the mobile Remote experience.
  - Start with a bounded number of panels/buttons and deterministic ordering.
  - Support add, rename, reorder, disable, delete, reset, and a live mobile
    refresh after save. Define what happens to a device viewing a panel while it
    is edited or deleted.
- [ ] Support these action types, each with explicit host validation and
  capability/permission behaviour:
  - [ ] **Keystroke:** one virtual key or modifier chord from an allowlisted
    representation; no raw scan-code editor in the first version.
  - [ ] **Key sequence:** a bounded ordered list with documented inter-key timing;
    do not use it for long text entry.
  - [ ] **Type text:** reviewed Unicode text delivered through the existing safe
    input path, with a conservative length limit and no clipboard readback.
  - [ ] **Open URL:** reuse section 1's validation and permission; do not create
    a second URL launcher.
  - [ ] **Host-approved command:** reuse the existing opaque, host-configured
    executable-launch action. This must remain an administrator-of-the-host
    choice and show the current local warning on add/edit.
- [ ] Ship built-in, editable presets only after the panel model exists:
  - [ ] Media PC.
  - [ ] Browser.
  - [ ] Presentation.
  - [ ] Developer.
  - [ ] Swedish keyboard.
  - Each preset needs a reviewed list of mappings, a safe reset/reapply path,
    and a note that application-specific shortcuts can change upstream.
- [ ] Add versioned JSON export/import for panel definitions only. Import must
  preview changes, reject unknown action types and invalid IDs/URLs/lengths, and
  never import executable paths, arguments, device secrets, pairing data, or
  host permissions. Exported files must be safe to inspect and share.
- [ ] Add acceptance tests covering a saved panel rendered on mobile, a button's
  complete input/launch result path, removal while connected, invalid import,
  denied action, and reset to defaults.

Decision gate: decide whether panels are shared by all paired devices (the
recommended initial model) or need per-device assignments. Do not add
per-device panel editing until shared panels are proven insufficient.

### 3.2 Remaining per-device preferences

- [ ] Persist the last selected mobile mode per saved-PC/client profile locally,
  then restore it only when the mode is still enabled and supported by the host.
  Fall back predictably to Trackpad when it is not. Forgetting a PC removes this
  preference with its profile.
- [ ] Inventory candidate host-managed layout preferences before adding storage.
  The initial candidates are assigned shortcut panels and a host default Remote
  mode; panel layout, theme, keyboard rows, and split placement already belong
  to the local browser profile unless a concrete shared-device need says
  otherwise.
- [ ] Add each approved preference to the settings schema, capability snapshot,
  device UI, and tests for a clean profile, reconnect, reset, revoked device,
  and host downgrade/unsupported capability behaviour.

---

## 4. Public documentation and release trust

### 4.1 Public site and README

- [ ] Capture and publish a short, silent-captioned demo video or GIF sequence
  from a disposable isolated test host. It must show QR pairing, cursor movement,
  typing, Split mode, and Remote mode without visible pairing tokens, secrets,
  personal files, notifications, or third-party copyrighted video.
- [ ] Add a concise comparison table to the public site/README using verifiable,
  non-marketing claims: local-network operation, account/cloud relay, supported
  host OS, mobile installation model, and intended use. Name alternatives only
  after checking their current documentation; do not imply endorsement or make
  unverified security or latency claims.
- [ ] Add an FAQ that answers the questions support will actually receive:
  supported Windows and browser versions, same-network requirement, QR pairing
  and reconnect, device removal, permissions, diagnostics, manual host entry,
  unsigned-download warnings, and how to recover after a network or host change.
- [ ] Convert the existing privacy/trust copy into a standalone privacy page
  only if it needs legal contact, analytics/cookie disclosure, data-retention
  detail, or information that cannot stay accurately concise on the product
  page. Link it from the footer and keep it aligned with the current product.
- [ ] Add a short README roadmap link to this file, labelled as planned work and
  without promising dates.

### 4.2 Release integrity and communication

- [ ] Generate one `SHA256SUMS.txt` file from the final portable ZIP, default
  installer, and full installer after packaging. Upload it to every GitHub
  release alongside the assets and document a PowerShell verification command.
  The workflow must fail if the manifest omits, duplicates, or mismatches an
  uploaded asset.
- [ ] Add a release-notes template with: highlights, fixed issues, upgrade or
  compatibility notes, known limitations, security/privacy changes, asset names,
  checksum instructions, unsigned/signed status, and a concise manual test
  boundary. The workflow may use the template, but release notes still require
  a human review.
- [ ] Add a maintained known-limitations section to every release. Start from
  facts already documented: Windows 11 host only, LAN-only use, no wake after
  sleep/shutdown, no remote desktop/file sync, and unsigned assets until signing
  is introduced. Do not use it to hide reproducible defects.
- [ ] Design a Windows-host update notification that preserves the local-first
  product promise: no automatic binary download, execution, or forced update in
  the first version. Decide whether checking is manual, opt-in periodic, or
  disabled by default; show the checked version, source, failure state, and an
  explicit browser link to the official release.
- [ ] Investigate code-signing as a documented decision, including certificate
  type, organization identity checks, annual cost, private-key storage, CI
  signing path, timestamping, renewal, revocation, and who owns the account.
- [ ] When a signing process is approved, sign and timestamp the host EXE/DLL,
  native cursor watchdog, both installers, and any distributed executable
  payloads. Add packaging verification for every signed asset and update all
  public unsigned-download wording in the same change.
- [ ] Consider auto-update only after checksum publication, signed assets,
  understandable update notification, rollback/failure behaviour, and a
  privacy review are in place. A background downloader/updater is not an
  acceptable substitute for these prerequisites.
- [ ] Consider Microsoft Store distribution only after a written maintenance
  owner, package/update path, signing requirements, Store policy review, and a
  reason it benefits users beyond the existing release channel.

### 4.3 Contribution intake

- [ ] Replace the existing generic bug template with an issue form or structured
  template that captures host version, mobile browser/device, connection method,
  reproducible steps, expected/actual result, redacted diagnostics, and whether
  the problem reproduces after reconnecting. Warn explicitly against sharing QR
  tokens, reconnect secrets, or private network details.
- [ ] Add a feature-request form that asks for the real workflow, current
  workaround, affected device/browser, expected value, and why an existing mode
  does not cover it. It must not imply a commitment to implement.
- [ ] Add a connection-problem form that captures safe, redacted network and
  pairing facts and routes suspected vulnerabilities to `SECURITY.md` instead of
  public issues.
- [ ] Add repository issue configuration linking users to discussions/support
  documentation where applicable, then create and document the labels: `good
  first issue`, `pairing`, `keyboard`, `trackpad`, `website`, `security`, and
  `documentation`. Label creation is a GitHub repository setting and needs the
  appropriate maintainer permission.

---

## 5. Research-gated experiments

Do not schedule these into a release milestone until the stated decision gate
has a written outcome and a prototype proves the primary user path. All remain
LAN-only unless a separate product decision changes the threat model.

### 5.1 Wake-on-LAN

- [ ] Resolve the fundamental sender problem before implementing UI: when the
  target PC is asleep, the Windows host is not running, and a browser cannot
  send raw UDP magic packets. Evaluate a router/NAS integration, a separately
  installed always-on LAN relay, or a deliberately unsupported/manual setup.
- [ ] If a viable sender exists, define a saved-PC profile containing a validated
  MAC address, broadcast/unicast target, and optional relay configuration. Do
  not expose the action unless the profile is complete and explicitly enabled.
- [ ] Send the standard magic packet only after an explicit mobile confirmation;
  report that packet dispatch is not proof the PC woke. Never claim Voltura Air
  can wake every PC or recover its own network connection after sleep.
- [ ] Document BIOS/UEFI, NIC driver, Ethernet/Wi-Fi, subnet/VLAN, router, and
  Windows power-state prerequisites, plus a troubleshooting check list.

Decision gate: choose an always-available sender that does not turn the normal
host into a competing second host or introduce an internet relay. If none meets
the privacy and maintenance bar, keep Wake-on-LAN out of the product.

### 5.2 Screen preview

- [ ] Produce a time-boxed technical design comparing Windows Graphics Capture
  with other supported Windows capture APIs, including multi-monitor selection,
  protected-content behaviour, cursor handling, GPU/CPU cost, encoder choice,
  LAN bandwidth, latency, and browser decode support.
- [ ] Decide explicitly between a user-requested still image, a low-FPS preview,
  or a separate remote-desktop product. Start with the smallest option that has
  a clear use case; do not promise interactive remote-desktop latency.
- [ ] Require host-local opt-in for each capture session, a persistent visible
  host/tray privacy indicator naming the captured display, a clear mobile stop
  action, and automatic stop on disconnect/host exit. Never start capture merely
  because a device reconnects.
- [ ] Authenticate and authorize the preview separately, keep it LAN-only, bound
  concurrent viewers, frame rate, resolution, memory, and bandwidth, and avoid
  persisting frames. Add tests for denied capture, consent withdrawal,
  disconnect cleanup, and capture API failure.

### 5.3 Explicit phone-to-PC file send

- [ ] Design a separate authenticated upload path; do not put arbitrary file
  bytes into the current bounded WebSocket message protocol. Establish file-size,
  total-storage, file-count, filename, timeout, cancellation, and cleanup limits
  before UI work.
- [ ] Start with one explicitly selected file from phone to a dedicated
  user-visible download folder. The host must show a confirmation with safe file
  name, size, and source device before finalizing it, use collision-safe names,
  and never auto-open, execute, synchronize, or upload received files elsewhere.
- [ ] Preserve Windows security provenance where feasible and explain that local
  antivirus/Defender protection is not a guarantee. Reject dangerous path input
  and write only beneath the configured folder.
- [ ] Add primary-path tests for accepted upload, size/quota rejection,
  cancellation, disconnect, write failure, malicious filenames/path traversal,
  and cleanup of partial files. Document the limitations and supported browsers.

### 5.4 Motion or air mouse

- [ ] Research the current Device Orientation/Motion standards and current
  Android, iOS/iPadOS, and desktop-browser requirements, including secure-context
  and user-activation permission behaviour on Voltura Air's LAN origin.
- [ ] Prototype only behind an experimental flag with a visible start/stop,
  per-session permission request, dead zone, sensitivity, calibration, and a
  reliable fallback to Trackpad. Do not collect, log, or transmit sensor data
  while inactive.
- [ ] Test calibration, orientation change, focus/background loss, permission
  denial, disconnect, and high-motion input bounds. Keep the feature
  experimental until it is comfortable and predictable across representative
  supported devices.

### 5.5 Gamepad mode

- [ ] Investigate a basic touch-controller layout separately from Windows gamepad
  injection. Browser buttons alone are not a virtual Xbox/DirectInput controller.
- [ ] Compare Windows injection options, required drivers, signing/distribution,
  anti-cheat and compatibility constraints, install/elevation impact, and
  uninstall/recovery behaviour. Do not add a kernel driver or third-party input
  dependency without an explicit product and security decision.
- [ ] If a safe path is chosen, prototype one controller profile with bounded
  input rates, disconnect-to-neutral cleanup, and a visible experimental label.
  Do not claim gaming-grade latency, vibration, or broad game compatibility
  before measured, reproducible validation.

### 5.6 Native mobile apps

- [ ] Record the exact PWA limitation first (for example a browser permission,
  a missing platform API, installation/discovery friction, or a reliability
  defect) and show why a standards-based web change cannot solve it.
- [ ] Compare a thin wrapper with separate native clients for secure networking,
  device APIs, distribution, update policy, privacy disclosures, accessibility,
  test matrix, crash reporting, and long-term owner/cost.
- [ ] Start native work only with a written maintenance commitment for both iOS
  and Android or a deliberate decision to support one platform. Keep protocol,
  pairing, permissions, and LAN-only trust guarantees consistent with the web
  client.

---

## Recommended continuation order

1. Implement **Open URL on the PC** through the safe, permissioned path in 1.1.
2. Build **Presentation mode** with fixed, tested first-version mappings.
3. Define and implement the shared **custom shortcut-panel** model; finish the
   dependent per-device preference decisions afterward.
4. Complete the low-risk public-trust work: FAQ/README roadmap link, structured
   issue forms, release-note template, known limitations, and checksums.
5. Make and record the code-signing decision; only then design the update
   notification around the agreed distribution trust model.
6. Treat each section 5 idea as an individual research issue, beginning with the
   Wake-on-LAN sender decision rather than a misleading mobile button.

## Suggested milestones

### Milestone 1: Productive control

- Safe, permissioned Open URL action.
- Dedicated Presentation mode.
- Shared custom shortcut panels and presets.
- Last-mode restore and only the per-device preferences justified by the panel
  design.

### Milestone 2: Public trust and release hygiene

- Demo asset, FAQ, accurate comparison copy, and README roadmap link.
- Structured issue intake and maintained labels.
- SHA256 manifest, release-note template, and known limitations on releases.
- Written code-signing decision and, if approved, verified signing rollout.

### Milestone 3: Evidence-led experiments

- Completed decision/prototype documents for Wake-on-LAN, screen preview, file
  send, motion input, gamepad injection, and native clients.
- Only experiments whose privacy, maintenance, and primary-path validation gates
  pass become separately scheduled features.
