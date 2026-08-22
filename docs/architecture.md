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
| Secure Direct signaling and controller peer lifecycle | Relay Worker `SecureDirectRoomObject`; host `SecureDirectHostConnection`, `SecureDirectSessions`, and `SecureDirectWebSocket` |
| Pairing/authenticated session state | `PairingManager`, token authority, registry, store, and session handler |
| Persistent PC identity and fresh-pair bootstrap proofs | `ScreenViewHostIdentity`, `PairingBootstrapCrypto`, and `PairingManager` |
| Framing, socket registration, serialized sends | `WebSocketTransport` |
| Portable relay rooms, bounds, and provider contracts | `services/relay/src/core` |
| Cloudflare and standalone relay adapters | `services/relay/src/cloudflare` and `services/relay/src/standalone` |
| Screen-view tickets, single-viewer arbitration, encrypted records, and capture lifecycle | `ScreenViewCoordinator`, `ScreenViewCrypto`, and `DxgiScreenViewCaptureSource` |
| Phone-webcam producer arbitration, video/audio receive pipelines, explicit local audio monitor, frame pipe, and virtual devices | `PhoneWebcamCoordinator`, `PhoneWebcamVideoPipeline`, `PhoneWebcamAudioPipeline`, `PhoneWebcamAudioMonitor`, `PhoneWebcamAudioTarget`, `PhoneWebcamFeature`, and `Features/PhoneWebcam/Native` |
| Opaque file navigation, Windows file clipboard/Shell actions, persisted panel locations, and serialized mutation work | `FileManagerService`, its platform/location/journal adapters, and `FileManagerCommandHandler` |
| Lazy mobile screen renderer and fallback crypto | `features/screen-view/` dynamic import |
| Coalesced capability/status delivery | `HostStatusBroadcaster` and payload factory |
| Validated input and focused Windows actions | Command handlers and platform adapters |
| Host-local simulated activity | `ActivitySimulationService`, `AppActivitySimulationSettings`, and the narrow activity-pulse sender |
| Custom-screen definitions, editing, assignment, and invocation | `CustomScreenStore`, `CustomScreenService`, `Features/CustomScreens`, and `CustomScreenCommandHandler` |
| Settings and persisted data | Their focused settings/store types; `HostSettingsJsonValue` supplies only the shared bounded exact-shape registry-JSON boundary |
| Logs and Diagnostics reads | `AppLog`, file store, and per-view refresh session |
| Usage-statistics consent, identity, counters, sender, and Diagnostics state | `UsageStatisticsSettings`, `UsageTelemetryService`, typed recorder, session-owned feature bitset, and `Features/Diagnostics/UsageStatisticsController` |
| Usage-statistics ingest, storage, aggregate administration, and cleanup | `apps/public-site/telemetry` plus the existing Custom Screens administrator/session/CSRF/layout owner |
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
| Phone webcam | The coordinator owns one expiring offer or active phone producer. The peer owns receive-only H.264 and optional Opus RTP plus ICE/TURN resources; duration-bounded pipelines own depacketization, Media Foundation/Concentus decode, and exact-endpoint WASAPI output. The Windows page may explicitly own one duration-bounded `CABLE Output`-to-default-speakers monitor while an audio track is active. The feature owns one latest decoded frame and authenticated versioned pipe; the Frame Server media source owns fixed 1920 x 1080 NV12 consumer output. Stop, hiding, revocation, loss, removal, and shutdown release their complete ownership chains. |
| Native input, Awake, and simulated activity | Input is decoded once and dispatched in order. Native calls have bounded callers; late completion reconciles before more work. Awake uses `IAwakeService`, never power-plan changes or elevation. Optional simulated activity owns one fixed-delay loop while enabled, probes input availability without waiting, releases the input gate before its one-event native call, and is disposed before the shared injector. |
| Logs and files | Producers use bounded non-blocking queues. Filesystem work stays off input/UI loops. Stores validate bounds and content, replace atomically, and preserve the last complete state. |
| Usage statistics | While disabled there is no accumulator, channel, worker, timer, or HTTP work. While enabled the runtime owns one fixed saturating accumulator, one capacity-one batch channel, separate scheduler/sender tasks, one in-flight retrying batch, five-second cancellable HTTPS attempts, and one shutdown-only final five-second attempt. Disable publishes the cached false state before cancellation and clears every unsent owner; graceful shutdown sends the active accumulator once, while crashes or power loss can lose unsent counters. |
| WPF and tray | Dispatcher work is owned and bounded. Timers, hooks, subscriptions, icons, windows, and refresh sessions are released on unload/shutdown. |
| Mobile effects | Each effect releases sockets, listeners, timers, pointer capture, animation frames, and speech events it acquires. |
| Mobile pairing QR capture | The pairing feature owns one temporary camera stream and one lazy decoder worker per active scan. Live input is a centered capacity-one frame at a bounded cadence; cancellation, success, hiding, track loss, replacement, or unmount stops every track, timer, listener, and worker. Photo capture uses the same worker and pairing-link parser without retaining frames. |
| Cursor recovery | Cursor overrides require an independent recovery process. Host exit cannot terminate it. If either process exits, the remaining process restores the Windows cursor scheme. |

Optional features allocate no feature-specific worker, timer, subscription,
native resource, or network activity while disabled. Hot input/render paths use
cached settings and event-driven updates, not registry reads or polling.
Simulated-activity success and busy skips perform no persistence, logging, UI,
or network work; remote input never enters its timer, state, or failure paths.

