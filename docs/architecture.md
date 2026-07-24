# Architecture

Voltura Air has two runtime halves: a React PWA captures user intent; a Windows
tray host authenticates clients, applies permissions, and performs Windows
operations.

## Dependency direction

```text
mobile app -> feature slices -> shared UI
     \             |
      +---- typed foundation ---- protocol ---- Windows session/policy
                                                   |
                                             platform adapters
```

Mobile source lives under `app`, `features`, `ui`, or `foundation/<domain>`.
Dependencies follow the diagram: UI imports no feature/foundation state;
foundation imports no React presentation; features use public entry points, not
another feature's private files.

The Windows host composes services at startup. Authenticated, bounded messages
move through one validation/policy boundary before focused command handlers and
platform adapters. UI and tray surfaces request lifecycle actions; the runtime
performs startup, rollback, and shutdown.

## Ownership map

| Area | Owner |
| --- | --- |
| Mobile shell, navigation, safe area, overlays | `app/` |
| Mobile capability UI and feature state | `features/<capability>/` |
| Mobile controls and feedback without domain state | `ui/` |
| Mobile sockets, protocol, input, persistence, platform | `foundation/<domain>/` |
| Host composition, rollback, shutdown | `Program` and `WpfHostRuntime` |
| ASP.NET/static PWA and session capacity | `WebHostService` |
| Pairing/authenticated session state | `PairingManager`, token authority, registry, store, and session handler |
| Framing, socket registration, serialized sends | `WebSocketTransport` |
| Coalesced capability/status delivery | `HostStatusBroadcaster` and payload factory |
| Validated input and focused Windows actions | Command handlers and platform adapters |
| Settings and persisted data | Their focused settings/store types |
| Logs and Diagnostics reads | `AppLog`, file store, and per-view refresh session |
| Tray, main window, and WPF pages | Tray context, `MainWindow`, and `Features/<feature>` |

`MainWindow` owns only shell composition, navigation, visibility, and
subscriptions needed by visible views. `MainWindow.xaml.cs` is its only
maintained source file; the other partial declaration is WPF-generated. Feature
behavior belongs in a named type under `Features/<feature>` or an existing
service owner, not another window partial.

## Resource contract

Every long-lived worker or native resource has one owner, bounded input,
cancellation where possible, deterministic cleanup, and a shutdown wait. Startup
rollback and shutdown release composition-owned resources in reverse order.

| Resource family | Required ownership |
| --- | --- |
| Sockets and status | Registered sends are serialized and timed; status uses one capacity-one coalescing worker; shutdown closes and awaits owned work. |
| Native input and Awake | Input is decoded once and dispatched in order. Native calls have bounded callers; late completion reconciles before more work. Awake uses `IAwakeService`, never power-plan changes or elevation. |
| Logs and files | Producers use bounded non-blocking queues. Filesystem work stays off input/UI loops. Stores validate bounds and content, replace atomically, and preserve the last complete state. |
| WPF and tray | Dispatcher work is owned and bounded. Timers, hooks, subscriptions, icons, windows, and refresh sessions are released on unload/shutdown. |
| Mobile effects | Each effect releases sockets, listeners, timers, pointer capture, animation frames, and speech events it acquires. |
| Cursor and watchdog | Custom pointer is one host-wide Windows scheme. The host reloads the configured Windows scheme at startup and restores it on disable/exit. Custom pointer requires one ready-confirmed watchdog, launched outside the host's Windows job before a cursor is replaced, to restore that scheme after abnormal host exit; replacement hosts wait for an earlier watchdog to finish restoration. The host owns one OS exit notification for that watchdog; on loss it restores normal cursors and disables Custom pointer, without polling or touching protocol/client paths. |

Optional features allocate no feature-specific worker, timer, subscription,
native resource, or network activity while disabled. Hot input/render paths use
cached settings and event-driven updates, not registry reads or polling.

## Source limits

Review maintained source above 300 lines/12 KiB; above 500 lines/20 KiB is a
strong mixed-ownership warning. Split by responsibility, lifecycle, or
dependency. Cohesive algorithms, schemas, interop declarations, or data tables
may remain larger with a recorded rationale.

`npm run size:report` reports thresholds.
`npm run size:check` validates strong-warning reviews in
`scripts/source-size-reviews.json`. `npm run host:ownership:check` rejects a
maintained host type spread across source files except framework/generated
partials.
