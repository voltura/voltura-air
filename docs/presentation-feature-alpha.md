# Presentation feature alpha authority

This document is the maintained product authority for Presentation while the
feature remains alpha. It owns Presentation-specific current behavior, approved
unfinished work, platform findings, and candidate decisions. The
[protocol](protocol.md), [architecture](architecture.md),
[product UI system](ui-system.md), [Windows host UI guidelines](host-ui-guidelines.md),
and [privacy policy](../PRIVACY.md) remain authoritative for their shared
domains.

## Purpose, audience, gate, and permissions

- **Implemented** — Presentation provides a phone/tablet presenter surface,
  presentation timing and slide statistics, host-side report storage, and a
  Windows report archive for presenters who want one local workflow.
- **Implemented** — The audience is a user presenting from the paired Windows
  PC with PowerPoint, Google Slides, or a PDF/browser presentation.
- **Implemented** — **Preferences > Developer tools > Enable alpha features**
  defaults on for this release and can be turned off by the user. The host omits
  Presentation capability metadata while the gate is off and enforces the same
  gate at command and report-save boundaries.
- **Implemented** — The effective global/per-device **Presentation control**
  permission governs presenter commands, native laser state, and report saves.
  Stored reports remain available in the Windows archive after the alpha gate
  is disabled.
- **Implemented** — Presentation is the default fourth mobile mode on a clean
  client. Dictation, Send text to PC, and Get text from PC remain selectable;
  if the host omits Presentation capability, the client falls back to Dictation
  without exposing an unusable Presentation entry point.
