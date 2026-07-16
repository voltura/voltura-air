# Architecture

Voltura Air has two runtime halves: a React PWA that captures user intent and a Windows host that authenticates clients, applies permissions, and performs Windows operations. The protocol in `apps/mobile-web/src/protocol.ts` and `docs/protocol.md` is their compatibility boundary.

## Dependency direction

```text
mobile components -> feature hooks/state -> protocol and storage models
                                            |
                                            v
Windows WebHostService -> validation/policy -> input, audio, and remote services
                                                |
                                                v
                                         Windows platform APIs
```

React components send typed intents through `useVolturaAirConnection`; the connection subsystem owns pairing, reconnects, acknowledgements, and protocol parsing. On the host, `WebHostService` validates and routes messages, while `HostPermissions` and paired-device settings decide policy before Windows adapters execute an approved command.

## Host subsystems

- `Program.cs` and `WpfHostRuntime.cs` compose the one-host tray application and own startup rollback and process-resource shutdown. `WpfTrayApplicationContext.cs` requests user commands and application shutdown but does not own service internals. `WebHostService.*.cs` owns HTTP/WebSocket lifetime, bounded messages, receive/send/close deadlines, per-socket send serialization, the single coalescing status broadcaster, authenticated status, and static PWA serving.
- `PairingManager.*.cs`, `PairingStore.cs`, and `PairingAttemptRateLimiter.cs` own authentication, bounded and validated atomic pairing persistence, and per-device permissions and pointer-speed overrides.
- `ClientMessageValidator.cs` decodes each bounded authenticated input message once into a `ValidatedInputCommand`; `InputDispatcher.cs` consumes that command without rereading JSON and translates it for `SendInputInjector.cs`. The injector serializes native calls, reuses its single-input movement buffer, and sends text in bounded batches with partial-send cleanup. `PresentationCommands.cs` owns the fixed target-specific presenter shortcut allowlist. `TextDestination.cs` owns safe focused/clipboard/configured text delivery. `WindowsProcessIntegrity.cs` and `PointerHighlightForegroundMonitor.cs` use a single foreground event hook solely to block remote input when a higher-integrity app owns the foreground; they have no cursor responsibilities.
- `AppLog.cs` owns the optional bounded single-writer filesystem queue and shutdown flush. `WpfHostRuntime` disposes it after every producer; a standalone `WebHostService` disposes only a logger it created itself.
- `CustomPointerService.cs` builds real Windows cursors from the packaged maximum-size templates and applies them through the Windows cursor API. It retains the Windows role mapping for normal use, leaving a separate role-to-shape policy seam for future presentation styles such as a single-shape or laser pointer. It persists Voltura's enabled, size, RGB, and default-on recovery-watchdog settings, never changes Windows Accessibility settings, and reloads the user's normal cursor scheme on disable or normal exit.
- `CursorWatchdogService.cs` owns the native monitor while Custom pointer and **Use cursor recovery watchdog** are both enabled. `VolturaAir.CursorWatchdog.exe` first runs as a short-lived bootstrap that starts the ready-confirmed monitor and exits; the monitor is therefore outside the live host process tree, waits on the host process handle, reloads the configured Windows cursor scheme after unexpected termination, and exits. Normal host shutdown stops the monitor after restoring the cursor. The helper is not an overlay or a second .NET runtime.
- `RemoteActionExecutor.*.cs`, `AppLaunchService.cs`, `SystemAudioController.cs`, `SystemPowerController.cs`, `WindowsDisplayActionController.cs`, and `AwakeService.cs` own their focused platform functions. `MainWindow.*.cs` and `WpfTrayApplicationContext.cs` own WPF/tray presentation and dispatcher-affine state; they request lifecycle actions while `Program.cs` and `WpfHostRuntime.cs` perform shutdown.

## Mobile subsystems

- `App.tsx` is the composition root; `useVolturaAirConnection.ts` is the public connection entry point. Supporting modules own identity, credentials, health, acknowledgements, congestion limits, and reconnect policy.
- `components/` contains presentation and interaction surfaces. They emit typed intents rather than controlling sockets.
- `appStorage.ts`, `pcProfiles.ts`, and settings modules own persisted client formats. `gestures.ts` and `keyboardDelta.ts` are the input-domain modules.

## Invariants

- Only one Windows host process runs per signed-in user.
- Automated protocol tests use ASP.NET Core `TestServer`; temporary UI hosts use `--isolated-test-mode`, bind only to loopback, use disposable host settings and pairing data, and must not run beside a normal host.
- Custom pointer is host-wide because it is one real Windows cursor scheme. Size, color, and recovery-watchdog choice are host-only; paired devices may toggle only its host-wide enabled state through the authenticated protocol. Development and capture launchers terminate the host without recursively terminating its orphaned watchdog, then wait for cursor restoration before launching another host.
- Typed text, pointer coordinates, and pairing credentials never enter application logs.
- Every long-lived worker has one service owner, bounded input, cancellation, and a shutdown wait. UI event handlers may enqueue service work but must not create competing fire-and-forget workers.
