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
| Persistent PC identity and fresh-pair bootstrap proofs | `ScreenViewHostIdentity`, `PairingBootstrapCrypto`, and `PairingManager` |
| Framing, socket registration, serialized sends | `WebSocketTransport` |
| Portable relay rooms, bounds, and provider contracts | `services/relay/src/core` |
| Cloudflare and standalone relay adapters | `services/relay/src/cloudflare` and `services/relay/src/standalone` |
| Screen-view tickets, single-viewer arbitration, encrypted records, and capture lifecycle | `ScreenViewCoordinator`, `ScreenViewCrypto`, and `DxgiScreenViewCaptureSource` |
| Opaque file navigation, Windows file clipboard/Shell actions, persisted panel locations, and serialized mutation work | `FileManagerService`, its platform/location/journal adapters, and `FileManagerCommandHandler` |
| Lazy mobile screen renderer and fallback crypto | `features/screen-view/` dynamic import |
| Coalesced capability/status delivery | `HostStatusBroadcaster` and payload factory |
| Validated input and focused Windows actions | Command handlers and platform adapters |
| Custom-screen definitions, editing, assignment, and invocation | `CustomScreenStore`, `CustomScreenService`, `Features/CustomScreens`, and `CustomScreenCommandHandler` |
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
| Screen capture and stream | The coordinator owns one expiring WebRTC offer or active viewer. The capture source owns one DXGI duplication session, D3D11 conversion resources, and hardware Media Foundation H.264 encoder. The peer owns its RTP track, DTLS data channel, ICE state, retransmission buffer, and native libdatachannel handles. Native resources are released on stop, switch, revocation, loss, or shutdown. |
| Native input and Awake | Input is decoded once and dispatched in order. Native calls have bounded callers; late completion reconciles before more work. Awake uses `IAwakeService`, never power-plan changes or elevation. |
| Logs and files | Producers use bounded non-blocking queues. Filesystem work stays off input/UI loops. Stores validate bounds and content, replace atomically, and preserve the last complete state. |
| WPF and tray | Dispatcher work is owned and bounded. Timers, hooks, subscriptions, icons, windows, and refresh sessions are released on unload/shutdown. |
| Mobile effects | Each effect releases sockets, listeners, timers, pointer capture, animation frames, and speech events it acquires. |
| Cursor recovery | Cursor overrides require an independent recovery process. Host exit cannot terminate it. If either process exits, the remaining process restores the Windows cursor scheme. |

Optional features allocate no feature-specific worker, timer, subscription,
native resource, or network activity while disabled. Hot input/render paths use
cached settings and event-driven updates, not registry reads or polling.

Direct LAN and Relay converge on the same `WebSocketSessionHandler` and
`PairingManager`. In Relay mode a persistent routing key derives an opaque route
and authenticates the one outbound host socket. Each device becomes a bounded
virtual WebSocket. After the existing pairing or reconnect proof, P-256 ECDH,
signed identity transcripts, HKDF-SHA256, and direction-specific AES-256-GCM
protect every accepted-session frame. The service forwards opaque envelopes
and does not own product pairing, permissions, commands, or device identities.

Relay preserves the root responsiveness invariant: command/input framing is
bounded and independent from media, TURN/usage work, persistence, logging, and
UI. Slow consumers close instead of building lag; screen media may degrade or
stop while commands remain responsive. Status UI reads one immutable snapshot.

Screen viewing is navigation/capability wiring in the initial PWA bundle. Its
workspace, WebRTC video renderer, event parser, host-identity verification, and
diagnostics stay in the Screen dynamic chunk and load only when the tool opens.
The JSON control socket owns discovery, signed offer/answer signaling,
source-switch, stop, and optional direct-pointer commands. Direct mouse mode is
browser-local; `ScreenViewCoordinator` authorizes the active viewer/display,
maps normalized positions through cached host monitor rotation and virtual-
desktop bounds, and owns held-button cleanup. The existing input dispatcher and
`SendInputInjector` perform guarded atomic absolute position/action batches.
Screen media uses a separate H.264 RTP track;
cursor/status uses the `screen-events` data channel, so media backpressure cannot
consume the command socket's serialized send queue.

