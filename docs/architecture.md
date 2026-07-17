# Architecture

Voltura Air has two runtime halves: a React PWA that captures user intent and a Windows host that authenticates clients, applies permissions, and performs Windows operations. The protocol in `apps/mobile-web/src/protocol.ts` and `docs/protocol.md` is their compatibility boundary.

The target mobile structure is `app`, `features`, `ui`, and `foundation`. Until
P0 completes, the root-level foundation paths listed below remain part of the
current source layout. New and refactored code moves a coherent ownership
boundary and its tests into the target structure.

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

- `Program.cs` and `WpfHostRuntime.cs` compose the one-host tray application and own startup rollback and process-resource shutdown. `WpfTrayApplicationContext.cs` requests user commands and application shutdown but does not own service internals. `WebHostService.*.cs` owns HTTP/WebSocket lifetime, bounded messages, receive/send/close deadlines, per-socket send serialization, the single coalescing status broadcaster, authenticated status, and static PWA serving. `AppDeveloperSettings.cs` owns the cached, default-off **Enable alpha features** umbrella gate; individual alpha features own their capability advertisement and command-boundary enforcement.
- `PairingManager.*.cs`, `PairingStore.cs`, and `PairingAttemptRateLimiter.cs` own authentication, bounded and validated atomic pairing persistence, and per-device permissions and pointer-speed overrides.
- `ClientMessageValidator.cs` decodes each bounded authenticated input message once into a `ValidatedInputCommand`; `InputDispatcher.cs` consumes that command without rereading JSON and translates it for `SendInputInjector.cs`. The injector serializes native calls, reuses its single-input movement buffer, and sends text in bounded batches with partial-send cleanup. `PresentationCommands.cs` owns the fixed target-specific presenter shortcut allowlist. `TextDestination.cs` owns safe focused/clipboard/configured text delivery. `WindowsProcessIntegrity.cs` and `PointerHighlightForegroundMonitor.cs` use a single foreground event hook solely to block remote input when a higher-integrity app owns the foreground; they have no cursor responsibilities.
- `AppLog.cs` owns the optional bounded single-writer filesystem queue and shutdown flush. `WpfHostRuntime` disposes it after every producer; a standalone `WebHostService` disposes only a logger it created itself.
- `CustomPointerService.cs` builds Windows cursors from the packaged maximum-size templates, preserves the Windows role mapping, applies them through the Windows cursor API, and reloads the user's configured cursor scheme on disable or normal exit. It owns the enabled, size, RGB, and default-on recovery-watchdog settings.
- `CursorWatchdogService.cs` owns the native monitor while Custom pointer and **Use cursor recovery watchdog** are both enabled. `VolturaAir.CursorWatchdog.exe` starts a ready-confirmed monitor outside the live host process tree. The monitor waits on the host process handle, restores the configured Windows cursor scheme after unexpected termination, and exits. Normal host shutdown restores the cursor before stopping the monitor.
- `RemoteActionExecutor.*.cs`, `AppLaunchService.cs`, `SystemAudioController.cs`, `SystemPowerController.cs`, `WindowsDisplayActionController.cs`, and `AwakeService.cs` own their focused platform functions. `MainWindow.*.cs` and `WpfTrayApplicationContext.cs` own WPF/tray presentation and dispatcher-affine state; they request lifecycle actions while `Program.cs` and `WpfHostRuntime.cs` perform shutdown.

## Mobile subsystems

- `App.tsx` is the thin composition root. `app/` owns shell state and global overlays, `features/<capability>/` owns each user-facing vertical slice with its tests and CSS, and `ui/` owns domain-neutral controls and feedback primitives. Feature surfaces emit typed intents rather than controlling sockets or persistence directly.
- `foundation/<domain>/` is the target for typed non-presentation boundaries. It owns identity, credentials, connection health, acknowledgements, congestion limits, reconnect policy, protocol translation, persisted settings, and platform lifecycle. `useVolturaAirConnection` is the public communication entry point exposed from that layer.
- The current root `connection/`, `input/`, `pairing/`, `pwa/`, and `settings/` directories and root modules such as `appStorage.ts`, `pcProfiles.ts`, `gestures.ts`, and `keyboardDelta.ts` are transitional foundation code. The interim architecture lint rule treats non-`app`, non-`features`, and non-`ui` source as foundation until coherent domains move under `foundation/`; it must tighten as migration removes those roots.
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
`docs/ui-system.md` applies the same review threshold specifically to
presentation ownership.

## Invariants

- Only one Windows host process runs per signed-in user.
- Automated protocol tests use ASP.NET Core `TestServer`; temporary UI hosts use `--isolated-test-mode`, bind only to loopback, use disposable host settings and pairing data, and must not run beside a normal host.
- Custom pointer is host-wide because it is one real Windows cursor scheme. Size, color, and recovery-watchdog choice are host-only; paired devices may toggle only its host-wide enabled state through the authenticated protocol. Development and capture launchers terminate the host without recursively terminating its orphaned watchdog, then wait for cursor restoration before launching another host.
- Typed text, pointer coordinates, and pairing credentials never enter application logs.
- Every long-lived worker has one service owner, bounded input, cancellation, and a shutdown wait. UI event handlers may enqueue service work but must not create competing fire-and-forget workers.
- Alpha availability is read from the host's cached setting and propagated through the existing event-driven status broadcaster. It is never polled or read from the registry on input/render hot paths. Disabled alpha features allocate no feature-specific timers, subscriptions, native resources, workers, or network activity.
