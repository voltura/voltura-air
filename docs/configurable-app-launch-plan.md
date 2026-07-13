# Configurable App Launch: Plan and Work Log

Updated: 2026-07-13

Status: Implemented; ready for user validation

This document is the durable implementation plan and handoff log for
`docs/todo.md` section 2.1, **Configurable app launch**. It is intended to make
the work reviewable without relying on the Codex conversation.

## Roadmap audit

The roadmap's recommended issue list still names keyboard compatibility and
help text as its first item. That work was implemented and removed from the
open roadmap sections in later commits. The first genuinely open roadmap item
is therefore configurable app launch, and this implementation starts there.

## Product outcome

The Windows user can choose which application-launch buttons a paired device
may see. Built-in presets cover Browser, Spotify, VLC, and PowerPoint. The user
can also add a custom executable with optional arguments after reviewing a
host-side warning. The mobile Remote screen presents only the approved labels
and sends only opaque action IDs.

## Security contract

The following constraints are part of the feature, not optional hardening:

- A mobile client never sends an executable path, command line, URL, or preset
  target.
- A mobile client receives only an opaque action ID, display label, and preset
  kind. Paths and arguments stay on the Windows host.
- The existing global/per-device **Allow paired devices to start
  applications** permission gates both discovery and execution.
- Presets are fixed host-owned definitions. A client cannot change their
  target.
- A custom entry stores an executable path and argument string separately. It
  does not invoke a shell or accept shell syntax as an executable target.
- Custom executable paths must be absolute, point to an existing local file,
  use an executable extension, and contain no control characters.
- Labels, IDs, action counts, paths, and arguments have explicit size limits.
- Every custom entry requires a local warning confirmation before it is saved.
  Editing a custom command requires confirmation again.
- A custom target is validated again immediately before every launch. A file
  removed or replaced by an invalid target is not executed.
- Unknown, malformed, disabled, or no-longer-configured IDs are rejected
  without falling back to arbitrary process execution.
- Application logs may record the opaque action ID and outcome, but never the
  custom path or arguments.

## Compatibility plan

The existing `remote.launch` messages for the fixed YouTube and Kodi actions
remain unchanged. Configurable buttons use a new `app.launch` message so the
old fixed-action allowlist cannot accidentally become a general command
surface. Authenticated host status gains an optional `appLaunchActions` array.
Older mobile clients ignore it; newer clients treat a missing array as no
configured buttons.

## Detailed implementation plan

### 1. Host model, validation, and persistence

- Add a focused application-launch settings service and immutable models.
- Define stable preset IDs for Browser, Spotify, VLC, and PowerPoint.
- Store only enabled preset IDs and explicitly approved custom entries in the
  current-user settings area.
- Normalize and validate all loaded data; invalid stored entries are omitted.
- Keep persistence designed for a clean state, without migrations for older
  shapes.
- Add unit tests for defaults, limits, invalid paths, invalid arguments,
  duplicate IDs, stored-data normalization, and preset resolution.

### 2. Host execution service

- Extend the remote action service with execution by opaque configured ID.
- Resolve preset targets entirely on the host:
  - Browser: launch the user's default browser.
  - Spotify: launch the registered Spotify URI/application.
  - VLC: resolve the registered App Paths entry or a known install location.
  - PowerPoint: resolve the registered App Paths entry or a known Office
    install location.
- Launch custom executables without a command shell and pass the optional
  argument string directly to the executable through `ProcessStartInfo`.
- Revalidate custom paths immediately before process creation.
- Return structured success/failure outcomes for protocol feedback and tests.

### 3. Protocol and policy

- Advertise approved action summaries only after authentication and only when
  the effective application-launch permission is enabled.
- Validate `app.launch` as an opaque bounded ID with no extra properties.
- Resolve and execute IDs only after authentication and permission checks.
- Return an `app.launch.result` message so mobile does not imply success merely
  because a socket send succeeded.
- Broadcast updated status when the host action list changes.
- Add in-memory `TestServer` coverage for authentication, discovery,
  permission denial, valid execution, stale IDs, malformed IDs, and result
  messages.

### 4. Windows Preferences UI

- Add a WPF **Application launch buttons** accordion section near Remote
  defaults.
- Offer opt-in preset toggles for Browser, Spotify, VLC, and PowerPoint, explain
  that desktop applications must be installed, and report launch failures.
- Show configured custom entries in a WPF list with Add, Edit, and Remove
  actions.
- Use a themed WPF editor for label, executable path, and optional arguments.
- Validate inline before saving and show a local warning confirmation that
  names the exact label and target.
- Keep actions outside growing scroll regions and add host UI tests for section
  order and control availability.

### 5. Mobile Remote UI

- Parse and retain host-advertised action summaries in connection state.
- Add an **Apps** entry to the Remote utility panel only when actions are
  available.
