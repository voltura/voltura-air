# Voltura Air candidate directions

These ideas need a product decision or evidence before moving to
[todo.md](todo.md). Each implementation needs explicit ownership, limits,
privacy/security review, recovery behavior, and proportionate validation.

## Control and personalization

### Mobile app gaps

Mouse and keyboard control, media playback controls, Files for Windows PC or
mapped-drive storage, and one-file transfers are implemented. The following
capabilities from the broader mobile-app list are not implemented:

| Missing capability         | Possible direction                                                                                                                                           |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Running-process management | Consider a bounded process list with explicit, safe actions only after defining identity, elevation, confirmation, stale results, and cancellation behavior. |

### HTTPS-enabled controller opportunities

Secure Direct and Gyro mouse provide an HTTPS controller, direct WebRTC data
transport, motion permission handling, and bounded sensor cleanup. Treat those
as existing owners, not as a general browser-capability framework. Feature-detect
every browser API, require explicit user activation for sensitive access, keep
captured data transient by default, and validate permission and lifecycle behavior
on real target devices.

| Candidate                 | Direction and evidence needed                                                                                                                                                                                                                                                                                                                  |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Motion gestures           | Extend the existing motion owner with a small set such as Shake and left/right Flick, mapped locally to existing actions. Prove useful thresholds, false-positive resistance, orientation behavior, cancellation, and real-iPhone operation before adding Tilt scroll or Custom Screen integration; do not send or persist raw sensor samples. |
| Share to Voltura Air      | Evaluate installed-PWA share intake as an entry point to existing Send text/Open URL behavior. Confirm target-platform support, launch/session behavior, input bounds, and an understandable fallback; do not add history, inboxes, or cloud storage.                                                                                          |
| Document scanning and OCR | Build on the implemented one-photo **Take photo** upload only after defining review, correction, multi-page/PDF output, OCR ownership, quality, memory, privacy, temporary-data limits, and real-device evidence.                                                                                                                              |
| Current location          | Consider only a direct user action that sends the current location to an existing text or URL destination. Define a concrete workflow and validate permission, precision disclosure, cancellation, and cleanup; do not add tracking, geofencing, or background automation.                                                                     |

#### Share to Voltura Air

- Accept shared text and one HTTP(S) address only after proving installed-PWA share
  target support on the intended devices. A shared file could only enter the existing
  one-file transfer after equivalent support and review are proven.
- Route an address to the existing Open URL review flow and other text to the
  existing Send text draft. Never open or send shared content automatically.
- Keep one bounded transient pending share across PWA launch and connection setup,
  let the user choose the PC destination, and discard it after use or cancellation.
  Define replacement behavior when another share arrives before the first is used.
- Preserve ordinary in-app controls as the fallback when the browser or installation
  mode cannot register Voltura Air in the device share sheet. Validate cold launch,
  already-open app, unpaired/disconnected state, malformed input, and navigation
  cleanup without adding a new host message.

#### Motion gesture shortcuts

- Extend the current gyro/motion owner rather than creating another sensor service.
  Classify a deliberately small vocabulary locally, beginning with Shake and
  left/right Flick, and emit only an existing Voltura Air action.
- Require an explicit enable or armed interaction so normal device handling cannot
  trigger PC input. Start with reversible actions and avoid destructive or
  hold-to-repeat mappings.
- Calibrate thresholds against false positives, different devices, portrait and
  landscape orientation, and accessibility needs. Stop observation on disable,
  navigation, page hiding, disconnect, or permission loss.
- Do not transmit, log, retain, or synchronize raw sensor samples. Add a protocol
  action only if no existing bounded action represents the chosen behavior.

#### Document scanning and OCR

- Keep the implemented **Take photo** one-image upload as the baseline and fallback.
  Any scanning or OCR work should prepare a user-reviewed result for the existing
  one-file transfer rather than introduce another transfer service.
- Investigate crop and perspective correction, retake, multi-page documents, and
  PDF generation only with explicit quality and memory bounds, temporary-data
  cleanup, and real-iPhone evidence.
- Define OCR output, language support, processing ownership, consent, and privacy
  before implementation. Do not retain extracted text or intermediate images after
  the user submits or cancels the result.
