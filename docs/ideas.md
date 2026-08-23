# Voltura Air candidate directions

These ideas need a product decision or evidence before moving to
[todo.md](todo.md). Each implementation needs explicit ownership, limits,
privacy/security review, recovery behavior, and proportionate validation.

## Dictate: PC assistance

Optionally add a named, feature-gated Dictate path that inspects the Windows default
microphone and opens Windows Voice Typing without capturing audio.

Decide the user value, permission model, privacy wording, and whether controlling
the system-wide default input is appropriate. If promoted, the host should use
bounded Core Audio operations, fixed Windows Settings destinations, the
protected input path for `Win+H`, no polling, and no microphone names, levels,
peaks, text, or audio in logs. Browser dictation must remain independent.

## Presentation

- Consider reusable host-managed presentations only after defining canonical
  presentation identity, file ownership, deletion behavior, and how saved runs
  relate to the reusable item.
- Consider bounded Open XML metadata inspection for modern PowerPoint files
  without launching PowerPoint. Decide refresh behavior, legacy `.ppt`
  treatment, failure states, and cleanup first.
- Add bounded multi-select report actions with clear filtered-selection and
  partial-failure behavior.
- Evaluate deeper mail-provider integration only with explicit consent, token
  ownership, provider limits, and final user review.
- Research slideshow zoom through a supported automation mechanism; do not
  inject `+` or `-` because PowerPoint's slideshow Zoom property is read-only.
- Validate reliable automation before adding All Slides or Presenter View
  interactions.
- Consider presentation previews only after deciding rendering, privacy, cache
  limits, invalidation, and deletion behavior.
- Consider ink/read-only controls, hyperlinks, hidden-slide/help controls,
  media transport, and temporary-pointer behavior only with explicit state,
  availability, and cleanup contracts.

## Control and personalization

### Mobile app gaps

Mouse and keyboard control, media playback controls, and Files for Windows PC or
mapped-drive storage are implemented. The following capabilities from the broader
mobile-app list are not implemented:

| Missing capability | Possible direction |
| --- | --- |
| Phone storage access / PC-device transfer | Let the user explicitly copy one file at a time between the paired PC and phone storage. Start with iPhone validation; treat Android as a later compatibility target until a real Android device is available for testing. Use the detailed **PC and device file transfer** candidate below; do not begin with sync, backup, batches, or archives. |
| System information and diagnostics | Consider a read-only, user-invoked mobile view of selected host information and diagnostic state. Define privacy, permission, redaction, bounds, and failure behavior first. |
| Running-process management | Consider a bounded process list with explicit, safe actions only after defining identity, elevation, confirmation, stale results, and cancellation behavior. |
| Terminal or shell access | Keep this research-gated until authentication, command policy, output limits, working-directory restrictions, lifetime, privacy, and audit behavior are defined. |
| PC screenshots | Consider an explicit action to capture a selected PC display or current view and deliver it to the mobile app, with permission, size, transient-storage, and cleanup limits. |
| Clipboard synchronization | One-shot clipboard reads and writes are supported, but continuous or background synchronization is not. Any future sharing must remain explicit, foreground, and privacy-bounded. |
| Reusable custom macros | Custom Screens provide individual configured actions, not a general multi-step macro runner. Consider bounded workflows only with explicit confirmation, cancellation, and no arbitrary code execution. |

### HTTPS-enabled controller opportunities

Secure Direct and Gyro mouse provide an HTTPS controller, direct WebRTC data
transport, motion permission handling, and bounded sensor cleanup. Treat those
as existing owners, not as a general browser-capability framework. Feature-detect
every browser API, require explicit user activation for sensitive access, keep
captured data transient by default, and validate permission and lifecycle behavior
on real target devices.

| Candidate | Direction and evidence needed |
| --- | --- |
| Motion gestures | Extend the existing motion owner with a small set such as Shake and left/right Flick, mapped locally to existing actions. Prove useful thresholds, false-positive resistance, orientation behavior, cancellation, and real-iPhone operation before adding Tilt scroll or Custom Screen integration; do not send or persist raw sensor samples. |
| Share to Voltura Air | Evaluate installed-PWA share intake as an entry point to existing Send text/Open URL behavior. Confirm target-platform support, launch/session behavior, input bounds, and an understandable fallback; do not add history, inboxes, or cloud storage. |
| PC/device file transfer | Extend Files with one-file-at-a-time authenticated download first, then upload. Define a bounded payload transport separate from interactive commands, permissions, progress, cancellation, Relay quotas, safe names, path containment, partial-file cleanup, and reconnect behavior before considering batches or archives. |
| Capture or scan directly to PC | After upload exists, reuse its destination, progress, cancellation, and cleanup while the existing camera owner supplies one transient photo or document capture. Prove real-device capture, review, retake, page lifecycle, and camera-track cleanup before considering multi-page scanning, correction, or OCR. |
| Current location | Consider only a direct user action that sends the current location to an existing text or URL destination. Define a concrete workflow and validate permission, precision disclosure, cancellation, and cleanup; do not add tracking, geofencing, or background automation. |