- Present configured actions in a responsive sheet/panel without exposing host
  paths or arguments.
- Show pending, success, denied, stale, and failed feedback where the action was
  initiated.
- Add component and connection/protocol tests for rendering, sending opaque
  IDs, result feedback, and absence when no actions are configured.

### 6. Documentation and roadmap

- Update `docs/protocol.md`, `docs/features.md`, `docs/setup.md`,
  `docs/troubleshooting.md`, `README.md`, and public site copy where the new
  behavior changes current user guidance.
- Remove completed configurable-launch bullets from `docs/todo.md` and correct
  the stale recommended-next numbering/order.
- Update `docs/architecture.md` if the service boundary changes.
- Record final files, validation commands, results, limitations, and manual
  follow-ups in this document.

### 7. Validation

Run sequentially, per `AGENTS.md`:

1. Targeted mobile tests.
2. Targeted host tests.
3. `npm run build`.
4. `npm test`.
5. `npm run size:report`.
6. Targeted WPF and browser UI inspection where automation is practical.

Release packaging is not planned because this is feature work rather than a
release-candidate packaging request. If implementation changes packaging or
generated host assets unexpectedly, add `npm run package:win` before handoff.

## Work log

### 2026-07-13: Audit and design

- Read the repository instructions, roadmap, UI guidance, architecture,
  current fixed launch implementation, permission model, protocol, mobile
  Remote composition, and relevant tests.
- Confirmed the working tree was clean before implementation.
- Chose a separate opaque-ID protocol rather than expanding `remote.launch` to
  accept arbitrary strings.
- Chose local approval at create/edit time plus launch-time revalidation. This
  makes each stored custom command an explicit host approval while keeping
  normal remote use practical.

## Implementation results

- Added host-owned launch settings and execution boundaries in
  `AppLaunchSettings`, `AppLaunchService`, and a dedicated WPF preference
  section/editor. Presets are opt-in; custom commands require absolute existing
  `.exe` paths and explicit approval on every add or edit.
- Added authenticated `app.launch` and `app.launch.result` protocol messages.
  The host advertises only opaque IDs, labels, and kinds, and advertises nothing
  when the effective application-launch permission is disabled.
- Added a responsive Applications section to the mobile Remote utility panel,
  including pending, success, timeout, denial, stale-action, and failure
  feedback.
- Kept the existing fixed YouTube and Kodi `remote.launch` contract intact.
- Added host settings/protocol/UI tests and mobile protocol/hook/component
  tests. No production dependency was added.
- Updated the current-state README, architecture, feature, setup, protocol,
  troubleshooting, roadmap, and public-site documentation.

### 2026-07-13: Validation follow-up refinements

- Added `AllowRemoteAppLaunch` to the persisted per-device override record and
  the Devices-page tri-state permission UI. **Use global**, **Allow**, and
  **Block** now affect both advertised configurable buttons and execution for
  that paired device.
- Added protocol coverage for both override directions: a device can block a
  globally allowed launch permission or explicitly allow one that is globally
  blocked.
- Application launch result feedback below the Remote Fn buttons now clears
  after four seconds, including successful and failed results. Starting another
  action clears the previous result immediately.
- Repository history confirmed `100lvh` was the viewport behavior before commit
  `ab2068a` changed the app to `100dvh`. Browser tabs continue to use `dvh`;
  detected installed/standalone PWAs use `lvh`, with `vh` as the compatibility
  fallback. The settings drawer and expanded trackpad share the same variable.
- Preset and custom application buttons now use host-managed labels limited to
  1–10 characters. Presets default to **WWW**, **Spotify**, **VLC**, and
  **PPT**, and enabled presets expose a compact **Button label** editor in
  Preferences. Valid edits save automatically without a separate save button,
  and the input stops accepting additional text after 10 characters.
  The mobile Fn grid renders the exact advertised label, chooses columns from a
  120-pixel minimum, stacks each icon above its label, and applies modest
  length-based font sizing without ellipsis so the complete label remains
  visible.

### 2026-07-13: Host label editor and test-suite audit

- Replaced the preset label save buttons with automatic persistence on valid
  text edits. Invalid empty text remains visibly marked and is not persisted.
- Added one **Button label** heading over the preset label column and reduced
  each input to a fixed 32-pixel height.
- Changed **Add custom button** to size to its label instead of stretching
  across the full settings panel.
- Added separation below the **Custom buttons** heading and normalized the
  preset checkbox margins so each 32-pixel label input is vertically centered
  against its corresponding checkbox row.
- Removed the shared CheckBox template's duplicate internal bottom margin; the
  existing helper-level row margin now supplies spacing without shifting the
  rounded checkbox content below its aligned label field.
- Moved custom-row spacing to the outer bordered card so separate frames no
  longer touch, and fixed both preset and dialog label fields to a compact
  140-pixel width appropriate for the 10-character limit.
