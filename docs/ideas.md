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
| Live pairing QR scanner | Replace or complement the current photo capture with a temporary rear-camera preview that decodes frames through the existing QR decoder and pairing-link parser. Stop the camera as soon as a valid Voltura Air code is found, the page is hidden, or the user cancels; retain photo capture as fallback and the existing device-name confirmation. Validate permission denial, browser coverage, cancellation, and immediate track cleanup. Only consider general QR-to-text/URL scanning after this pairing workflow proves useful; do not add image storage, sync, or a generic camera framework. |
| Browser clipboard actions | Add explicit user-triggered Copy to phone and Paste phone clipboard to PC actions only where the Clipboard API permits them. Reuse Get text/Send text and their host permissions, retain the existing UI as fallback, and do not add background monitoring or synchronization. |
| Mobile share intake | Evaluate installed-PWA share intake as an entry point to existing Send text/Open URL behavior. Confirm target-platform support, launch/session behavior, input bounds, and an understandable fallback; do not add history, inboxes, or cloud storage. |
| Current location | Consider only a direct user action that sends the current location to an existing text or URL destination. Define a concrete workflow and validate permission, precision disclosure, cancellation, and cleanup; do not add tracking, geofencing, or background automation. |
| Phone microphone or camera as a Windows input | Keep microphone and webcam modes as separate feasibility spikes. Prove the Windows virtual-device/media endpoint, packaging, signing, permissions, latency, disconnect cleanup, and real application compatibility before choosing product architecture; use WebRTC media tracks rather than expanding the control DataChannel. |

Presentation already includes Gyro in its Trackpad, while Send text and Open URL
already cover the basic phone-to-PC action. Any further work there needs a
specific usability gap rather than a parallel feature or transport. Push,
WebAuthn/passkeys, and app badging need a concrete product or security requirement
before further design.

### Files

- Consider host-defined custom Files locations below the Windows known folders after defining configuration ownership, unavailable-target behavior, ordering, and per-device visibility.
- Consider an internal read-only file viewer after defining supported formats, bounded decoding/rendering, privacy, temporary-data cleanup, large-file behavior, and fallback to the Windows default application.
- Consider transferring files between the PC and mobile device as a separate extension to Files on PC. Start with one-file-at-a-time authenticated streaming downloads, then evaluate uploads and multi-file archives. Keep host-side Copy and Move distinct, require a separate global permission with per-device overrides, and define large-file confirmation, progress, cancellation, relay encryption, quotas, safe names, path containment, partial-file cleanup, and short-lived download authorization before implementation.

### Additional device preferences

Candidates include restoring the last supported mode per PC/client and
assigning a default Remote mode. Keep theme, keyboard rows, and split placement
browser-local unless a cross-device workflow justifies host ownership.

## Public project and release

| Candidate | Decision boundary |
| --- | --- |
| Demo video/GIF | Isolated capture, captions, privacy-safe content, and licensed media. |
| Comparison table | Verifiable current facts and primary sources for named alternatives. |
| FAQ | Add only when recurring support demand justifies maintenance. |
| Release checksums | Generate and verify one checksum file against every asset. |
| Update notification | Choose manual, opt-in periodic, or disabled-by-default checks with privacy and failure behavior. |
| Code signing | Certificate, cost, key custody, CI signing, timestamps, renewal, revocation, and asset coverage. |
| Automatic update | Integrity, signing, consent, rollback, recovery, privacy, and ownership. |
| Microsoft Store | Packaging, signing, account, policy, update channel, and demonstrated benefit. |

## Research-gated capabilities

| Candidate | Evidence needed |
| --- | --- |
| Wake-on-LAN | An available LAN sender, hardware/network prerequisites, validated target data, and explicit confirmation. |
| Screen preview | Consent, capture behavior, protected content, encoding, limits, authorization, and cleanup. |
| PC/mobile file transfer | Authenticated streaming download and upload, separate permission, relay encryption, quotas, safe names, path containment, provenance, cancellation, and partial-file cleanup. |
| Gamepad mode | Driver, signing, elevation, install/remove, anti-cheat behavior, neutral disconnect, and latency. |
| Native mobile apps | Demonstrated PWA gap, platform scope, protocol parity, accessibility, privacy, distribution, and maintenance. |

## Platform and compatibility

- Any public upgrade guarantee needs an explicit compatibility policy for
  persisted settings, pairing data, protocol messages, and client formats.
