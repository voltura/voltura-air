# Architecture

Voltura Air has two runtime halves: a React PWA that captures user intent and a Windows host that authenticates clients, applies permissions, and performs Windows operations. The typed client protocol in `apps/mobile-web/src/foundation/protocol/messages.ts` and the wire authority in `docs/protocol.md` form their compatibility boundary.

The mobile source structure is `app`, `features`, `ui`, and `foundation`.
Architecture lint rejects source outside those roots and requires every
foundation file to live under a named domain.

## Dependency direction

```text
mobile app shell -> capability slices -> UI primitives
       |                    |
       +--------------------+
                         |
                         v
                 typed foundation layer
       (connection, protocol, input, settings, storage, platform)
                         |
                         v
Windows WebHostService -> WebSocketSessionHandler -> validation and policy
                                                     |
                                                     v
                                   focused command handlers and platform APIs
```

React components send typed intents through `useVolturaAirConnection`; the connection subsystem owns pairing, reconnects, acknowledgements, and protocol parsing. On the host, `WebHostService` owns ASP.NET lifetime and composes `WebSocketSessionHandler`. The session handler authenticates and routes validated messages to focused command handlers, while `HostStatusPayloadFactory`, `HostPermissions`, and paired-device settings decide policy before Windows adapters execute an approved command.

## Host subsystems