- Converted mobile application-launch pending/result feedback to a global
  viewport-fixed toast rendered at the app root. It stays in the visible
  top-left corner with safe-area spacing, above panels, drawers, scrolling, and
  landscape layouts while messages such as “Started PPT.” are visible.
- Both preset and custom label inputs use WPF's `MaxLength` with the shared
  10-character limit, so an eleventh typed character is not accepted. The
  settings boundary independently rejects longer values from non-UI callers.
- Reviewed all 22 host test files and 29 mobile test files by test name, then
  scanned both suites for assertions coupled only to WPF/DOM structure, CSS
  classes, exact placement, and duplicated case matrices.
- Removed 12 mobile cases: eight duplicate shortcut callback cases plus four
  tests of placeholder copy or exact DOM/CSS arrangement. The remaining
  shortcut matrix still covers all removed callback mappings.
- Removed two host cases: a WPF permission-row test that could pass by finding
  generic buttons in unrelated rows, and a global-denial WebSocket case that
  duplicated the effective-denial path already covered by the per-device
  integration test and permission-resolution unit test.
- Reduced the large host navigation smoke test to page navigation, accordion,
  scrolling, and diagnostics view-state boundaries instead of repeating the
  literal content of every setting control.
- Retained tests for security validation, authenticated protocol behavior,
  permission inheritance and both override directions, opaque action IDs,
  timeout/result clearing, pairing recovery, and native input cleanup.

## Validation results

- Initial targeted host tests: passed, 38 tests. Follow-up permission and UI
  coverage: passed, 46 tests.
- Initial targeted mobile tests: passed, 88 tests. Follow-up launch-feedback
  coverage: passed, 81 tests.
- Label-editor and fit follow-up: passed, 18 focused host tests and 86 focused
  mobile tests.
- `npm run build`: passed; Vite and .NET host builds completed with zero .NET
  warnings and errors.
- `npm test`: passed after the label-editor and test-audit follow-up; 279 mobile
  tests across 29 files and 162 host tests.
- `npm run size:report`: passed as a report-only check. The reviewed
  `WebHostServiceTests.cs` exception is now approximately 35 KB and is recorded
  in `docs/architecture.md`; production sources remain below the 25 KB split
  threshold, with several files still flagged for normal review above 20 KB.
- `git diff --check`: passed. Git reported only the repository's normal
  LF-to-CRLF working-copy warnings.
- WPF dark-theme inspection: passed for accordion placement, preset controls,
  scrolling, add/edit controls, editor layout, and warning-review flow.
- Mobile browser inspection: passed at 393 x 852 and 375 x 667. The app button
  remained at least 44 pixels high, fit without horizontal overflow, and stayed
  visible in the responsive utility panel.
- Follow-up viewport inspection at 393 x 852: browser mode selected `100dvh`,
  the app shell matched the 852-pixel visual viewport, and the loaded stylesheet
  exposed the installed-mode `100lvh` rule plus its `100vh` fallback. The
  temporary viewport override, tab, and development server were removed.
- Label fit inspection at 320 x 667: the application grid retained two columns;
  measured Browser, VLC, PPT, and custom labels had equal client/scroll widths
  with no overflow. WPF inspection confirmed all four preset label editors fit
  the Application launch buttons accordion. The isolated host, pairing store,
  browser tab, and viewport override were removed afterward.
- Follow-up WPF inspection confirmed the single **Button label** heading,
  compact 32-pixel preset fields, automatic-save accessibility descriptions,
  and the absence of separate save buttons. The hard 10-character UI limit is
  configured on both host label inputs; the attempted interactive keystroke
  check was stopped when concurrent user input was detected. The one temporary
  PowerPoint-label character introduced during that attempt was restored and
  the stored label was verified as **PPT** before the isolated host was stopped.
- Pairing/UI inspection used an isolated loopback host with
  `--isolated-test-mode`; the temporary pairing store and process were removed
  afterward. The temporary Browser preset was restored to disabled.

## Remaining manual checks

- Launch each installed preset on a normal paired device: default Browser,
  Spotify, VLC, and PowerPoint. Missing desktop applications should produce
  friendly failure feedback.
- Add a harmless custom executable, exercise optional arguments, edit it,
  remove it, and confirm the mobile list updates after each host-side change.
- Recheck the new host section in light/system themes and at the Windows display
  scales used for release testing. Dark theme was inspected during this pass.
- Confirm per-device application-launch permission changes hide and restore the
  configured buttons on the intended physical phone.
- On the affected bookmarked/installed PWA, refresh in portrait several times,
  background/foreground the app, and rotate landscape then portrait. Confirm
  the bottom navigation consistently reaches the actual screen edge without
  behaving as if browser chrome were present.
- Release packaging was not run because no packaging or release metadata was
  changed. Run the normal release validation sequence if this feature is rolled
  into a release candidate.