Usage statistics follows that optional-feature rule without introducing an
event bus or analytics SDK. `WpfHostRuntime` owns one service and disposes it
after admitted controller sessions stop. Producers capture one immutable enabled-
generation token, use a session-local fixed bitset bound to that generation, and
perform saturating integer increments only when the service still owns that exact
generation. Work paused across disable/re-enable is dropped instead of crossing
into the replacement identity. After making recording unavailable, Disable
synchronously clears every live session bitset through a fixed connection-bounded
registry before the transition completes; sessions use no reset subscription.
Producers never log, allocate a per-event record, await, or touch the Windows registry, disk, HTTP,
database, UI, or the Application-log queue. The scheduler atomically replaces
the current accumulator and uses `TryWrite`; a full channel drops the sealed
snapshot locally. HTTP delay and retry therefore cannot delay command or media
processing. The PWA supplies only capability-gated functional input context on
the existing authenticated transport and owns no telemetry identity or sender.

The fixed first-party PHP endpoint validates the complete version-1 batch,
derives domain-separated HMAC keys, applies installation/source/service bounds,
and commits deduplication plus daily upsert in one MariaDB transaction. Public
ingest never loads administrator cleanup or starts a catalog session. The
aggregate dashboard instead reuses the existing administrator, role, session,
CSRF, theme, and layout and issues only fixed telemetry-table queries. Automatic
retention is lease-controlled and bounded per table; manual deletion requires a
preview whose counts are rechecked under the deletion transaction, commits at
most 1,000 rows, and cannot address catalog tables.

Direct LAN and Relay converge on the same `WebSocketSessionHandler` and
`PairingManager`. In Relay mode a persistent routing key derives an opaque route
and authenticates the one outbound host socket. Each device becomes a bounded
virtual WebSocket. After the existing pairing or reconnect proof, P-256 ECDH,
signed identity transcripts, HKDF-SHA256, and direction-specific AES-256-GCM
protect every accepted-session frame. The service forwards opaque envelopes
and does not own product pairing, permissions, commands, or device identities.

Secure Direct also converges on that same session handler. `WebHostService`
owns one non-blocking 64-session admission pool shared by local, Relay, pending
Secure Direct, and established Secure Direct sessions. The secure host
connection owns only authenticated outbound signaling/retry; its session owner
holds pending IDs, source keys, admission leases, peers, timers, handlers, and
drain. `SecureDirectWebSocket` alone owns libdatachannel handles, callbacks,
offer/answer, the bounded text queue, and private direct-candidate validation.
Signaling owns cancellation only until answer application; afterward the peer
and existing controller lifecycle own the session. Disabled and Relay
compositions allocate no Secure Direct resources.

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

Phone webcam follows the same lazy mobile feature boundary and authenticated
connection owner, with media direction reversed. The phone selects and sends one
H.264 camera track; `PhoneWebcamCoordinator` owns the single pending or active
producer, signed offer/answer expiry, permission and pairing revocation, Relay
credential reuse, and terminal cleanup. `PhoneWebcamVideoPipeline` keeps one encoded
access unit, preserves H.264 configuration through decoder recovery, and publishes
one latest fixed-size NV12 frame to the feature-owned pipe. The native Frame Server
media source contains no network or credential logic. The Windows page consumes the
registered virtual camera like any other application and never becomes a second
media broker. Switching phone cameras replaces the sender track on the active peer;
it does not replace the peer or command connection. A phone-webcam stop releases
only feature-owned camera/media resources. Native cleanup runs outside the
authenticated command receive loop, and neither stop nor camera switching closes or
marks the paired device connection unavailable.
The pipe carries capacity-one frame or clear records. A camera handoff therefore
holds the last valid frame instead of flashing the synthetic waiting image, while a
terminal session transition sends an explicit clear record after media ownership is
released.

Files follows the same lazy feature boundary: the initial shell carries only capability/navigation wiring and loads `features/file-manager` on entry. The host resolves every opaque session, location, entry, revision, continuation, and job reference. Effective global/per-device Files policy is applied while the host constructs each directory revision, including removal of protected Hidden+System items before any client-visible count or opaque entry reference exists. `FileManagerService` intentionally validates source and destination revisions together with queue admission so a changed directory cannot redirect or partially resolve an operation. Its bounded workers own the single mutation queue, panel-location persistence, and atomic local job journal; shutdown cancels and awaits them. Every temporary, backup, or partially committed artifact remains durably owned until cleanup, commit, or rollback is confirmed. Windows clipboard, Shell, location-store, and journal behavior remain replaceable adapters so destructive transitions can be fault-injected independently.

The capture owner uses Desktop Duplication GPU frames and cursor metadata. A
D3D11 conversion stage supplies NV12 GPU surfaces to a capability-selected
hardware Media Foundation H.264 transform. A bundled libdatachannel peer sends
the Annex-B access units as H.264 RTP and owns DTLS-SRTP, direct or relay-only ICE,
sender reports, NACK retransmission, and keyframe requests. The controller sends
bounded aggregate receiver-health counters over the authenticated command path.
Relay sessions receive short-lived TURN credentials through the
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
The catalog stages and validates a complete official bundle before its locked
database reconciliation. Provenance-keyed official rows retain stable package
IDs, ratings, and download counters; only absent rows with Voltura provenance
are removed. Upload/delete/import database transactions enqueue narrowly owned
content-file cleanup jobs. Mutating requests drain a bounded number and the same
idempotent owner is available through the catalog maintenance CLI; referenced,
missing, or hash-mismatched files are never destructively guessed.

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
