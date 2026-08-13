# Voltura Air TODO

Approved unfinished work is ordered here. Current behavior belongs in
[features](features.md); possible directions belong in [ideas](ideas.md).
Remove completed items after updating their current authority.

## Priority

1. Release-blocking correctness, security, connection, input, data-loss,
   recovery, and resource-lifetime defects.
2. Work promoted from `ideas.md` after its outcome, priority, ownership, and
   validation boundary are decided.

## Phone-as-webcam production feature plan

### Outcome

Add an opt-in, video-only **Phone webcam** feature that turns a paired phone camera
into `Voltura Air Webcam` on Windows 11. Direct LAN is free/local. The existing
Voltura-operated Relay path may later be subject to hosted-service entitlement or
quality tiers, but this implementation must not add billing or imply that a paid plan
already exists. Self-hosted Relay remains supported by the public product.

The feasibility spike proved live iPhone Chrome video over Direct and Relay into VLC,
Chrome, and Teams, including explicit stop/start and live switching across every
camera exposed on the tested iPhone 17 Pro Max. It did not prove Edge, simultaneous
multi-consumer capture, corrected install/remove on hardware, or the numeric latency
gate. iOS closing a backgrounded browser peer is a known product lifecycle case, not
a transport retry on the dead peer.

### Repository ownership

| Owner | Work |
| --- | --- |
| Public `voltura-air` | Windows virtual camera and installer integration; host receive/decode/frame pipeline; phone webcam UI and capture; authenticated webcam protocol; Direct and self-hosted Relay support; settings, permissions, diagnostics, tests, and public documentation. |
| Private `voltura-air-service` | Voltura-operated Relay deployment and quota policy; any future webcam entitlement/quality response; hosted PWA/site publication; production rollout and service evidence. |

Keep media and lifecycle behavior public. A private service decision may limit use of
Voltura's hosted resources, but must not fork codecs, camera behavior, the virtual
camera, or the public protocol implementation.

### Product contract

- Video only. A phone microphone is a separate future feature.
- First supported phone target: current iPhone Chrome/WebKit on iOS. Record Android
  and other iOS browsers as untested until separately proved.
- Request the best practical camera capture up to 1920×1080 at 30 fps. Show actual
  capture and encoded dimensions/fps; never label a lower stream as 1080p.
- Expose the cameras returned after one explicit permission action. Let the user
  select a camera before **Start webcam**; switching during streaming replaces the
  track on the current healthy peer.
- **Stop webcam**, capture loss, host stop, permission loss, and transport loss stop
  every phone track immediately and make the Windows camera show its waiting frame.
- When iOS closes the peer in the background, returning to the foreground creates one
  fresh authenticated session automatically. Do not attempt to revive, renegotiate,
  or append work to the dead peer. Bound retries and require an explicit user action
  after the retry budget is exhausted.
- One host owns the feature. At most one phone webcam producer is active. Consumers
  may open the Windows camera according to Windows Frame Server behavior; no custom
  multi-consumer broker is added unless compatibility evidence requires it.
- Initial rollout is an explicit feature-owned toggle under **Developer tools**. The
  normal host, installer, and PWA own it; no spike executable or second app remains.

### Public implementation stages

#### 1. Production virtual camera foundation

- Move the reviewed native media-source portion from the spike into the Windows host
  native ownership area, retaining Microsoft's MIT notice. Preserve one advertised
  type: NV12 1920×1080/30 and the 500 ms waiting-frame timeout.
- Integrate current-user camera create/remove with the normal installer/uninstaller.
  Elevation is limited to native file and COM registration boundaries. Use the
  spike's three durable states—files, COM, current-user camera—and its rollback rules;
  never stop the shared Frame Server service.
- Keep the versioned named-pipe contract and latest-frame capacity of one. Reuse the
  reviewed SID/process-token checks, monotonic sequence validation, exact record
  bounds, and deterministic disconnect behavior.
- Add install/upgrade/remove fault injection at each external boundary, including an
  in-use DLL and rollback failure. Do not advance until corrected install/remove is
  rerun on a clean Windows 11 profile.

#### 2. Host media pipeline

- Move the spike's receive-only libdatachannel interop, bounded H.264 RTP
  depacketizer, Media Foundation decoder recovery, stride/crop normalization, and
  NV12 frame publication into feature-owned production classes.
- Reuse the existing production WebRTC identity, TURN parsing, quota-derived quality,
  TLS/TCP 443 bridge, logging, Media Foundation lifetime, and Screen View peer
  patterns. Do not reference the spike executable or duplicate Relay constants.
- Preserve H.264 configuration across decoder recovery, request a key frame after
  loss, keep at most one pending encoded access unit and one pending decoded frame,
  and pace the virtual camera at 30 fps. Backlog must drop locally rather than become
  latency.
- Accept decoded portrait or landscape input and letterbox/crop into the fixed virtual
  camera output without rejecting useful lower-resolution video. Record the actual
  received resolution as quality evidence.

#### 3. Authenticated webcam session protocol

