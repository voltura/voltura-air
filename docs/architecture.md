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
Windows WebHostService -> validation/policy -> input, audio, and remote services
                                                |
                                                v
                                         Windows platform APIs
```

React components send typed intents through `useVolturaAirConnection`; the connection subsystem owns pairing, reconnects, acknowledgements, and protocol parsing. On the host, `WebHostService` validates and routes messages, while `HostPermissions` and paired-device settings decide policy before Windows adapters execute an approved command.

## Host subsystems

- `Program.cs` and `WpfHostRuntime.cs` compose the one-host tray application and own startup rollback and process-resource shutdown. `WpfTrayApplicationContext.*.cs` owns tray presentation, dispatcher one-shots, menu state, icons, and subscriptions; it requests commands and application shutdown but does not own service internals. `WebHostService.*.cs` separates host lifetime, network/origin selection, socket-session routing, socket transport, status broadcasting, and command operations while retaining one service owner for HTTP/WebSocket lifetime, bounded messages, deadlines, per-socket send serialization, authenticated status, and static PWA serving. `AppDeveloperSettings.cs` owns the cached, default-off **Enable alpha features** umbrella gate; individual alpha features own their capability advertisement and command-boundary enforcement.
- `PairingManager.*.cs`, `PairingStore.cs`, and `PairingAttemptRateLimiter.cs` own authentication, bounded and validated atomic pairing persistence, and per-device permissions and pointer-speed overrides.
- `ClientMessageValidator.cs` decodes each bounded authenticated input message once into a `ValidatedInputCommand`; `InputDispatcher.cs` consumes that command without rereading JSON and translates it for `SendInputInjector.cs`. The injector serializes native calls, reuses its single-input movement buffer, and sends text in bounded batches with partial-send cleanup. `PresentationCommands.cs` owns the fixed target-specific presenter shortcut allowlist. `TextDestination.cs` owns safe focused/clipboard/configured text delivery. `WindowsProcessIntegrity.cs` and `PointerHighlightForegroundMonitor.cs` use a single foreground event hook solely to block remote input when a higher-integrity app owns the foreground; they have no cursor responsibilities.
- `AppLog.*.cs` separates public models, bounded single-writer persistence, and bounded queries. `WpfHostRuntime` disposes the log after every producer; a standalone `WebHostService` disposes only a logger it created itself.
- `CustomPointerService.cs` builds Windows cursors from the packaged maximum-size templates, preserves the Windows role mapping, applies them through the Windows cursor API, and reloads the user's configured cursor scheme on disable or normal exit. It owns the enabled, size, RGB, and default-on recovery-watchdog settings.
- `CursorWatchdogService.cs` owns the native monitor while Custom pointer and **Use cursor recovery watchdog** are both enabled. `VolturaAir.CursorWatchdog.exe` starts a ready-confirmed monitor outside the live host process tree. The monitor waits on the host process handle, restores the configured Windows cursor scheme after unexpected termination, and exits. Normal host shutdown restores the cursor before stopping the monitor.
- `RemoteActionExecutor.*.cs`, `AppLaunchService.cs`, `SystemAudioController.cs`, `SystemPowerController.cs`, `WindowsDisplayActionController.cs`, and `AwakeService.cs` own their focused platform functions. `MainWindow.xaml` and the XAML views under `Features/` own page and reusable-section composition for Connect, Connection, Devices, Preferences, and Diagnostics. `MainWindow.*.cs` supplies data and behavior. WPF/tray presentation requests lifecycle actions while `Program.cs` and `WpfHostRuntime.cs` perform shutdown.

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
reviews. Mixed host protocol, logging, tray, and Preferences responsibilities
were split before the remaining exceptions were recorded. `docs/ui-system.md`
applies the same review threshold specifically to presentation ownership.

The production mobile build also enforces the measured JavaScript budget after
Vite compression: at most 560 KB raw and 132 KB Brotli across emitted JavaScript
assets. Budget changes require an intentional ownership and payload review.

## Long-lived resource inventory

| Owner | Long-lived resources | Deterministic cleanup and coverage |
| --- | --- | --- |
| `WpfHostRuntime` | App log, web host, input guard, custom pointer/watchdog, tray context, windows, and single-instance activation | Startup rollback and reverse-order `DisposeAsync`; host runtime/startup tests cover failure and shutdown paths. |
| `WebHostService` | Lifetime cancellation, bounded status channel/worker, session semaphore, ASP.NET host, registered sockets/send gates, settings and pairing subscriptions, owned adapters | `StopAsync` cancels, completes the channel, closes sockets with deadlines, awaits workers/host, and `DisposeAsync` releases subscriptions and owned services; protocol/connection tests cover limits, failures, disconnects, and shutdown. |
| `AppLog` | Bounded channel and single writer task | `DisposeAsync` completes the channel and awaits the writer after accepted-entry flush; log tests cover backpressure, read/write failure, pruning, flush, and disposal. |
| `AwakeService` | Dedicated request thread, bounded blocking collection, expiration timer, execution-state ownership | `Dispose` stops intake/timer, wakes and joins the thread, then releases Windows execution state; awake tests cover transitions, expiration, failure, and cleanup. |
| `SingleInstanceCoordinator` | Named synchronization objects and registered activation wait | `Dispose` unregisters the wait and releases handles; focused single-instance tests cover activation and disposal. |
| `PointerHighlightForegroundMonitor` and `TextDestinationDraftStore` | Windows foreground hook/dispatcher timer and periodic draft-cleanup timer | Each service stops its timer, unregisters native/event state, and is disposed by its composition owner; focused platform/service tests cover inactive and cleanup behavior. |
| `WpfTrayApplicationContext` | Notify icon/menu/icons, subscriptions, startup and disconnect one-shots | `Dispose` cancels owned one-shots, unsubscribes, hides/disposes tray resources, and releases icons. `OwnedDispatcherTimer` tests cover fire-once and dispose-before-fire behavior. |
| Mobile connection/PWA/input hooks | WebSocket listeners, health/retry/feedback timers, page listeners, pointer capture, animation frames, and speech events | React effect cleanup closes/releases each acquired resource. Connection lifetime tests cover listener release, reconnect replacement, suspension, timeouts, and unmount; focused input/PWA tests cover their exit paths. |

## Invariants

- Only one Windows host process runs per signed-in user.
- Automated protocol tests use ASP.NET Core `TestServer`; temporary UI hosts use `--isolated-test-mode`, bind only to loopback, use disposable host settings and pairing data, and must not run beside a normal host.
- Custom pointer is host-wide because it is one real Windows cursor scheme. Size, color, and recovery-watchdog choice are host-only; paired devices may toggle only its host-wide enabled state through the authenticated protocol. Development and capture launchers terminate the host without recursively terminating its orphaned watchdog, then wait for cursor restoration before launching another host.
- Typed text, pointer coordinates, and pairing credentials never enter application logs.
- Every long-lived worker has one service owner, bounded input, cancellation, and a shutdown wait. UI event handlers may enqueue service work but must not create competing fire-and-forget workers.
- Alpha availability is read from the host's cached setting and propagated through the existing event-driven status broadcaster. It is never polled or read from the registry on input/render hot paths. Disabled alpha features allocate no feature-specific timers, subscriptions, native resources, workers, or network activity.