- **Deferred** — Graduation and removal of the alpha gate require the separately
  reviewed change described in [Graduation](#graduation).

## Current feature inventory

### Mobile presentation controls

- **Implemented** — The user explicitly selects PowerPoint, Google Slides, or
  PDF/browser. Voltura Air does not infer the focused app.
- **Implemented** — Next, Previous, and End use acknowledged reviewed shortcut
  commands for all three targets. PowerPoint also exposes Start slideshow.
  Blackout uses the separately permissioned display-curtain action where shown.
- **Implemented** — Start slideshow starts presentation timing when timing is
  not already active. Next starts timing and slide 2 when no session exists.
  Repeated Start while timing is active does not alter statistics.
- **Implemented** — Start remains available because the host does not claim to
  know whether PowerPoint accepted or retained slideshow focus.
- **Implemented** — The native Voltura Air laser pointer is available for all
  three targets. The client sends an explicit desired state; the host applies
  one generated laser cursor to all Windows cursor roles, reports actual state,
  restores the cached configured custom-pointer or Windows scheme on disable,
  and uses the existing cursor-recovery watchdog. It is disabled on leaving
  Presentation, owner disconnect, End slideshow, and host shutdown.
- **Implemented** — While alpha features are enabled, **Preferences >
  Presentation** provides host-only laser size from 1–15 and labeled Red,
  Green, and Blue choices. The default is size 6 and Red. Appearance preferences
  are cached and persisted on the PC; transient enabled state is never persisted.
- **Implemented** — A compact volume row provides volume down, mute/unmute, and
  volume up without introducing another panel.
- **Implemented** — Presentation controls remain usable independently of the
  local timer except where an acknowledged command is pending.

### Integrated trackpad and adaptive layout

- **Implemented** — A collapsible trackpad sits between presenter controls and
  the timer. It starts collapsed; choosing Laser pointer expands it.
- **Implemented** — Expanding the trackpad collapses the timer, and expanding
  the timer collapses the trackpad. Expanded trackpad layouts consume remaining
  usable space without making the page scroll.
- **Implemented** — Its fullscreen action retains the normal trackpad action
  placement and adds the normal left/right buttons using the saved handedness.
  Presentation mode remains active.
- **Implemented** — Portrait panels attach without decorative empty rows.
  Landscape keeps controls to the left and the trackpad/timer region to the
  right. Safe-area insets apply to edge-sensitive content.
- **Implemented** — Timer typography scales with available width so hour-length
  values remain visible beside the compact log. Regions stack before clipping
  when their minimum usable widths cannot coexist.

### Timer, sessions, slides, and breaks

- **Implemented** — A timer session begins on Start and ends only through the
  end/reset workflow. Active state intentionally does not survive reload,
  application restart, or Presentation-mode unmount.
- **Implemented** — The target selected at Start is captured for the report and
  does not change when the control target later changes.
- **Implemented** — Presenting time excludes breaks. Total elapsed time includes
  presenting sessions and breaks and represents the audience's total time
  commitment.
- **Implemented** — Every Pause closes a presenting session and creates a
  numbered break with cumulative presentation time, live/final break duration,
  wall-clock start/end, and slide context.
- **Implemented** — Resume starts the next presenting session. The report keeps
  session duration, break duration, running total elapsed time, session count,
  break count, and presentation/break totals.
- **Implemented** — Ending while a break is active closes that terminal break
  without inventing a zero-duration presentation session after it.
- **Implemented** — Start slideshow assumes slide 1. Next closes the active
  slide and advances; Previous returns to the prior slide. Time accumulates by
  slide number when a slide is revisited. Next with no active timer starts a
  session on slide 2.
- **Implemented** — Up to 1,000 distinct slide numbers are accepted by the
  report protocol. Slide timing is observational and follows the user's
  presenter-button actions; it does not claim knowledge of external app state.
- **Implemented** — The compact newest-first rail shows the presentation
  session/checkpoint and following break duration with persistent semantic
  icons. It is hidden until the first break and uses only the available height,
  commonly showing two or three of the newest records.
- **Implemented** — Selecting a compact row opens an OK-dismissed detail dialog.
  Up to 100 breaks are retained; Pause is blocked at the limit with save/reset
  guidance.
- **Implemented** — Planned duration, five-minute and elapsed milestones,
  optional vibration, timer folding, and a debug-only 10x timer multiplier are
  retained. The multiplier is not part of saved production behavior.

### Live statistics and lifecycle

- **Implemented** — Expanded read-only statistics prioritize presenting
  information: captured type, start time, current state, session time,
  presenting and break totals, sessions, slides, breaks, an intermittent
  proportional timeline, and chronological session/break rows with running
  elapsed totals.
- **Implemented** — The expanded view provides Minimize, Pause/Resume, and End
  presentation. Statistics cards wrap instead of truncating.
- **Implemented** — End presentation freezes the active session/slide and opens
  an end-framed save dialog. Reset uses the neutral **Save presentation data**
  workflow. Narrow dialogs stack actions with tokenized gaps.
- **Implemented** — Leaving Presentation with active timing shows
  **Presentation active** and offers Return to presentation or Continue.
  Connection changes receive equivalent in-context protection.
- **Implemented** — End slideshow and End presentation enter the same frozen
  save workflow when timing data exists.

## Report saving, storage, and archive

- **Implemented** — Reset/end freezes a snapshot. The user can save, discard, or
  cancel. Cancel restores the prior timer state without adding dialog time.
- **Implemented** — Save is unavailable while disconnected and the frozen
  snapshot remains retryable. Failure or timeout preserves it. A successful
  matching acknowledgement clears timer/history.
- **Implemented** — Authenticated `presentation.report.save` and
  `presentation.report.save.result` are idempotent by operation/report IDs. The
  exact wire contract, bounds, fields, and failure codes belong to
  [protocol.md](protocol.md#presentation-commands-and-reports).
- **Implemented** — The host derives device identity/name from the authenticated
  pairing, validates the target allowlist, chronology, finite durations,
  identifiers, transport size, break/slide bounds, gate, and permission.
- **Implemented** — Reports are normalized and atomically stored under
  `%LOCALAPPDATA%\Voltura Air\Presentation statistics\<safe-device-key>\`.
  Stored reports are treated as untrusted and their titles/timing content are
  not logged.
- **Implemented** — Device names are captured with reports and survive later
  device rename/removal. Removing a paired device does not delete its reports.
- **Implemented** — Default names are `Presentation`, `Presentation (1)`, and so
  on within one captured device. Different devices have independent sequences.
  Rename edits the presentation name and validates a Windows-safe export name.
- **Implemented** — The archive retains at most 1,000 reports. New saves are
  rejected at capacity; reports are never silently evicted.
- **Implemented** — The top-level Windows **Presentations** page has title,
  type, captured-device, and date-range filters; current-filter aggregates; a
  newest-first virtualized archive; Open and row double-click navigation; and
  filtered export, email, and deletion.
- **Implemented** — Detail shows the presentation name/type, captured
  date/device, compact statistics, intermittent timeline, chronological
  session/break table, fixed actions, and running elapsed totals.
- **Implemented** — Per-report actions include Rename, Presentation file,
  Presentation URL, Export, Email, and confirmed Delete. File and URL status is
  semantic and exposed through accessible themed tooltips.
- **Implemented** — A report can link one local/OneDrive-visible file path and
  one validated HTTP/HTTPS URL. Stored paths are never sent to the mobile client
  or written to logs.
- **Implemented** — Export supports modern self-contained HTML, XLSX, PDF,
  formula-safe CSV, and TXT for one detail or the current filtered archive.
  Successful export opens the file through Windows shell association.
- **Implemented** — Email creates an Outlook draft through local automation when
  available and otherwise opens an `.eml` draft. It can send statistics only or
  attach available linked presentation files independently of whether the same
  report also has a URL; report URLs appear as links. A requested file that
  becomes unavailable stops draft creation with recoverable guidance rather
  than producing a successful draft with missing attachments. The
  email-body HTML uses conservative mail-client-compatible markup. When the
  available mail app cannot accept requested attachments, Voltura Air reports a
  recoverable failure instead of opening Explorer as a substitute.
- **Implemented** — Generated email artifacts become eligible for removal after
  seven days. An immediate, hourly host-owned cleanup removes them when they are
  no longer held by a mail client and retries locked files; the private draft
  directory is capped at 100 files so external clients cannot cause unbounded
  local copies.
- **Implemented** — Delete applies to the current filtered set, or all reports
  when no filter is active. Successful filtered deletion clears the filters.

## UI and accessibility contract

- **Implemented** — Presentation follows [ui-system.md](ui-system.md) and the
  host archive follows [host-ui-guidelines.md](host-ui-guidelines.md). Existing
  shared buttons, inputs, dialogs, tooltips, list cards, focus borders, spacing,
  safe areas, and minimum target sizes are reused.
- **Implemented** — `presentation-segment` and `presentation-break` semantic
  tokens are generated for React, WPF, and reports. Icons, labels, row names,
  table headers, and accessible timeline descriptions preserve meaning without
  color.
- **Implemented** — Controls expose state through text and accessibility
  properties, including `aria-pressed` for the native laser. Dialog focus,
  keyboard navigation, touch, screen-reader names, reduced motion, themes, and
  High Contrast follow the shared authorities.
- **Implemented** — Mobile/tablet copy stays concise; detailed timing metadata is
  disclosed through statistics or a selected-row dialog.

## Data, privacy, and resource contract

- **Implemented** — Durations are authoritative; wall-clock timestamps provide
  context. Presentation content, slide text, window titles, filenames, paths,
  and URLs are not detected automatically.
- **Implemented** — Active timing remains browser memory only until explicit
  save. Saved reports and optional file/URL links stay on the Windows PC as
  described by the [privacy policy](../PRIVACY.md).
- **Implemented** — Application logs record only bounded command/result metadata
  and failure details. They never contain report names, timing contents, linked
  paths, or URLs.
- **Implemented** — Timer work exists only while active. Native laser state is
  cached in memory, changes only on explicit state transitions, performs no
  registry read/write per command, and uses no polling or overlay worker.
- **Implemented** — Stored JSON readers reject files over 256 KiB before
  deserialization and validate bounded nested content defensively; persistence
  uses atomic replacement and preserves the last complete state on failure.
- **Implemented** — Archive dates, filtering, and break timestamps use the
  UTC offset captured with each report rather than the PC's current timezone.

## Platform findings and limitations

- **Implemented** — Presenter shortcuts are sent to the currently focused
  eligible Windows application. Success means Windows accepted input, not that
  PowerPoint, a browser, or a PDF viewer changed state.
- **Implemented** — PowerPoint Start can be retried because Voltura Air does not
  query slideshow state. Repeating it while local timing is active does not
  duplicate statistics.
- **Implemented** — Slide numbering is inferred from Voltura Air Next/Previous
  actions. Keyboard, mouse, presenter hardware, in-app navigation, skipped
  slides, custom shows, and external app state can make it differ from the
  visible slide.
- **Implemented** — Browser/PWA lifecycle can discard unsaved in-memory timing;
  navigation guards reduce accidental loss but do not provide background
  recovery.
- **Implemented** — Linked OneDrive files work when Windows exposes a usable
  local path. Cloud-only availability and mail-client automation vary by local
  provider and policy.
- **Implemented** — Native laser appearance is a generated Windows cursor. Its
  apparent size can vary with Windows cursor scaling and display DPI.
- **Deferred** — Saved-report management is Windows-only.

## Candidate / V2 register

Each candidate remains unapproved until its unresolved decisions are made.
New evidence updates the relevant entry rather than adding a chronological log.

### Recent presentation launch from the device

- **Candidate / V2** — User value: choose a recent named presentation on the
  phone/tablet and start the correct file or URL without returning to Windows.
- **Candidate / V2** — Platform constraints: the host can use Windows shell
  association for an existing linked path or URL, but availability, application
  focus, cloud hydration, renamed/moved files, and browser sign-in are not
  guaranteed.
- **Candidate / V2** — Privacy: the host must expose only a bounded display
  name, opaque ID, type, and availability—not local paths or full private URLs.
- **Candidate / V2** — Dependencies: stored report association, authenticated
  launch acknowledgement, permission enforcement, and up to 10 bounded recent
  entries.
- **Candidate / V2** — Decisions: whether recency is per device or shared,
  duplicate-name behavior, missing-target recovery, and whether launch also
  begins slideshow/timing.

### Canonical presentation identity and cross-session analytics

- **Candidate / V2** — User value: compare repeated deliveries of “Physics 101”
  or the same deck across dates.
- **Candidate / V2** — Platform constraints: report names and file paths are not
  canonical; Google Slides URLs contain presentation-specific and mode-specific
  forms; moved/copied files can change identity.
- **Candidate / V2** — Privacy: automatic filename, path, URL, browser-tab, or
  window-title detection would collect contextual data and needs explicit
  disclosure, minimization, and opt-in.
- **Candidate / V2** — Dependencies: a stable host-owned presentation identity,
  merge/split controls, renamed/missing resource handling, and aggregate rules.
- **Candidate / V2** — Decisions: manual identity only versus detection,
  canonical Google Slides normalization, and whether analytics combine devices.

### Bulk report management

- **Candidate / V2** — User value: multi-select reports for export, email,
  deletion, file association, or device-level cleanup.
- **Candidate / V2** — Platform constraints: virtualized selection, filtered
  hidden selections, keyboard/touch parity, and confirmation counts must remain
  understandable.
- **Candidate / V2** — Privacy: bulk email/export can disclose more report and
  linked-file data than a single-report action.
- **Candidate / V2** — Dependencies: explicit selection model, action-specific
  eligibility, failure summaries, and bounded batch size.
- **Candidate / V2** — Decisions: selection persistence across filters and which
  actions may operate on partial eligibility.

### Deeper mail-provider integration

- **Candidate / V2** — User value: automatically add attachments or send through
  a chosen provider without the current local draft/fallback workflow.
- **Candidate / V2** — Platform constraints: Outlook variants, default mail
  clients, provider APIs, authentication, attachment limits, and enterprise
  policy differ.
- **Candidate / V2** — Privacy: provider integration transmits report and
  presentation data to a third party and requires explicit consent and policy.
- **Candidate / V2** — Dependencies: provider/account ownership, token security,
  recipient confirmation, retry/idempotency, and audit-safe logging.
- **Candidate / V2** — Decisions: draft versus automatic send, supported
  providers, and whether attachments are ever added without a final user review.

## Validation contract

- **Implemented** — Mobile coverage exercises target selection, commands,
  timing transitions, sessions, slides, breaks, limits, reset/save/retry state,
  exit cleanup, adaptive disclosure, and accessible labels.
- **Implemented** — Host coverage exercises gate/permission enforcement,
  strict wire validation, idempotent report saves, chronology/bounds, capacity,
  atomic persistence and corruption recovery, archive calculations/actions,
  exports, and native cursor apply/restore ownership.
- **Implemented** — Cross-runtime changes use sequential root build/test gates.
  Structural passes run `npm run size:check`; catalog changes run
  `npm run docs:check`.
- **Approved / in progress** — Complete the final integrated build/test,
  size/docs gates, and a real paired-device check with PowerPoint, Google
  Slides, and PDF/browser before graduation review.

## Graduation

- **Approved / in progress** — Satisfy the remaining validation item and review
  every Implemented statement against the shipping build.
- **Deferred** — Remove the alpha gate and alpha-only wording only through a
  separately reviewed graduation change that retains the Presentation
  permission and production command enforcement.
- **Deferred** — On graduation, migrate implemented user-visible behavior into
  [features.md](features.md), move unresolved Candidate / V2 entries into
  [ideas.md](ideas.md), and update the root README, public site,
  machine-readable summary, setup, privacy, and troubleshooting as applicable.
- **Deferred** — Remove this document and its catalog entry only after every
  implemented rule and unresolved candidate has another maintained authority
  and no unique alpha authority remains.