- Reuse the current upload destination, permissions, safe names, conflicts,
  progress, cancellation, Relay accounting, and partial-file cleanup.

#### Send current location to the PC

- Provide one explicit action that requests the current location, shows the
  returned accuracy and proposed text/map address, and requires review before the
  user chooses Open URL, Send text, or PC clipboard destination.
- Reuse the existing Open URL and Send text owners and permissions; no host protocol
  extension is needed unless a later workflow cannot be represented by their
  current bounded inputs.
- Make unsupported, denied, unavailable, timed-out, and canceled results clear and
  retryable. Cancel or ignore stale requests on navigation, page hiding,
  disconnect, or a newer activation.
- Never watch location, run in the background, build history, infer places, geofence,
  or log/persist coordinates. Decide whether the disclosed accuracy is acceptable
  before allowing the reviewed value to leave the device.

Presentation already includes Gyro in its Trackpad, while Send text and Open URL
already cover the basic phone-to-PC action. Any further work there needs a
specific usability gap rather than a parallel feature or transport. Push,
WebAuthn/passkeys, and app badging need a concrete product or security requirement
before further design.

### Files

- Consider host-defined custom Files locations below the Windows known folders after defining configuration ownership, unavailable-target behavior, ordering, and per-device visibility.
- Consider an internal read-only file viewer after defining supported formats, bounded decoding/rendering, privacy, temporary-data cleanup, large-file behavior, and fallback to the Windows default application.

### Additional device preferences

Candidates include restoring the last supported mode per PC/client and
assigning a default Remote mode. Keep theme, keyboard rows, and split placement
browser-local unless a cross-device workflow justifies host ownership.

## Candidate extensions

### High-impact / relatively natural extensions

1. **Multi-display simultaneous view and independent control**
   Display selection and switching are implemented, with one selected display
   active at a time. Consider pinning a second display as a read-only
   picture-in-picture or secondary view while the primary remains interactive.
   Define capture ownership, input targeting, bandwidth, quality adaptation, and
   cleanup for every displayed stream.
2. **Selected-application sound and local listening controls**
   Screen viewing already carries muted-by-default Windows system output under
   its existing permission. Consider selecting one application's sound when
   Windows exposes a reliable boundary, plus browser-local listening volume.
   Preserve session ownership, bounded capture, encrypted transport, and
   audio-only failure without adding remote control of PC playback.
3. **Notification relay or mirror**
   Optionally send Windows toast notifications or a host-filtered subset to the
   paired device, with bounded quick actions such as dismiss or an explicit
   reviewed reply through existing text tools. Define foreground/background PWA
   support, sensitive-content filtering, action authorization, expiry, and
   cleanup without persistent cloud storage.
4. **Clipboard history and multiple clipboard items**
   Build on explicit clipboard reads and writes with a short-lived, bounded
   host-side history for text and images. Keep **Copy to device** and **Paste from
   device** explicit, permission-scoped actions; define image transfer, item and
   byte limits, expiry, clearing, sensitive-content behavior, and disconnect
   cleanup before implementation.
5. **Session recording and annotated screenshots**
   Extend the existing native-PNG screen capture with optional annotations and
   short recordings of the live mirror, including an optional laser-pointer or
   drawing overlay. Keep captures transient until the user explicitly saves them
   on the device or uploads one through the existing one-file transfer, with
   browser support, duration, memory, cancellation, and cleanup limits.

### Quality-of-life and power-user features

6. **Virtual gamepad or controller**
   Consider a configurable touch-and-gyro controller with D-pad, buttons, and
   analog sticks, building on Custom Screen control concepts. A full
   XInput/DirectInput path requires an explicit permission plus decisions about
   driver installation and signing, elevation, anti-cheat compatibility, latency,
   neutral disconnect state, removal, and recovery.
7. **Macro or sequence recorder**
   Record a bounded sequence of existing pointer, keyboard, application-launch,
   or other allowlisted actions on the host and replay it from a Custom Screen or
   device control. Store definitions only on the PC and expose only opaque IDs and
   labels; define confirmation, timing and repetition limits, cancellation,
   destructive-action handling, stale targets, and no arbitrary code execution.
