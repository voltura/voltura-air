# Architecture

Voltura Air consists of two runtime halves: a React PWA that captures user intent and a Windows host that authenticates clients, applies permissions, and performs platform operations. JSON messages defined in `apps/mobile-web/src/protocol.ts` and `docs/protocol.md` form the compatibility boundary between them.

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

The mobile client does not make raw WebSocket decisions in React components. Components send typed user intents through `useVolturaAirConnection`. The connection subsystem owns connection health, acknowledgements, reconnects, pairing credentials, and protocol parsing. Storage modules own persisted formats.

The host protocol layer validates and routes messages. `InputDispatcher`, `AudioMessageRouter`, and `RemoteActionExecutor` execute approved commands. `HostPermissions` and paired-device settings determine whether those commands are allowed. Platform services do not decide policy.

## Host subsystems and entry points

- `Program.cs` and `WpfHostRuntime.cs` are composition roots.
- `WebHostService.cs` and `WebHostService.Protocol.cs` own HTTP/WebSocket orchestration and protocol dispatch. `WebHostStaticFiles.cs` owns static PWA serving.
- `PairingManager.*.cs`, `PairingStore.cs`, and `PairingAttemptRateLimiter.cs` own pairing state, authentication, persistence, and queries.
- `InputDispatcher.cs` translates validated input messages for the focused `SendInputInjector.cs` Windows adapter. Failure policy remains in the dispatcher so one failed injection does not disable later input.
- `RemoteActionExecutor.*.cs` owns fixed YouTube, Kodi, fullscreen, and window interop behavior. `AppLaunchSettings.cs` owns approved preset/custom launch definitions and validation; `AppLaunchService.cs` resolves opaque IDs and owns process creation. `SystemAudioController.cs` owns system audio. `SystemPowerController.cs` owns fixed Windows power/session routing. `WindowsDisplayActionController.cs` owns the WPF multi-monitor blackout curtain, native screen-saver availability/activation, and blackout dismissal; the native monitor-power request remains subject to Windows Modern Standby behavior. `WorkstationLockPolicy.cs` owns current-user lock-policy inspection and local enablement.
- `AwakeService.cs` owns the long-lived, thread-scoped Windows execution-state request, expiry timer, resume reapplication, and safe release. `AppAwakeSettings.cs` owns its current-user persisted configuration. Tray, WPF, and protocol code consume `IAwakeService` and do not call the native API directly.
- `AppLog.cs` owns the opt-in, sanitized JSON Lines application log, structured queries, retention, and deletion. Remote protocol handlers and local Windows services submit safe event metadata through `IAppLog`; typed text, pointer coordinates, and pairing credentials never enter the log. Diagnostics renders filtered records with existing WPF theme resources.
- `MainWindow.*.cs`, `WpfTrayApplicationContext.cs`, and the WPF XAML files own host presentation and lifecycle. Preferences uses a WPF single-open accordion, and Diagnostics keeps filters and action rows outside the scrollable log-record region. Business and protocol logic do not belong in WPF code-behind.

## Mobile subsystems and entry points

- `main.tsx` and `App.tsx` are composition roots. `App.tsx` connects feature state to components. Reusable connection, input, pairing, and installation behavior belongs in focused hooks or services.
- `useVolturaAirConnection.ts` is the public connection entry point. Supporting modules own client identity, pairing credentials, protocol normalization, connection health, acknowledgements, and reconnect policy. After Windows accepts a display-off request, the client schedules an early health probe and uses the normal health deadline; it does not claim the host remains reachable when Windows suspends it.
- `components/` contains presentation and interaction surfaces for trackpad, keyboard, remote, dictation, pairing, diagnostics, and settings. Components emit typed intents rather than controlling sockets.
- `appStorage.ts`, `pcProfiles.ts`, and the settings modules own persisted formats and normalization. Their existing keys and compatible shapes remain stable.
- `gestures.ts` and `keyboardDelta.ts` are input-domain modules. Gesture and keyboard translation behavior belongs there, with contract tests, rather than in the connection layer.

## Architectural boundaries