- `Program.cs` and `WpfHostRuntime.cs` compose the one-host tray application and own startup rollback and process-resource shutdown. `WpfTrayApplicationContext.cs` owns the notify icon, themed menu, icons, and shutdown requests. `OwnedDispatcherAction` is the small host-owned primitive for recurring latest-state UI notifications: it retains at most one dispatcher callback and aborts it on disposal. The main window, tray connection feedback, foreground monitor, WPF theme/accessibility refresh, and Diagnostics refresh session own their instances. `TrayAwakeMenuController.cs` owns Keep awake menu state and its service subscription.
- `WebHostService.cs` owns ASP.NET/static-PWA lifetime and the bounded session semaphore. `WebHostNetwork.cs` owns adapter-independent URL, origin, and rate-limit-key helpers. `WebSocketSessionHandler.cs` owns pairing and authenticated protocol state. `WebSocketTransport.cs` owns bounded framing, close/send deadlines, active sockets, and per-socket send serialization. `HostStatusBroadcaster.cs` owns the single capacity-one coalescing status worker, starts it explicitly on the thread pool, and keeps all worker and close continuations independent of WPF. `HostStatusPayloadFactory.cs` owns capability and status payload construction. Focused command handlers own input, power, Awake, presentation, external launch, text-transfer, clipboard, and command-log behavior. `AppDeveloperSettings.cs` owns the cached, default-off **Enable alpha features** umbrella gate; individual alpha features advertise capability only while enabled and enforce the gate at their command boundary.
- `PairingManager.cs` owns the synchronization boundary and publishes pairing/device events. `PairingTokenAuthority.cs` owns pairing-code creation, rotation overlap, validation, and invalidation. `PairedDeviceRegistry.cs` owns normalized paired-device state, active connection counts, device permissions, and pointer-speed overrides. `PairingStore.cs` owns bounded, validated, atomic persistence, and `PairingAttemptRateLimiter.cs` owns per-source authentication throttling.
- `ClientMessageValidator.cs` decodes each bounded authenticated input message once into a `ValidatedInputCommand`; `InputDispatcher.cs` consumes that command without rereading JSON and translates it for `SendInputInjector.cs`. The injector serializes native calls, reuses its single-input movement buffer, and sends text in bounded batches with partial-send cleanup. `PresentationCommands.cs` owns the fixed target-specific presenter shortcut allowlist. `TextDestination.cs` owns safe focused/clipboard/configured text delivery. `WindowsProcessIntegrity.cs` and `PointerHighlightForegroundMonitor.cs` use a single foreground event hook solely to block remote input when a higher-integrity app owns the foreground; they have no cursor responsibilities.
- `AppLog.Models.cs` defines separate writer and reader contracts. `AppLog.cs` owns the bounded single-writer queue, flush barriers, backpressure records, and change notifications; `AppLogFileStore.cs` owns JSONL append, retention, bounded queries, and deletion. Diagnostics owns a per-view `ApplicationLogRefreshSession` that runs one background read at a time, coalesces automatic notifications into one latest-filter follow-up, and suppresses results after unload. `WpfHostRuntime` disposes the log after every producer; a standalone `WebHostService` disposes only a logger it created itself.
- `CustomPointerService.cs` builds Windows cursors from the packaged maximum-size templates, preserves the Windows role mapping, applies them through the Windows cursor API, and reloads the user's configured cursor scheme on disable or normal exit. It owns the enabled, size, RGB, and default-on recovery-watchdog settings.
- `CursorWatchdogService.cs` owns the native monitor while Custom pointer and **Use cursor recovery watchdog** are both enabled. `VolturaAir.CursorWatchdog.exe` starts a ready-confirmed monitor outside the live host process tree. One monitor is allowed per host process ID. The monitor blocks on a synchronized host-process handle, restores the configured Windows cursor scheme after every confirmed host exit, optionally signals successful restoration for validation, and exits. The host confirms readiness before replacing a cursor. Feature-disable paths restore cursors before explicitly stopping the monitor. Final host shutdown restores cursors, detaches the managed process wrapper without killing the monitor, and lets the monitor perform the harmless second restoration after actual host exit.
- `RemoteActionExecutor.cs` routes allowlisted remote modes to `YoutubeRemoteAction.cs` and `KodiRemoteAction.cs`; `WindowsWindowActivator.cs` owns Windows window discovery and activation. `AppLaunchService.cs`, `SystemAudioController.cs`, `SystemPowerController.cs`, and `WindowsDisplayActionController.cs` own focused platform functions. `AwakeService.cs` exposes asynchronous operations backed by one dedicated execution thread and a non-blocking queue bounded to eight requests. It commits state only after native acceptance, skips abandoned queued work, reconciles late native completion to the committed state, and bounds asynchronous shutdown even if the background native call cannot return. `MainWindow.xaml` is the WPF shell and page-composition root; XAML views and feature-owned controllers or models under `Features/` own each page's presentation, non-visual state, and behavior. The Connect feature's `PairingLinkController` owns pairing-link construction, host hints, rotation, and refresh state, while `PairingQrCodeRenderer` owns QR bitmap creation and its native GDI handle. WPF/tray presentation requests lifecycle actions while `Program.cs` and `WpfHostRuntime.cs` perform shutdown.

### Host presentation ownership

`MainWindow` owns only window-level concerns: navigation, shell visibility, visual
composition, and the subscriptions needed to refresh visible views. It forwards
user intent and host events to feature-owned types; it does not own feature
rules, persistence, platform operations, or non-visual feature state.

`MainWindow.xaml.cs` is the only `MainWindow` source file; its partial declaration
exists solely for the WPF-generated XAML half. Page behavior is grouped under
`Features/Connect`, `Features/Connection`, `Features/Devices`,
`Features/Preferences`, and `Features/Diagnostics`. Each page controller owns
its view construction and interaction state. Focused preference sections own
application/logging, Keep awake, global permissions, app-launch,
text-destination, custom-pointer, and developer/Windows-policy behavior, while
the preferences page controller coordinates page-level settings and preserved
scroll/expansion state. Domain-neutral WPF construction, toast, clipboard, and
window-artwork helpers live under `Ui/`.

Adding another `MainWindow` partial file is not a feature boundary. Move a
cohesive responsibility into a named type under `Features/<feature>` (or into an
existing host service when that service already owns the state) and test that
owner without constructing a WPF window. Partial types remain appropriate where
WPF XAML or source-generated interop requires them, but not as the default way
to split a growing class.

