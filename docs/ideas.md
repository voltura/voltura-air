# Voltura Air candidate directions

These ideas need a product decision or evidence before moving to
[todo.md](todo.md). Each implementation needs explicit ownership, limits,
privacy/security review, recovery behavior, and proportionate validation.

## Dictate: PC assistance

Optionally add an alpha-gated Dictate path that inspects the Windows default
microphone and opens Windows Voice Typing without capturing audio.

Decide the user value, permission model, privacy wording, and whether controlling
the system-wide default input is appropriate. If promoted, the host should use
bounded Core Audio operations, fixed Windows Settings destinations, the
protected input path for `Win+H`, no polling, and no microphone names, levels,
peaks, text, or audio in logs. Browser dictation must remain independent.

## Presentation

- Define canonical presentation identity before cross-session analytics.
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
| Phone-to-PC files | Authenticated upload, quotas, safe names, path containment, provenance, cancellation, and partial-file cleanup. |
| Motion pointer | Browser permission behavior, calibration, sensitivity, active-session collection, and cleanup. |
| Gamepad mode | Driver, signing, elevation, install/remove, anti-cheat behavior, neutral disconnect, and latency. |
| Native mobile apps | Demonstrated PWA gap, platform scope, protocol parity, accessibility, privacy, distribution, and maintenance. |

## Platform and compatibility

- HTTPS LAN transport needs a threat model, host identity, certificate lifecycle,
  key custody, mobile trust, browser/discovery behavior, and recovery.
- Any public upgrade guarantee needs an explicit compatibility policy for
  persisted settings, pairing data, protocol messages, and client formats.