Files follows the same lazy feature boundary: the initial shell carries only capability/navigation wiring and loads `features/file-manager` on entry. The host resolves every opaque session, location, entry, revision, continuation, and job reference. Effective global/per-device Files policy is applied while the host constructs each directory revision, including removal of protected Hidden+System items before any client-visible count or opaque entry reference exists. `FileManagerService` intentionally validates source and destination revisions together with queue admission so a changed directory cannot redirect or partially resolve an operation. Its bounded workers own the single mutation queue, panel-location persistence, and atomic local job journal; shutdown cancels and awaits them. Every temporary, backup, or partially committed artifact remains durably owned until cleanup, commit, or rollback is confirmed. Windows clipboard, Shell, location-store, and journal behavior remain replaceable adapters so destructive transitions can be fault-injected independently.

The capture owner uses Desktop Duplication GPU frames and cursor metadata. A
D3D11 conversion stage supplies NV12 GPU surfaces to a capability-selected
hardware Media Foundation H.264 transform. A bundled libdatachannel peer sends
the Annex-B access units as H.264 RTP and owns DTLS-SRTP, direct or relay-only ICE,
sender reports, NACK retransmission, keyframe requests, and receiver bitrate
feedback. Relay sessions receive short-lived TURN credentials through the
authenticated routing identity. The Windows peer keeps libjuice as the ICE/TURN
owner and supplies it a loopback-only TURN endpoint. A host-owned bounded bridge
connects that endpoint to the issued `turns` service with certificate-validated
TLS/TCP, translates only RFC 8656 datagram/stream framing, accepts one loopback
owner, and ends with the peer. Direct sessions create no bridge or TURN service.
The mobile Screen workspace owns relay-candidate gathering. It can complete a
Relay answer from a settled relay-only SDP even when a browser leaves gathering
in progress; Direct answers retain complete-gathering behavior. Changing
source/profile or ending capture disposes the encoder and duplication session;
a new source begins with a keyframe.

Custom screens cross the trust boundary as visual definitions and opaque IDs
only. The host-owned store retains actions and assignments, the status
broadcaster publishes assigned summaries, and the command handler rechecks
  assignment, revision, effective permission, and current approved
application availability before dispatching through existing input,
HTTP(S)-URL, known-application, or allow-listed system-action owners. Destructive
action confirmation is host-derived visual metadata rather than package policy.
Screen components and actions remain shared identities; optional
portrait and landscape records are peer layout overrides for visibility, order,
width/size, and button row rather than duplicated action definitions.
Button and trackpad panels share the same row composer: content rows reserve
their measured height, and fill rows divide the remaining viewport by weight.
Collapsible trackpads are the existing trackpad wire kind plus collapsible
presentation state; fullscreen is local mobile state and never mutates the
stored layout.
The saved-screen preview reuses that visual projection through a loopback-only
HTTP read. A themed WPF window owns the fixed device,
orientation, and Rotate controls and embeds the saved mobile surface in
WebView2 below them. The HTML contains no preview toolbar or window-resize
logic. The embedded surface has no WebSocket command channel and therefore
cannot invoke actions. The Custom screens page owns every preview window and
closes them together after navigation away succeeds. Editor lifecycle outcomes
enter the existing non-blocking `AppLog`; no custom-screen names, labels,
action payloads, or drag events are recorded.

Official screens are owned by `scripts/custom-screens`: concise definitions use
shared action/layout builders, and `scripts/generate-custom-screens.mjs`
deterministically emits exact package-version-1 JSON, catalog metadata, and a
fixed-timestamp ZIP. Generated packages are artifacts, never hand-maintained.
The catalog's admin importer validates the entire bundle before installing
content-addressed files and committing one database transaction; stable
official IDs preserve ratings and download counters across updates.

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