## Mobile subsystems

- `App.tsx` is the thin composition root. `app/` owns shell state, the stable
  application frame, safe-area allocation, top-level navigation, and global
  overlay state. `features/<capability>/` owns each user-facing vertical slice
  with its tests and CSS. `ui/` owns domain-neutral controls, feedback, and
  overlay primitives. Feature surfaces emit typed intents rather than
  controlling sockets or persistence directly.
- `foundation/<domain>/` owns typed non-presentation boundaries for connection, diagnostics, identity, input, pairing, platform, protocol, PWA lifecycle, and persisted settings. `useVolturaAirConnection` is the public communication entry point. It composes focused command and persistence hooks with `useConnectionSocketLifecycle`, whose single effect owns the socket state machine, listeners, health and retry timers, background suspension, and cleanup.
- `app/` owns mode-tab and cross-feature split-layout composition contracts. A
  feature owns its internal responsive composition, local models, timers,
  validation, tests, and stylesheet imports. Global CSS is limited to generated
  tokens, base behavior, the shell, and shared primitives.
- The shell exposes normalized viewport and safe-area contracts to features.
  Features do not set shell geometry or branch on device identity. Ordinary
  responsive layout uses CSS Grid, Flexbox, containment, and media or container
  queries. Runtime viewport observation is reserved for shared overlays that
  must follow the transient visual viewport while a software keyboard is open.
- A shared overlay primitive owns native dialog behavior, focus and dismissal,
  safe-area bounds, and visual viewport listeners. The invoking feature owns
  dialog content, validation, actions, and whether the dialog is present. It
  does not duplicate the primitive's mechanics.
- Persisted client formats are owned by the appropriate foundation domain and
  follow the current design policy in the root `AGENTS.md`.

## Source ownership and size

File size is a review signal, not a mechanical architecture rule. Review an
actively maintained source file when it exceeds either 300 lines or 12 KB. Treat
more than 500 lines or 20 KB as a strong warning that ownership may be mixed.

Split by responsibility, lifecycle, or dependency boundary, not arbitrary line
count. A cohesive algorithm, interop declaration set, schema, or data table may
remain larger when separating it would hide ownership or increase complexity;
record the reason during review. Composition roots, pages, feature components,
and service classes should rarely need that exception.

`npm run size:report` applies these repository-wide review and warning thresholds
to actively maintained source while excluding generated and vendored output.
Strong warnings have their current cohesive-ownership rationale in
`scripts/source-size-reviews.json`; `npm run size:check` rejects missing or stale
reviews. `npm run host:ownership:check` rejects a host type spread across
multiple maintained partial source files; framework and generated partial types
remain confined to one maintained source file. `docs/ui-system.md` applies the
same review threshold specifically to presentation ownership.

The production mobile build also enforces the measured initial JavaScript budget
after Vite compression: at most 568 KB raw and 136 KB Brotli for the module
scripts referenced directly by `index.html`. The checker still reports total
emitted JavaScript so on-demand chunks remain visible during review, but async
feature chunks do not count against the initial control-surface budget. The July
2026 review raised the previous 560 KB / 132 KB limits narrowly for the
synchronous P-256 reconnect signer used on HTTP LAN origins; keeping it in the
initial bundle avoids a pairing-time network fetch. Budget changes require an
intentional ownership and payload review.

## Long-lived resource inventory