| Area | Boundary | Relevant coverage |
| --- | --- | --- |
| Windows input and dispatch | Preserve the `InputDispatcher` and `SendInputInjector` separation | `InputDispatcherTests`, `SendInputInjectorTests` |
| WebSocket, acknowledgements, and reconnect | Keep identity, credentials, message policy, and lifecycle in focused connection modules behind the existing hook API | `useVolturaAirConnection.test.ts`, host WebSocket tests |
| Browser, YouTube, Kodi, and fullscreen | Keep host behavior in `RemoteActionExecutor.*.cs` and remote shortcut policy in focused client modules | `RemoteMode.test.tsx`, host protocol tests |
| Configurable application launch | Keep paths/arguments and approval on the host; advertise and accept only opaque IDs and labels | app-launch settings, service, protocol, WPF, hook, and Remote component tests |
| Power and session actions | Keep fixed Windows execution in `SystemPowerController`, current-user lock-policy access in `WorkstationLockPolicy`, effective permission enforcement in the host protocol layer, and hold-to-confirm in the focused mobile power-sheet component | power protocol, policy, permission, component, and responsive browser tests |
| Keep awake | Keep execution-state ownership on one dedicated service thread; tray, Preferences, and permission-gated protocol clients share the same state | Awake service, protocol, permission, WPF, and mobile Power-sheet tests |
| Pairing, devices, and permissions | Keep pairing state and authentication on the host, while client identity and credentials remain isolated from presentation components | pairing, profile, permission, and connection tests |
| Host settings, WPF, and tray | Preserve the current partial-class and service boundaries | WPF, startup, and settings tests |
| React composition and feature state | Keep `App.tsx` as the composition root and move cohesive feature behavior into focused hooks and components | `App.test.tsx` and component tests |
| Styles | Keep styles separated in cascade order by shell, pairing, settings, input mode, split mode, and remote mode | production build and component tests |
| Diagnostics and scripts | Preserve diagnostic fields and redaction behavior, and maintain root-level source-size reporting | diagnostics tests and `npm run size:report` |

Temporary hosts with empty pairing stores use `--isolated-test-mode`. The mode
has a separate single-instance scope, listens and advertises on loopback only,
and does not write automatic network or port selections. This prevents tests,
screenshots, and UI validation from impersonating the real LAN host and causing
paired phones to discard valid reconnect credentials.

Automated host protocol tests additionally use ASP.NET Core's in-memory
`TestServer`, so the normal test suite opens no host TCP listener and needs no
Windows Firewall exception. Explicit browser UI previews still use the
loopback-only isolated host because a browser needs a real local endpoint.

Host subsystems receive inspection and validation before client changes when work spans both runtime halves. Reorganization only occurs when it improves a clear boundary, removes duplication, or reduces complexity. Each extraction preserves the dependency direction shown above and removes the replaced implementation after tests pass.

## Compatibility invariants

Do not change protocol message names or shapes, pairing or reconnect secrets, storage keys or settings shapes, permission semantics, input acknowledgement behavior, browser, YouTube, or Kodi fallback behavior, PWA URLs, diagnostics fields or redaction, host, tray, or window behavior, or packaging and release outputs without an explicit compatibility plan, matching tests, and updated documentation.

## Source-file size policy

Actively maintained source files normally remain below 20 KB. Files above 20 KB require review. Files above 25 KB require an explicit justification or a cohesive split.

Generated files, dependencies, build output, lockfiles, and binary or static assets are excluded. Tests may remain larger when splitting them makes behavior harder to understand.

Run the non-failing report from the repository root:

```powershell
npm run size:report
```

The report marks files above 25 KB as warnings. Prefer feature boundaries over line-count splits and avoid generic helper modules.

`tests/VolturaAir.Host.Tests/WebHostServiceTests.cs` is a reviewed exception at approximately 35 KB. It remains a single cohesive WebSocket contract fixture because its tests share one in-process host, socket helpers, and fake platform services. Splitting the fixture duplicates setup and makes protocol scenarios harder to review. The file contains tests only and does not affect connector edits to production source.
