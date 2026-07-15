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

- `Program.cs` and `WpfHostRuntime.cs` compose the one-host tray application. `WebHostService.*.cs` owns HTTP/WebSocket lifetime, bounded messages, deadlines, authenticated status, and static PWA serving.
- `PairingManager.*.cs`, `PairingStore.cs`, and `PairingAttemptRateLimiter.cs` own authentication, pairing persistence, and per-device permissions and pointer-speed overrides.
- `InputDispatcher.cs` translates validated input for `SendInputInjector.cs`. `TextDestination.cs` owns safe focused/clipboard/configured text delivery. `WindowsProcessIntegrity.cs` and `PointerHighlightForegroundMonitor.cs` use a single foreground event hook solely to block remote input when a higher-integrity app owns the foreground; they have no cursor responsibilities.
- `CustomPointerService.cs` builds real Windows cursors from the packaged maximum-size templates and applies them through the Windows cursor API. It retains the Windows role mapping for normal use, leaving a separate role-to-shape policy seam for future presentation styles such as a single-shape or laser pointer. It persists only Voltura's enabled, size, and RGB settings, never changes Windows Accessibility settings, and reloads the user's normal cursor scheme on disable, normal exit, or watchdog recovery.
- `VolturaAir.CursorWatchdog` is a small native process that waits for the host PID and reloads the configured Windows cursor scheme after host termination. It is not an overlay or a second .NET runtime.
- `RemoteActionExecutor.*.cs`, `AppLaunchService.cs`, `SystemAudioController.cs`, `SystemPowerController.cs`, `WindowsDisplayActionController.cs`, and `AwakeService.cs` own their focused platform functions. `MainWindow.*.cs` and `WpfTrayApplicationContext.cs` own WPF presentation and lifecycle.

## Mobile subsystems

- `App.tsx` is the composition root; `useVolturaAirConnection.ts` is the public connection entry point. Supporting modules own identity, credentials, health, acknowledgements, congestion limits, and reconnect policy.
- `components/` contains presentation and interaction surfaces. They emit typed intents rather than controlling sockets.
- `appStorage.ts`, `pcProfiles.ts`, and settings modules own persisted client formats. `gestures.ts` and `keyboardDelta.ts` are the input-domain modules.

## Invariants

- Only one Windows host process runs per signed-in user.
- Automated protocol tests use ASP.NET Core `TestServer`; temporary UI hosts use `--isolated-test-mode`, bind only to loopback, and must not run beside a normal host.
- Custom pointer is host-wide because it is one real Windows cursor scheme. Size and color are host-only; paired devices may toggle its host-wide enabled state through the authenticated protocol.
- Typed text, pointer coordinates, and pairing credentials never enter application logs.