8. **Wake-on-LAN and reachability status**
   Add an opt-in reachable/awake indicator and Wake-on-LAN only after identifying
   an available LAN-side sender; a sleeping PC cannot send its own wake packet and
   a browser cannot assume raw LAN access. Define hardware/network prerequisites,
   validated target data, permission, explicit activation, rate limits, stale
   status, and behavior outside the local network.
9. **Limited selective folder mirroring**
   Consider one-way or two-way synchronization for a small number of explicitly
   chosen PC and device folders rather than general Dropbox-style sync. Define
   ownership, conflict policy, deletion semantics, background/browser lifecycle,
   bandwidth and size limits, offline state, recovery journals, partial-file
   cleanup, and opt-in permissions before adding watchers or persistent state.
10. **Browser and tab control enhancements**
    Evaluate browser-specific integration for supported Edge, Chrome, and other
    Chromium-family browsers to list, focus, close, and open tabs or send reviewed
    text to the address bar. Reuse the web-address permission where it accurately
    covers the action, while defining extension/native integration, browser
    support, private-window exclusion, opaque tab identity, stale results, and
    failure behavior.

### Presentation and media polish

11. **Live annotations or whiteboard overlay**
    Draw over the live screen mirror with finger or stylus and optionally render a
    temporary PC overlay or save a composed image. Keep annotations separately
    owned from pointer input, define whether pixels ever return to the PC, and set
    explicit permissions, bounds, latency, clear/undo behavior, capture treatment,
    and teardown.
12. **Advanced YouTube and media remote**
    Consider seek, playlist navigation, chapter jumps, and supported quality
    controls for YouTube, plus equivalent controls for players such as Spotify or
    VLC when the host can identify and operate them reliably. Extend existing
    media owners where possible and define per-player capability detection, stale
    sessions, unsupported controls, and fallback behavior.

### Accessibility and multiple devices

13. **Voice command layer**
    Map a deliberately small set of reviewed voice intents such as **Next slide**,
    **Volume up**, or **Switch to Chrome** to existing authorized actions. Prefer
    on-device or local processing, require clear activation and confirmation where
    appropriate, and do not turn the read-only AI Assistant into an unrestricted
    action agent or retain raw audio by default.
14. **Secondary viewer or observer mode**
    Allow a second authorized device to view the selected display read-only while
    one primary device retains control. Define a separate permission, primary
    ownership, viewer limits, stream/capture reuse versus independent encoding,
    Relay bandwidth and quota behavior, revocation, and deterministic cleanup.
15. **Tablet stylus and pressure support**
    Use browser Pointer Events pressure and stylus metadata in Screen viewing or
    Custom Screens to drive reviewed mappings such as drawing pressure, mouse
    buttons, or right-click. Feature-detect support and define calibration,
    orientation, palm rejection, accessibility alternatives, input bounds, and
    fallback when pressure is unavailable.

### Shared implementation boundaries

- Put every new capability under the existing per-device permission matrix and
  **My device**, **Remote controls**, and **Custom** profiles.
- Prefer local-first operation or the existing end-to-end encrypted Relay paths;
  do not add persistent cloud storage for user content.
- Keep the client a browser/PWA. Privileged Windows APIs, durable configuration,
  and heavy processing remain host-owned.
- Keep new work open-source friendly, explicitly permission-gated, and documented
  in the implemented-capability owner only after it ships.

## Public project and release

| Candidate       | Decision boundary                                                                                |
| --------------- | ------------------------------------------------------------------------------------------------ |
| Demo video/GIF  | Isolated capture, captions, privacy-safe content, and licensed media.                            |
| Code signing    | Certificate, cost, key custody, CI signing, timestamps, renewal, revocation, and asset coverage. |
| Microsoft Store | Packaging, signing, account, policy, update channel, and demonstrated benefit.                   |

## Research-gated capabilities

| Candidate          | Evidence needed                                                                                               |
| ------------------ | ------------------------------------------------------------------------------------------------------------- |
| Native mobile apps | Demonstrated PWA gap, platform scope, protocol parity, accessibility, privacy, distribution, and maintenance. |

## Platform and compatibility

- Any public upgrade guarantee needs an explicit compatibility policy for
  persisted settings, pairing data, protocol messages, and client formats.