- Extend the current authenticated host/device connection with one webcam session
  owner and bounded start/offer/answer/stop/end messages. Follow Screen View's signed
  operation ID, expiry, SDP hashing, and exact-current-version rejection model, with
  roles reversed: phone sends one H.264 track and host receives it.
- Direct uses host candidates; Relay uses only the existing authenticated route and
  issued TURN configuration. Relay-only mode rejects any non-relay candidate on both
  peers. No PHP signaling, room fragment, new Relay endpoint, or separate credential
  policy enters production.
- A fresh foreground session obtains current Relay credentials and a new operation
  ID. The old session is terminal and is disposed before the replacement is accepted.
- Add a specific host permission, capability advertisement, busy result, bounded
  negotiation timeout, explicit terminal reasons, and revocation cleanup. Never log
  SDP, camera frames, credentials, proofs, or pairing secrets.

#### 4. Phone and Windows UX

- Add a small phone webcam workspace to the existing paired PWA: permission action,
  preview, camera selector, Direct/Relay route status, actual capture/send quality,
  **Start webcam**, and **Stop webcam**. No audio, orientation automation, quality
  settings surface, account UI, or generic media framework.
- Add a Windows Developer-tools toggle, install/status/removal feedback, permission,
  active-phone state, waiting/error state, and tray stop action using existing host UI
  composition and settings ownership.
- Treat page hidden as immediate track release. On foreground return, reconnect once
  through the existing connection/session coordinator and make the recovery state
  visible. Camera selection remains stable when the same device ID is still exposed.

#### 5. Rollout and cleanup

- Remove production dependencies on `apps/webrtc-spike-host` and
  `apps/secure-web-spike`; retain their README/evidence until the production gates
  supersede them, then delete the isolated binaries/site and update the docs map.
- Keep the feature developer-only until every required automated and hardware gate
  passes. Promotion to a normal feature is a separate explicit product decision.

### Private service stages

- Deploy the unchanged public Relay worker from an exact pinned public commit using
  the private production configuration and existing 15-minute TURN credentials,
  4/2 Mbps effective bitrate policy, 750 GB warning/Data Saver threshold, 850 GB
  cutoff, and TLS/TCP 443 host bridge.
- Initially authorize webcam exactly as current Relay use is authorized. Add no
  billing fields, database, account dependency, or quality tier.
- If hosted entitlement is later approved, define one bounded service response that
  distinguishes unavailable, allowed quality, and retry time. Direct and custom
  self-hosted Relay must remain usable without Voltura billing. That future wire
  change requires its own compatibility and privacy review.
- Publish the hosted PWA only after Relay dry-run/deploy/health passes for the same
  pinned public revision. Keep production credentials and operational evidence in the
  private repository or provider, never in public source or browser payloads.

### Automated gates

- Current-version protocol parsing, signatures, operation expiry, permission/busy
  results, wrong-client responses, duplicate/stale messages, and terminal cleanup.
- Foreground recovery creates exactly one fresh peer; repeated visibility events,
  offline periods, failed credentials, and exhausted retry budget stay bounded.
- Camera permission denial, device enumeration, pre-start selection, active switching,
  unsupported H.264, stop, hidden page, ended track, and exact track disposal.
- RTP single NAL/STAP-A/FU-A assembly, gaps, timestamp changes, malformed/oversized
  packets, key-frame recovery, decoder format changes, stride/crop handling, lower
  resolutions, and capacity-one replacement.
- Pipe authentication/records/disconnect, 30 fps pacing, waiting timeout, host exit,
  and deterministic Media Foundation/native disposal.
- Installer install/upgrade/remove state-machine fault injection, rollback failure,
  in-use DLL, current-user ownership, and no orphaned COM/camera/files.
- Public build/test/docs/isolation gates plus private pin/config/dry-run tests. Neither
  automated suite may contact or mutate production.

### Required real-device gates

1. Clean Windows 11 install and removal under a standard current-user profile.
2. Direct LAN and relay-only live rendering from iPhone Chrome at reported
   1920×1080/30 when the browser actually supplies it; lower output is recorded, not
   disguised or treated as total feature failure.
3. Sequential selection/rendering in Windows Camera when available, Chrome, current
   Teams, and current Edge. Record simultaneous-consumer behavior separately.
4. Three stop/start cycles; every exposed camera; front/rear rotation; permission
   loss; mid-frame network loss; host/consumer exit; and waiting-frame recovery.
5. Background the iPhone browser long enough for iOS to close the peer, return, and
   prove automatic fresh-session recovery without restarting the host or desktop
   consumer.
6. Direct and Relay end-to-end p95 at or below 300 ms with effective source changes at
   least 28 fps on the corrected production path. Visual responsiveness is useful
   evidence but does not replace this numeric gate.
7. Relay-only candidates and host external TLS/TCP 443 evidence, followed by private
   production health and cleanup checks for the exact pinned revision.

Any failure at the virtual-camera, media, lifecycle, or Relay boundary stops rollout
at that stage. Do not add billing, microphone capture, automatic orientation, or a
general media subsystem to work around an unproved gate.