#### Share to Voltura Air

- Accept shared text and one HTTP(S) address only after proving installed-PWA share
  target support on the intended devices. Files belong to the separate file-transfer
  candidate.
- Route an address to the existing Open URL review flow and other text to the
  existing Send text draft. Never open or send shared content automatically.
- Keep one bounded transient pending share across PWA launch and connection setup,
  let the user choose the PC destination, and discard it after use or cancellation.
  Define replacement behavior when another share arrives before the first is used.
- Preserve ordinary in-app controls as the fallback when the browser or installation
  mode cannot register Voltura Air in the device share sheet. Validate cold launch,
  already-open app, unpaired/disconnected state, malformed input, and navigation
  cleanup without adding a new host message.

#### PC and device file transfer

- Start with **Download to this device** for one selected PC file. Add one-file
  upload into the current Files folder only after download ownership, cancellation,
  and cleanup are proven. Defer directory transfer, multiple selection, archives,
  background continuation, and synchronization.
- Keep Files as the UI/session owner and add a separate default-off transfer
  permission with global and per-device policy. Existing PC-side Copy and Move
  remain distinct and must not imply permission to move bytes onto another device.
- Do not carry large payloads on the authenticated interactive command queue. Choose
  a bounded authenticated streaming path that preserves low-latency control traffic
  and works deliberately across Enhanced Direct and Relay.
- Define size limits, confirmation, progress, cancellation, timeout, reconnect,
  Relay accounting/quotas, safe download names, upload path containment, conflict
  handling, and durable ownership of every partial destination before protocol work.
  Test interruption and cleanup at each external read, write, network, and commit
  boundary.

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

#### Capture or scan directly to PC

- Treat this as a focused Files upload entry point, not a new transfer service.
  The user chooses or confirms the PC folder, captures one image, reviews or retakes
  it, and explicitly uploads it through the same permission and transfer owner.
- Reuse the existing camera permission and lifecycle mechanisms, while keeping
  capture state separate from Phone webcam streaming and QR pairing. Release every
  track on retake replacement, completion, cancellation, navigation, page hiding,
  disconnect, or failure; do not retain an unsubmitted image.
- Preserve the original capture first. Consider crop, perspective correction,
  compression choice, multi-page documents, PDF generation, or OCR only after the
  one-image workflow has real-device evidence and explicit quality, memory, and
  temporary-data limits.
- Use the transfer candidate's safe names, conflicts, progress, cancellation,
  reconnect behavior, Relay quotas, partial-file cleanup, and failure testing.

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
- Treat the detailed **PC and device file transfer** candidate above as the sole
  transfer direction; do not create a parallel Files transport or permission model.

### Additional device preferences

Candidates include restoring the last supported mode per PC/client and
assigning a default Remote mode. Keep theme, keyboard rows, and split placement
browser-local unless a cross-device workflow justifies host ownership.

## Public project and release

| Candidate | Decision boundary |
| --- | --- |
| Demo video/GIF | Isolated capture, captions, privacy-safe content, and licensed media. |
| Update notification | Choose manual, opt-in periodic, or disabled-by-default checks with privacy and failure behavior. |
| Code signing | Certificate, cost, key custody, CI signing, timestamps, renewal, revocation, and asset coverage. |
| Automatic update | Integrity, signing, consent, rollback, recovery, privacy, and ownership. |
| Microsoft Store | Packaging, signing, account, policy, update channel, and demonstrated benefit. |

## Research-gated capabilities

| Candidate | Evidence needed |
| --- | --- |
| Wake-on-LAN | An available LAN sender, hardware/network prerequisites, validated target data, and explicit confirmation. |
| Screen preview | Consent, capture behavior, protected content, encoding, limits, authorization, and cleanup. |
| Gamepad mode | Driver, signing, elevation, install/remove, anti-cheat behavior, neutral disconnect, and latency. |
| Native mobile apps | Demonstrated PWA gap, platform scope, protocol parity, accessibility, privacy, distribution, and maintenance. |

## Platform and compatibility

- Any public upgrade guarantee needs an explicit compatibility policy for
  persisted settings, pairing data, protocol messages, and client formats.