| Owner | Long-lived resources | Deterministic cleanup and coverage |
| --- | --- | --- |
| `WpfHostRuntime` | App log, web host, input injector, custom pointer/watchdog, foreground monitor, draft cleanup, tray context, and main window | Startup rollback and reverse-order `DisposeAsync`; host runtime/startup tests cover failure and shutdown paths. |
| `WebHostService` | ASP.NET application, static PWA mapping, session semaphore, composed protocol services, and owned platform adapters | `StopAsync` aborts active sockets and stops ASP.NET with a deadline. `DisposeAsync` stops the status owner, disposes the app/transport, and releases owned adapters; protocol/connection tests cover limits, failures, disconnects, and shutdown. |
| `WebSocketTransport` | Registered sockets and per-socket send gates | Bounded close/send operations and `Dispose` retire connection state; connection tests cover serialized sends, unregister races, limits, and shutdown. |
| `HostStatusBroadcaster` | Bounded coalescing channel, cancellation source, worker, settings/pairing subscriptions, and tracked revocation-close tasks | `DisposeAsync` unsubscribes, cancels and completes the channel, then awaits the worker and tracked closes; status tests cover propagation and disconnect behavior. |
| `AppLog` | Bounded channel and single writer task | `DisposeAsync` completes the channel and awaits the writer after accepted-entry flush; log tests cover backpressure, read/write failure, pruning, flush, and disposal. |
| `AwakeService` | Dedicated request thread, eight-request bounded queue, operation deadlines, expiration timer, and committed execution-state ownership | `DisposeAsync` stops intake and the timer, cancels queued work, requests restoration on the execution thread, and bounds shutdown; late native completion reconciles before more work, while a permanently blocked bridge may retain only its background thread. Awake tests cover ordering, bounds, timeout/cancellation, reconciliation, expiration, failure, and cleanup. |
| `SingleInstanceCoordinator` | Named synchronization objects and registered activation wait | `Dispose` unregisters the wait and releases handles; focused single-instance tests cover activation and disposal. |
| `PointerHighlightForegroundMonitor` and `TextDestinationDraftStore` | Windows foreground hook/dispatcher timer and periodic draft-cleanup timer | Each service stops its timer, unregisters native/event state, and is disposed by its composition owner; focused platform/service tests cover inactive and cleanup behavior. |
| `CursorWatchdogService` | Managed wrapper for the ready native monitor | Explicit feature stop terminates the monitor; final `Dispose` detaches the wrapper so the native process remains until host exit. Watchdog acceptance tests cover startup results, PID reuse, forced termination, restoration, and final detachment. |
| `WpfTrayApplicationContext`, `TrayConnectionFeedbackController`, and `TrayAwakeMenuController` | Notify icon/menu/icons, theme/Awake/connection subscriptions, and startup/disconnect one-shots | Their `Dispose` methods unsubscribe, cancel owned one-shots, hide/dispose tray resources, and release icons. Tray and `OwnedDispatcherTimer` tests cover state and cleanup behavior. |
| Mobile connection/PWA/input hooks | WebSocket listeners, health/retry/feedback timers, page listeners, pointer capture, animation frames, and speech events | React effect cleanup closes/releases each acquired resource. Connection lifetime tests cover listener release, reconnect replacement, suspension, timeouts, and unmount; focused input/PWA tests cover their exit paths. |

## Invariants

- Only one Windows host process runs per signed-in user.
- Automated protocol tests use ASP.NET Core `TestServer`; temporary UI hosts use `--isolated-test-mode`, bind only to loopback, use disposable host settings and pairing data, and must not run beside a normal host.
- Custom pointer is host-wide because it is one real Windows cursor scheme. Size, color, and recovery-watchdog choice are host-only; paired devices may toggle only its host-wide enabled state through the authenticated protocol. Development and capture launchers terminate the host without recursively terminating its orphaned watchdog, then wait for cursor restoration before launching another host.
- Typed text, pointer coordinates, and pairing credentials never enter application logs.
- Every long-lived worker has one service owner, bounded input, cancellation, and a shutdown wait. UI event handlers may enqueue service work but must not create competing fire-and-forget workers.
- Alpha availability is read from the host's cached setting and propagated through the existing event-driven status broadcaster. It is never polled or read from the registry on input/render hot paths. Disabled alpha features allocate no feature-specific timers, subscriptions, native resources, workers, or network activity.
