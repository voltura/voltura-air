# Protocol

JSON control WebSocket contract at `/ws`, including authenticated WebRTC screen
signaling. Product behavior belongs in
[features](features.md); routing in
[network selection](network-and-host-selection.md); connection UX in
[pairing feedback](pairing-feedback.md).

## Transport and JSON

Direct mode exposes `/ws` on the selected LAN adapter. Relay mode binds the
same host application to loopback and multiplexes it through one authenticated
outbound relay WebSocket. The Cloudflare Durable Object and standalone Node
adapters share the same room contract: one authenticated host, at most 64
device sockets, 64 KiB inner frames, bounded forwarding queues, and no stored
commands. The opaque relay payload permits only the fixed 26-byte encrypted
frame overhead above that limit; endpoints enforce 64 KiB again after decryption.

When enhanced capabilities are enabled with Direct, the hosted HTTPS PWA uses
Secure Direct instead of `/ws`: `/v1/secure/device/<route>` and
`/v1/secure/host/<route>` carry bounded signaling through the existing relay
Worker, then one ordered reliable `voltura-control` DataChannel carries the
unchanged controller JSON directly over the selected private IPv4 LAN. The
service reuses routing-key host authentication and `RelayEnvelope` correlation,
forwards exactly one `{ "type": "secure.offer", "sdp": "..." }` and one
`{ "type": "secure.answer", "sdp": "..." }`, and retains no established
controller state. SDP is limited to 32 KiB, encoded signaling to 64 KiB,
authentication control to 4 KiB, output buffering to 256 KiB, and negotiation
to ten seconds. Signaling loss after answer application does not close a healthy
DataChannel. A transient ICE `disconnected` state is allowed to recover; only
terminal `failed` or `closed` peer states close Secure Direct control. The selected local address must be the configured private IPv4
adapter address, and the selected remote address must be private IPv4. No STUN
or TURN server is configured. Public, loopback, wrong-interface, malformed, or
unverifiable selected addresses remain rejected. Relay mode never uses this
controller DataChannel.

An unauthenticated relay host candidate never owns the route. A bounded set of
candidates has ten seconds to prove the routing key; the first valid proof
claims the route and the other candidates are closed. Only the authenticated
socket blocks another host. A device Connected envelope carries
a 16-byte route-scoped source key derived by the relay for host pairing-rate
isolation. Hosts accept an empty payload from earlier relay adapters and isolate
that connection by session ID; other Connected payload lengths are rejected.
Host close envelopes contain only the session ID and make either relay adapter
close that device socket; deploy the relay adapter before a host that emits this
additive envelope kind.

Relay does not change command-latency ownership. Usage analytics, TURN renewal,
screen capture/encoding, logging, registry access, and host UI work never hold
the command receive/send path. A full bounded queue or relay backpressure closes
that slow session instead of accumulating delayed commands. Screen media uses
its separate WebRTC transport and can fail while commands remain connected.

- Allowed origins: missing, same-origin, configured development, loopback, and
  private LAN. Unrelated public origins are rejected before upgrade.
- Maximum 64 sessions; `pair.hello` deadline 10 seconds; authenticated receive
  idle timeout 5 minutes.
- `/ws` accepts maximum 64 KiB text messages across fragments. Oversize closes
  with 1009; binary is rejected. Screen media never enters this serialized
  command queue; it uses a negotiated WebRTC media track and data channel.
- Per-socket sends are serialized with a 5-second deadline; close has 1 second.
  Status updates are coalesced.
- Required fields are present. Optional fields are omitted, not `null` or empty
  placeholders. Empty values are valid only where stated (`clipboard` text and
  `appLaunchActions` may be empty).
- After authentication, unknown message types, malformed JSON shapes, duplicate
  or undeclared fields close with policy violation.

Server-originated dynamic strings use these current maxima unless a feature
declares a smaller bound: `operationId` 64, human-readable `message` 240,
`code`/`reason` 80, PC name 120, adapter description 256, IP address 64, URL
512, and build/session identifiers 128 characters. Mobile rejects an entire
server frame that exceeds its applicable bound; producers also bound dynamic
status values before sending them.

Wire changes update the test server-frame catalog and follow
[risk-based validation](setup.md#validation-by-change).

## Optional usage-statistics HTTPS contract

This first-party contract is independent of pairing and controller transport.
It is active only in the Windows host after explicit persisted Allow. The PWA
never calls it. The fixed endpoint is:

```text
POST https://voltura.se/air/telemetry/v1/ingest.php
Content-Type: application/json[; charset=utf-8]
```

The request body is at most 4,096 bytes. There are no CORS headers and no
configurable destination. Version 1 has this exact object and no unknown or
missing members:

```json
{
  "schemaVersion": 1,
  "installationId": "0c99c983-09f8-42af-879c-42b51d625c69",
  "batchId": "1f2e0a85-4115-40f2-b8cc-e46160186cb3",
  "hostVersion": "1.0.5",
  "hostStarts": 1,
  "connections": {
    "standardLocal": 0,
    "enhancedDirect": 0,
    "relay": 0
  },
  "features": {
    "trackpad": 0,
    "keyboard": 0,
    "dictation": 0,
    "mediaControls": 0,
    "presentation": 0,
    "customScreens": 0,
    "files": 0,
    "screenViewing": 0,
    "phoneWebcam": 0,
    "gyroMouse": 0
  }
}
```

`schemaVersion` is integer `1`. Both IDs are canonical lowercase hyphenated
UUIDs. `hostVersion` is the repository SemVer form, ASCII, and at most 32
characters. `hostStarts` is integer 0–1; every other count is integer 0–65,535;
at least one count is positive. There is no client timestamp: the successful
MariaDB UTC receipt date owns daily attribution. Retrying across midnight moves
that batch to its successful receipt day.

The server derives separate HMAC-SHA-256 installation, installation-rate, and
source-rate keys. It permits 24 requests per installation UUID per UTC day, 240
per source HMAC per UTC hour, and at most 50,000 accepted new batches per UTC
day. Deduplication by installation pseudonym and batch UUID and the daily
counter upsert occur in one transaction. The source IP is used only to derive
the short-lived rate HMAC; User-Agent is ignored. The endpoint stores no raw
UUID, IP address, request JSON, or event history.

Responses are fixed and smaller than 1 KiB:

| Status | Contract                                                                         |
| ------ | -------------------------------------------------------------------------------- |
| `202`  | New or duplicate accepted; exact body `{"schemaVersion":1,"status":"accepted"}`. |
| `400`  | Invalid JSON/schema/version/value; fixed generic invalid body.                   |
| `405`  | Wrong method; `Allow: POST`.                                                     |
| `413`  | Body exceeds 4,096 bytes.                                                        |
| `415`  | Unsupported media type.                                                          |
| `429`  | Installation, source, or service-wide bound reached; bounded `Retry-After`.      |
| `503`  | Configuration/database unavailable; no internal detail.                          |

The host seals first after a random 5–15 minutes and every six hours thereafter.
It reuses one batch UUID for the initial attempt and retries after 1, 5, and 15
minutes. Each HTTPS attempt has a five-second timeout and reads at most 1 KiB.
Only `202` with the exact accepted body completes the batch. `429`, transport
errors, timeouts, malformed success, and `5xx` use the remaining schedule;
other `4xx` responses permanently reject that batch. Disable cancels requests
and backoff and discards unsent data without a flush. During graceful Windows
host shutdown, the host seals the active in-memory accumulator and makes one
final five-second HTTPS attempt without the retry schedule; a failed final
attempt is discarded. Crashes or power loss can still lose unsent counters.

`GET https://voltura.se/air/telemetry/v1/health.php` returns `204` only when the
configuration, PDO connection, required telemetry columns, and maintenance row
are readable. It otherwise returns a generic `503` and never exposes counts,
schema, credentials, or identifiers.

## Pairing link

The host creates an absolute HTTP/HTTPS `/pair` URL containing one short-lived
bootstrap secret and no fragment. It does not include a host identity key,
fingerprint, reconnect key, or second identifier; `/` imports no pairing
credential.

| Parameter | Contract                                                                                                           |
| --------- | ------------------------------------------------------------------------------------------------------------------ |
| `t`       | Required 32-character URL-safe Base64 short-lived token.                                                           |
| `v`       | Required semver metadata; validated, but never authentication, compatibility enforcement, or cache busting.        |
| `h`       | Optional WebSocket host origin or port; a port resolves against the page host. Routing only, never authentication. |
| `d`       | Optional non-secret client ID added by mobile.                                                                     |
| `n`       | Optional non-secret device name added by mobile.                                                                   |

Mobile removes `t` from the current URL before name confirmation/authentication.
`/pair`, `v`, hints, and saved profiles never bypass token pairing or reconnect
proof. Manual host forms belong in
[network selection](network-and-host-selection.md).

Relay uses `https://voltura.se/a/<22-character-route>?v=<version>#<token>`.
`/a` redirects to `/air/app/` without a fragment so the browser preserves the
secret locally and never sends it in an HTTP request. The route derives from a
separate persistent routing public key and is not the PC name. A custom relay
uses the longer hosted-app form with a validated Base64url HTTPS endpoint; it
changes the saved profile, not the pairing or command protocol.

Relay always reports `capabilities.enhancedCapabilities.enabled` as `true`
because its controller already runs in the secure hosted app. Direct reports
the saved enhanced-capabilities preference: `true` for `/s` and `false` for
Standard Local `/pair`.

Secure Direct uses `https://voltura.se/s/<22-character-route>?v=<version>#<token>`;
`/s` redirects to `/air/app/?m=s&r=<route>&v=<version>` while preserving the
fragment. `/a` explicitly selects Relay and `/s` explicitly selects Secure
Direct; neither falls back or switches automatically. Both official hosted
paths use the same hosted profile ID and reconnect-key slot. The local HTTP
origin remains a separate device identity and storage boundary.

Debug host runs use `https://voltura.se/d/<route>?v=<version>#<token>` instead
of `/s`. `/d` redirects to the separately built `/air/dev-app/` scope and then
normalizes to the same Secure Direct connection identity and unchanged protocol.
Packaged hosts never generate `/d` links.

Tokens last five minutes. Connect rotates 15 seconds before expiry and retains
only the prior token for up to that 15-second overlap. Successful pairing
consumes both slots and creates a new visible token.

## Authentication

Every control session starts with `pair.hello`. Fresh pairing generates a P-256
reconnect key pair and a 32-byte client nonce. Mobile retains the reconnect
private key and the QR token, sends only `SHA-256(token)` as `pairTokenId`, and
never transmits the token itself over the socket. `clientId` is non-secret.

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone",
  "pairTokenId": "base64url-sha256-token-id",
  "clientNonce": "base64url-32-byte-nonce",
  "reconnectPublicKey": "base64url-uncompressed-p256-public-key"
}
```

The host resolves only its current/overlap token by ID, generates a server
nonce, and returns its persistent P-256 public identity and an HMAC-SHA-256 host
proof:

```json
{
  "type": "pair.bootstrap.challenge",
  "clientId": "browser-generated-id",
  "clientNonce": "base64url-32-byte-nonce",
  "serverNonce": "base64url-32-byte-nonce",
  "hostIdentity": {
    "publicKey": "base64url-uncompressed-p256-public-key",
    "fingerprint": "base64url-sha256-public-key"
  },
  "proof": "base64url-hmac-sha256-host-proof"
}
```

The host and client HMAC transcripts are newline-separated UTF-8 values:
direction prefix (`VolturaAir pairing host:v1` or
`VolturaAir pairing client:v1`), Base64url UTF-8 `clientId`, client nonce,
server nonce, reconnect public key, host public key, and host fingerprint. The
QR token's UTF-8 bytes are the HMAC key. Mobile verifies the public-key
fingerprint and host proof before returning:

```json
{
  "type": "pair.bootstrap.proof",
  "clientId": "browser-generated-id",
  "proof": "base64url-hmac-sha256-client-proof"
}
```

Only a valid client proof consumes both token slots and stores the reconnect
public key plus pinned host identity. For an existing `clientId`, fresh pairing
replaces its keys and revokes active sockets without adding another record. A
saved profile without a matching host identity pin is rejected and must pair
again; the wire does not accept the earlier plaintext-token hello or a QR
identity parameter.

For Relay, successful proof verification is followed by a signed ephemeral
P-256 ECDH exchange. Its transcript binds route, client ID, pinned host
identity, both ephemeral keys, and a nonce. HKDF-SHA256 derives separate
AES-256-GCM keys and nonce prefixes for each direction. Frames contain version,
direction, and a strictly monotonic 64-bit counter as authenticated data. The
host commits fresh pairing and consumes the token only after this exchange;
`pair.accepted` is the first encrypted application frame. Alteration, replay,
wrong direction, skipped counters, route mismatch, or identity mismatch closes
the session.

Reconnect omits token/key:

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone"
}
```

Known clients receive one random session-owned challenge:

```json
{
  "type": "pair.challenge",
  "clientId": "browser-generated-id",
  "challenge": "base64url-host-challenge"
}
```

Sign UTF-8 `VolturaAir reconnect:v1:<clientId>:<challenge>` using ECDSA P-256,
SHA-256, IEEE P1363 fixed-field format:

```json
{
  "type": "pair.proof",
  "clientId": "browser-generated-id",
  "signature": "base64url-p1363-signature"
}
```

The host consumes the challenge before verification. A session accepts one
proof; cross-session, different-challenge, reused, and post-restart proofs fail.

Success:

```json
{
  "type": "pair.accepted",
  "clientId": "browser-generated-id",
  "pcName": "WINDOWS-PC",
  "paired": true,
  "hostIdentity": {
    "publicKey": "base64url-uncompressed-p256-public-key",
    "fingerprint": "base64url-sha256-public-key"
  },
  "capabilities": {
    "enhancedCapabilities": { "enabled": true },
    "remoteInput": true,
    "gestureDebug": false,
    "inputAck": true,
    "inputContextV1": true,
    "power": {
      "lock": true,
      "lockAvailability": "notExplicitlyDisabled",
      "blackoutDisplay": true,
      "displayOff": false,
      "screenSaver": true,
      "screenSaverAvailable": false,
      "signOut": false,
      "restart": false,
      "shutdown": false
    },
    "awake": {
      "canControl": false,
      "active": false,
      "mode": "off"
    },
    "sleep": true,
    "volume": true,
    "remoteLaunch": true,
    "urlOpen": { "canOpen": false },
    "textTransfer": true,
    "clipboardRead": false,
    "diagnostics": { "canView": true },
    "screenView": {
      "enabled": false,
      "permissionGranted": false,
      "canView": false,
      "requiresRepair": false,
      "encrypted": true,
      "maxWidth": 1920,
      "maxHeight": 1080,
      "maxFramesPerSecond": 30,
      "directPointer": { "permissionGranted": true }
    },
    "phoneWebcam": {
      "enabled": true,
      "permissionGranted": false,
      "canUse": false,
      "requiresRepair": false,
      "microphoneAvailable": false,
      "maxWidth": 1920,
      "maxHeight": 1080,
      "maxFramesPerSecond": 30
    }
  },
  "host": {
    "hostVersion": "1.2.3",
    "webClientBuildId": "opaque-build-id",
    "pcName": "WINDOWS-PC",
    "defaultRemoteMode": "standard",
    "selectedAdapterName": "Wi-Fi",
    "selectedIp": "192.168.1.50",
    "selectedPort": 51395,
    "webSocketUrl": "ws://192.168.1.50:51395/ws",
    "textTransferTarget": {
      "mode": "focused",
      "displayName": "Currently focused application",
      "available": true
    },
    "pointerSpeed": 100,
    "customPointerEnabled": false,
    "showModeButtons": true,
    "controlDepth": true,
    "accentColor": null,
    "accentColorOverridden": false,
    "inputBlockedByElevation": false
  }
}
```

Fresh/reconnect acceptance has the same shape and confirms the pinned public
host identity. It never includes private keys, reconnect credentials,
challenges, proofs, or tokens.

```json
{ "type": "status.get" }
```

`status` contains `type`, `connected`, `message`, `pcName`, and the same
`capabilities`/`host` objects. Status may be pushed when host state changes.

Authenticated metadata is not authentication state:

- `defaultRemoteMode`: `standard`, `youtube`, or `kodi`; advisory when mobile
  has no saved PC-specific override.
- `appLaunchActions`: `{ id, label, kind }[]`; empty when launch permission is
  off. ID is opaque; label is 1–10 characters; kind is `browser`, `spotify`,
  `vlc`, `powerpoint`, or `custom`. Paths, URLs, and arguments are excluded.
- `urlOpen.canOpen`, `remoteInput`, `textTransfer`, `clipboardRead`: effective
  device permissions.
- `diagnostics.canView`: effective **View diagnostics** permission. The capability
  remains present when blocked so the mobile destination can explain recovery.
- `apps`: `enabled`, effective `permissionGranted`, authenticated `canUse`, and
  `previewAvailable`. The capability remains present when blocked.
  `previewAvailable` additionally requires effective Screen viewing permission;
  application-launch and Voltura-host-window control remain separate gates.
- `terminal`: `enabled`, effective `permissionGranted`, authenticated `canUse`,
  `requiresRepair`, host-wide `active`, device-specific `ownedByClient`, an
  owner-only `terminalId`, `shell: "windows-powershell"`, and
  `reconnectGraceSeconds: 900`. A denied capability remains present for recovery UI.
- `textTransferTarget`: exactly `{ mode, displayName, available }`; mode is
  `focused`, `clipboard`, or `configured`. It excludes paths, process/window
  IDs, matching rules, and clipboard content.
- `pointerSpeed`: effective device speed. `customPointerEnabled`: host-wide.
  `showModeButtons` and `controlDepth`: effective per-device appearance values.
  `accentColor` is the effective canonical uppercase `#RRGGBB` seed or `null`
  for the built-in palette; `accentColorOverridden` reports whether the
  authenticated device overrides the host's global device default.
  `inputBlockedByElevation`: higher-integrity foreground block.
- `webClientBuildId`: the client bundle served by a Direct host, independent of
  `hostVersion`. It can refresh only a Direct host-served PWA. Relay opens the
  public hosted PWA, so a differing PC bundle ID never triggers a refresh.
- Developer mode adds `developerMode: true` and `developerSessionId`.
- `screenView` is always present for a supporting host so the tool remains
  discoverable. `enabled`, `permissionGranted`, and `requiresRepair` explain
  why `canView` is false. Its `maxWidth: 1920`, `maxHeight: 1080`, and
  `maxFramesPerSecond: 30` members are frozen legacy capability markers retained
  for already-published clients; they are not adaptive stream ceilings. The
  selected adaptive profile bounds the actual stream. Supporting hosts also set
  `receiverQualityFeedback: true` so current controllers can report aggregate
  WebRTC decoder health.

Adapter metadata may reveal local hardware and appears only in explicit redacted
diagnostics.

Rejection:

```json
{ "type": "pair.rejected", "reason": "invalid-token" }
```

| `reason`          | Meaning                                                    |
| ----------------- | ---------------------------------------------------------- |
| `pair-first`      | Non-pairing message before authentication.                 |
| `invalid-token`   | No match with current/overlap token.                       |
| `expired-token`   | Matching retained token expired.                           |
| `stale-token`     | No active token state.                                     |
| `device-revoked`  | No device record for `clientId`.                           |
| `invalid-proof`   | Signature failed for the session challenge/public key.     |
| `rate-limited`    | Too many failed unauthenticated attempts from the address. |
| `invalid-message` | Invalid pairing JSON shape.                                |

Mobile derives `VAIR-PAIR-*`; no diagnostic-code field is sent. Unknown reasons
remain diagnosable instead of exposing raw protocol text.

Authenticated utility messages:

```json
{ "type": "pair.disconnect" }
{ "type": "pair.disconnect.accepted" }
{ "type": "device.rename", "deviceName": "Joakim iPhone" }
{ "type": "pointer.speed.set", "pointerSpeed": 65 }
{ "type": "appearance.mode-buttons.set", "showModeButtons": false }
{ "type": "appearance.control-depth.set", "controlDepth": false }
{ "type": "appearance.accent-color.set", "accentColor": "#5FC8B4" }
{ "type": "appearance.accent-color.set", "accentColor": null }
{ "type": "custom.pointer.set", "enabled": true }
{ "type": "health.ping" }
{ "type": "health.pong" }
```

The host durably removes the paired device before sending
`pair.disconnect.accepted`, then closes the controller transport. Secure Direct
revocation succeeds only after this acknowledgement; a DataChannel close alone
does not confirm the mutation.

`deviceName` must contain non-whitespace text; mobile substitutes its default
before sending a blank edit. Pointer speed and appearance changes are sent only
from user action. Appearance changes set an override for the authenticated
device. The host Devices page can restore inheritance for the controls exposed
there; accent inheritance is restored from the device's Appearance settings.
An accent-color string must use canonical uppercase `#RRGGBB`; `null` clears
the authenticated device's override so it inherits the host default. Older
clients ignore the additive status fields, while a current client treats an
omitted field as a host without synchronized accent support.
`health.pong` is liveness only; it contains no metadata/capability/audio state.
Any valid client message resets the receive timeout.

## Diagnostics

Diagnostics uses the authenticated paired-device connection. It is strictly
request/response: the host sends no proactive diagnostics snapshot and performs
no polling. The client requests one snapshot on page open or explicit Refresh.

```json
{ "type": "diagnostics.get", "operationId": "diagnostics-1" }
```

`operationId` uses the normal bounded operation-ID rules. The request has no
other fields. The host checks the effective **View diagnostics** permission
before collecting system data.

```json
{
  "type": "diagnostics.get.result",
  "operationId": "diagnostics-1",
  "succeeded": true,
  "message": "Diagnostics loaded.",
  "snapshot": {
    "generatedAt": "2026-08-25T12:00:00.0000000Z",
    "hostVersion": "1.1.0",
    "connectionMethod": "direct-lan",
    "enhancedCapabilities": "enabled",
    "relayStatus": "disabled",
    "relayEndpointType": "not-active",
    "relayFailureCode": "none",
    "pairingState": "connected",
    "windowsLockPolicy": "notexplicitlydisabled",
    "applicationLogging": "disabled",
    "applicationLogRetention": "7 days",
    "pairedDeviceCount": 1,
    "connectedDeviceCount": 1,
    "pcName": "WINDOWS-PC",
    "selectedAdapter": "Ethernet",
    "selectedIp": "192.168.1.50",
    "selectedPort": 51395,
    "advisories": [],
    "computer": {
      "windows": "Windows 11 Pro, version 24H2, build 26100",
      "system": "Manufacturer Model",
      "processor": "Processor model",
      "logicalProcessors": "8",
      "primaryDisplay": "3840 × 2160 at 60 Hz",
      "installedMemory": "16.0 GiB",
      "availableMemory": "8.0 GiB",
      "systemDisk": "500.0 GiB total, 200.0 GiB free",
      "systemUptime": "1d 2h 3m"
    }
  }
}
```

The success snapshot is an explicit allowlist. `enhancedCapabilities` reports
whether the active host endpoint enables the browser capabilities that require
the Voltura certificate and HTTPS. `advisories` contains at most two objects with
exactly `name`, `summary`, `details`, and `code`. Each computer probe fails
independently and uses `Unavailable` when its value cannot be read. The web-client
version, browser, and local display mode remain client-owned and are not sent by
the host.

The snapshot excludes application/data/executable paths, the Windows username,
other device names, raw host and WebSocket URLs, relay identifiers or credentials,
tokens, query strings, and log contents. Unknown snapshot fields are rejected by
the client.

```json
{
  "type": "diagnostics.get.result",
  "operationId": "diagnostics-1",
  "succeeded": false,
  "code": "permission-denied",
  "message": "Diagnostics viewing is disabled for this device."
}
```

Failures contain no `snapshot`. Expected codes are `permission-denied` and
`diagnostics-unavailable`; a diagnostics failure does not terminate the paired
connection.

## Encrypted screen viewing

Screen viewing is video-only, one display and one viewer at a time. Its adaptive
capture profiles retain the display aspect ratio and stay within the sender's
advertised H.264 frame-size and frame-rate limits. These bounded control messages
use the authenticated `/ws` session:

```json
{ "type": "screen.view.sources.get", "operationId": "screen-sources-1" }
{ "type": "screen.view.start", "operationId": "screen-start-1", "displayId": "display-1-1", "clientSignature": "base64url-p1363-signature" }
{ "type": "screen.view.answer", "operationId": "screen-start-1", "answerSdp": "bounded WebRTC answer SDP", "clientSignature": "base64url-p1363-signature" }
{ "type": "screen.view.quality", "operationId": "screen-start-1", "width": 3840, "height": 2160, "framesPerSecond": 30, "framesDecoded": 60, "framesDropped": 0, "freezeCount": 0, "packetsLost": 0 }
{ "type": "screen.view.source.set", "operationId": "screen-source-1", "displayId": "display-1-2" }
{ "type": "screen.view.stop", "operationId": "screen-stop-1" }
```

The quality message is sent only while its exact operation is active. Counts are
non-negative interval deltas bounded to 1,000,000; dimensions are 0..16384 and
frame rate is 0..240. It contains no screen content, cursor coordinates, typed
text, or persistent data.

`screenView.directPointer` is present when the host supports direct desktop
mouse control. Its `permissionGranted` value is the effective **Pointer and
keyboard** permission; Screen viewing remains independently permission-gated.
Clients omit the control when the object is absent.

While the browser-local Mouse mode is active, the client sends strict Screen-
owned input messages on the same authenticated socket:

```json
{ "type": "screen.pointer.move", "seq": 201, "displayId": "display-1-1", "x": 0.25, "y": 0.75 }
{ "type": "screen.pointer.button", "seq": 202, "displayId": "display-1-1", "x": 0.25, "y": 0.75, "button": "left", "action": "down" }
{ "type": "screen.pointer.button", "seq": 203, "displayId": "display-1-1", "x": 0.4, "y": 0.8, "button": "left", "action": "up" }
{ "type": "screen.pointer.wheel", "seq": 204, "displayId": "display-1-1", "x": 0.4, "y": 0.8, "dx": 0, "dy": -8 }
```

`x` and `y` are finite normalized coordinates from 0 through 1 over the
displayed image, inclusive. Buttons are `left` or `right`; direct button actions
are `down` or `up`. Wheel deltas retain the input bound of -5000 through 5000.
The host accepts these messages only from the active viewer for its exact
selected display and only while both Screen viewing and **Pointer and keyboard**
are allowed. It maps against host-owned rotated monitor bounds and the complete
virtual desktop. A stale display, missing active view, or permission denial
returns a recoverable `input.error` without closing the socket.

Source, start, answer, source-switch, and stop results echo `operationId`; start
also echoes `displayId`. The start request signs UTF-8
`VolturaAir screen-view:start:v2:<clientId>:<operationId>:<displayId>` with the
registered reconnect key. A successful start result supplies a bounded WebRTC
offer SDP and a host-identity signature over UTF-8
`VolturaAir screen-view:offer:v2:<clientId>:<operationId>:<displayId>:<offerHash>`,
where `offerHash` is unpadded base64url SHA-256 of the exact UTF-8 offer SDP.
The browser verifies that signature against its pinned PC identity before
applying the offer or rendering pixels.

When the PC owner uses the tray Stop action, the host sends the current viewer
one terminal command-channel event as it ends the media session:

```json
{ "type": "screen.view.ended", "operationId": "screen-start-1", "reason": "host-stopped", "message": "The PC stopped screen viewing." }
{ "type": "screen.view.ended", "operationId": "screen-start-1", "reason": "permission-revoked", "message": "The PC stopped screen viewing and disallowed this device." }
```

The terminal event carries the accepted start operation. The client clears the
video and disables stage input only when that ID still owns its active session;
a delayed event from an earlier session is ignored. The two listed reasons are
the complete current contract. Missing operation IDs, other reasons, and the
old event shape are rejected rather than interpreted as legacy variants.

An authorized source request succeeds with code `accepted` even when its
`sources` array is empty, allowing the client to report that no connected
display is available. Expected discovery failures such as unavailable Desktop
Duplication return their bounded capture code and message as a failed screen
result; they do not close the authenticated command socket. Start and source
switch apply the same discovery boundary.

The offer reserves the single viewer slot for at most 15 seconds. The browser
creates a WebRTC answer and signs UTF-8
`VolturaAir screen-view:answer:v2:<clientId>:<operationId>:<displayId>:<offerHash>:<answerHash>`
with its reconnect key. `answerHash` is the same unpadded base64url SHA-256
construction over the exact answer SDP. Invalid, mismatched, expired, or
oversized signaling is rejected and releases the pending peer.

Direct mode uses host ICE candidates without STUN or TURN. Relay mode requests
a signed, single-use 15-minute TURN credential from the active route and uses
relay-only ICE. Mobile renews before expiry by stopping the old peer and
performing a fresh signed WebRTC negotiation. The start result may include
bounded `iceServers`, `turnExpiresAt`, `relayUsageBytes`,
`relayUsageCheckedAt`, and `relayScreenQuality`; direct results omit them.
`relayScreenQuality` is `High`, `Standard`, or `DataSaver`, representing an
8, 4, or 2 Mbps sender ceiling respectively. A provider-forced Data saver result
continues to override the locally selected quality.
Some browsers can gather usable relay candidates without changing their ICE
gathering state to `complete`. In Relay mode the browser may therefore send its
answer after at least one relay candidate is present in `localDescription` and
candidate events have been quiet for 350 milliseconds. Before signing and
sending, every candidate line in the answer SDP must be `typ relay`; an empty or
mixed candidate set is rejected. The 10-second timeout remains a failure when
no relay candidate exists. Direct mode continues to wait for gathering to
complete.
The authenticated relay TURN response carries `usageBytes`, `checkedAt`, and
optional provider-owned `usageWarningBytes` and `usageCutoffBytes`. The host
retains them as one immutable runtime snapshot, not registry settings. Missing
or invalid limits hide the meter rather than inventing host defaults. A blocked
response still carries the usage snapshot so the host can explain the cutoff.
The hosted browser accepts Cloudflare TURN over UDP and TCP/TLS 443. The Windows
host selects issued `turns` entries and maps each to an ephemeral loopback TURN
UDP endpoint consumed by the existing libjuice ICE/TURN owner. A bounded,
certificate-validating bridge carries those TURN messages over TLS/TCP to the
original hostname and port. It preserves STUN messages and adds or removes only
the four-byte alignment padding required for ChannelData on a stream transport.
Reserved, malformed, oversized, non-loopback, or second-owner traffic is
rejected. Consequently the PC's external relay-screen traffic is TCP 443; UDP is
confined to loopback. DTLS-SRTP remains the media security boundary above TURN.
The selected display is sent as H.264 RTP on a send-only video track, with
DTLS-SRTP providing media confidentiality, integrity, and replay protection.
Cursor and terminal status records use the ordered reliable `screen-events`
WebRTC data channel, protected by DTLS. Capture starts only after the peer,
video track, and event channel are connected. The data channel record types are:

- `4`: signed 64-bit sequence, visibility, signed cursor position, hotspot,
  bounded dimensions, and an optional bounded PNG cursor shape; and
- `5`: a bounded UTF-8 stopped/paused code and message for display, lock/session,
  permission, or capture-device loss.

Desktop Duplication supplies the selected GPU frame and cursor metadata. A
hardware Media Foundation transform converts the frame to baseline H.264. The
offer advertises level 5.2 with level asymmetry enabled; the profile ladder keeps
each encoded frame and its macroblocks per second within that level, preserves
the source aspect ratio, and permits up to 60 frames per second. With level
asymmetry enabled, the answer's receive level is not used as a sender-resolution
ceiling. Direct starts at native resolution and 30 fps.
Automatic derives its minimum readable dimensions from physical display pixels
and effective Windows DPI; Quality keeps native dimensions; Data saver may use
smaller dimensions. Relay uses Automatic or Data saver within its 8/4/2 Mbps
ceiling. The RTP sender supports sender reports, NACK retransmission, and receiver
keyframe requests. A monotonic capture pacer
drops desktop presents that arrive before the selected profile's next frame slot.
Receiver-health reports and sustained sender backpressure move one profile at a
time; healthy decoding permits reversible upward probes. Buffered media and event
data have fixed upper bounds. An encoder-rejected profile becomes eligible again
after its cooldown.
Source switching resets the duplication/encoder session and forces a keyframe.
Permission revocation,
disconnect, lock/session loss, display removal, stop, or host shutdown releases
the peer, encoder, capture session, native resources, and any direct mouse
buttons held by that Screen session. Source switches, permission loss, and
native input failure also release held direct buttons. Pointer coordinates are
never logged.

## Phone webcam

Phone webcam is available only through the HTTPS hosted PWA used by Enhanced Direct
and Relay. Standard Local HTTP does not request camera capture. The capability is
always present on a supporting host:

- `enabled` reports an installed current native virtual camera;
- `permissionGranted` is the resolved device-profile Phone webcam policy;
- `canUse` additionally requires the current pinned host identity;
- `requiresRepair` reports a missing current host-identity pin;
- `microphoneAvailable` reports whether the host has resolved an active base VB-CABLE render endpoint; and
- `maxWidth`, `maxHeight`, and `maxFramesPerSecond` describe the requested ceiling,
  not a claim about the actual browser capture.

The phone sends one bounded signed start, answer, and stop sequence on the existing
authenticated controller connection:

```json
{ "type": "phone.webcam.start", "operationId": "webcam-start-1", "captureWidth": 1920, "captureHeight": 1080, "captureFps": 30, "useMicrophone": false, "clientSignature": "base64url-p1363-signature" }
{ "type": "phone.webcam.answer", "operationId": "webcam-start-1", "answerSdp": "bounded WebRTC H.264 and optional Opus answer SDP", "clientSignature": "base64url-p1363-signature" }
{ "type": "phone.webcam.stop", "operationId": "webcam-stop-1" }
```

The start signature covers UTF-8
`VolturaAir phone-webcam:start:v2:<clientId>:<operationId>:<captureWidth>:<captureHeight>:<captureFps>:<useMicrophone>`.
A successful `phone.webcam.start.result` echoes `operationId` and contains the
bounded H.264 receive offer, with one Opus audio section only when requested, plus `hostSignature` and `maximumBitrate`. The host
signature covers UTF-8
`VolturaAir phone-webcam:offer:v2:<clientId>:<operationId>:<offerHash>`. The browser
verifies it against the pinned PC identity, requires media to match the request exactly,
and creates send-only video and optional audio. Its answer signature covers
UTF-8
`VolturaAir phone-webcam:answer:v2:<clientId>:<operationId>:<offerHash>:<answerHash>`.
Hashes use the same unpadded base64url SHA-256 construction as Screen viewing.

Start and answer SDP are bounded to 32 KiB, dimensions to 1 through 4096, frame rate
to 1 through 60, and operation/signature fields to the shared authenticated-message
bounds. The current v2 shapes are exact and v1 shapes are rejected. `useMicrophone: true`
requires the advertised local target and exactly one Opus 48 kHz stereo media section;
false rejects audio. One pending or active producer exists per host. The offer expires after
20 seconds. Busy, invalid proof, expired offer, invalid answer, permission denial,
unavailable WebRTC/decoder, `microphone-unavailable`, and missing TURN credentials return bounded failure
codes without closing the command channel.

Enhanced Direct uses host ICE candidates and a 12 Mbps sender ceiling without STUN
or TURN. Relay supplies the existing `iceServers`, `turnExpiresAt`, aggregate usage
snapshot, quota-derived quality, and an 8/4/2 Mbps effective ceiling in
`maximumBitrate`. `relayQuality` remains `Standard` or `DataSaver`; it is omitted
for High so previously published clients can consume the existing result shape.
Both peers reject a
Relay answer containing an empty or non-relay candidate set. Before the 15-minute
credential expires, the visible browser stops and disposes the old session, obtains
fresh credentials, and creates one new signed peer. The old peer is never
renegotiated or reused.

`phone.webcam.answer.result` confirms host answer acceptance;
`phone.webcam.stop.result` confirms idempotent client stop. The host may send a
terminal event correlated to the started operation with one current reason:

```json
{
  "type": "phone.webcam.ended",
  "operationId": "webcam-start-1",
  "reason": "transport-lost",
  "message": "The Phone webcam session ended."
}
```

Accepted reasons are `stopped`, `connection-lost`, `transport-lost`,
`decoder-failed`, `audio-failed`, `permission-revoked`, `pairing-revoked`, `host-stopped`, and `offer-expired`.
Clients ignore terminal events for an older operation. Every terminal path releases the peer, decoder, bounded queues, local frame pipe input, and
phone tracks, Opus decoder, audio queue, and WASAPI output. Mute changes the browser audio track's enabled state and has no protocol message. Camera switching, rotation recovery, and bounded outbound-stall
recovery are browser-local `replaceTrack` operations on the same healthy peer and
add no protocol message. Page hiding is an immediate stop; one fresh
foreground session is the only automatic recovery attempt for that background
transition.

## Custom screens

`capabilities.customScreens` contains the current catalog revision and assigned
screen summaries in mobile Menu order:

```json
{
  "customScreens": {
    "catalogRevision": "opaque-revision",
    "screens": [
      {
        "id": "screen.opaque-id",
        "name": "Media desk",
        "revision": "opaque-screen-revision"
      }
    ]
  }
}
```

Clients that do not know the capability ignore it. Assignment grants catalog
visibility only; action permission is evaluated separately.

After authentication, mobile reports a bounded CSS viewport after connection
and on a debounced size/orientation change:

```json
{
  "type": "device.viewport.set",
  "width": 390,
  "height": 844,
  "orientation": "portrait"
}
```

`width` and `height` are whole values from 240 through 4096. `orientation` is
`portrait` or `landscape`. The host stores only the last value as optional
paired-device preview metadata. Older pairing records omit it.

Fetch one assigned visual definition:

```json
{
  "type": "custom.screen.get",
  "operationId": "local-operation-id",
  "screenId": "screen.opaque-id"
}
```

Success is:

```json
{
  "type": "custom.screen.get.result",
  "operationId": "local-operation-id",
  "succeeded": true,
  "screen": {
    "id": "screen.opaque-id",
    "name": "Media desk",
    "revision": "opaque-screen-revision",
    "orientationLayoutsEnabled": false,
    "showNavigationHeader": true,
    "sections": []
  }
}
```

The complete result is bounded by the transport's 64 KiB message limit before
the host accepts a Save. Sections contain ID, name, optional-header state,
12-column width, `content`/`fill` height and weight, zero-to-six button rows,
`buttonAlignment` (`start`, `center`, `end`, `space-between`, `space-around`, or
`space-evenly`), optional portrait/landscape overrides, and a `buttons`,
`trackpad`, `volume`, or `navigationRing` kind.
Overrides contain order and visibility plus the applicable section width or
button size/row. Missing override fields retain the shared responsive value.
Collapsible panels use `kind: "buttons"` plus the optional
`collapsible: true`, retain the button-section layout fields, use their required
name as the mobile toggle header, and may include `initiallyExpanded` for the
host-saved default state. Collapsible trackpads use `kind: "trackpad"` with the
same collapsible fields. Trackpad sections may include
`trackpadFullscreenControl` and `trackpadGyroControl`; maximizing is local UI
state and Restore returns the section to its saved responsive position. When
Gyro is enabled, the mobile trackpad exposes its Touch/Gyro movement selector;
Gyro permission and sensor availability remain client runtime state. Buttons contain only
visual/accessibility fields, row, repeat state, and resolved
availability/reason. A Laser pointer button additionally receives only
`laserPointerColor` (`default`, `red`, `green`, or `blue`); a missing field
identifies an ordinary button. Buttons for protected host actions additionally receive a
host-derived `confirmation` value (`confirm` or `hold`) and bounded warning
text. Screen JSON cannot select or weaken that safety policy. Literal text,
shortcut payloads, URLs, executable details, known-app mappings, and host
action IDs are never sent.

If a correlated `custom.screen.get.result` envelope is recognizable but its
screen definition fails client protocol validation, the mobile client completes
the pending load with a refresh-required compatibility error instead of leaving
the screen loading. That error dialog is shown only while paired; connection-loss
recovery and its reconnect actions take precedence.

When a saved screen contains exactly one distinct `knownApp` action, that
application is the target for the whole screen. The host projects every button,
navigation control, trackpad, and volume section as unavailable while the
cached target is unavailable, using one target-required explanation. It checks
the cached target again before dispatching any button. Screens with zero or
multiple distinct known-application actions retain per-control availability.

Volume sections contain no buttons, use only 3, 6, 9, or 12 columns
(25/50/75/100%), and publish resolved `volumeEnabled` and
`volumeUnavailableReason` state. The control itself uses the established
`audio.get`, `audio.mute.toggle`, and `audio.volume.set` messages and existing
volume permission.

Navigation-ring sections contain no buttons, use only 6, 8, 9, or 12 columns
(50/67/75/100%), and publish remote-input availability through the existing
`trackpadEnabled` and `trackpadUnavailableReason` fields. Directions use
`keyboard.special`; the center and surrounding trackpad surface use the
established pointer messages. No shortcut or action payload is added to the
visual definition.

`showNavigationHeader` controls the mobile Back/title row for that screen.
Literal-text and custom key/shortcut buttons use label-only presentation;
built-in, known-application, website, host-action, and approved host-local
application buttons may also use bundled icons.

The host library's **Preview** action opens
`/?customScreenPreview=<screenId>` against the host loopback address. That
entry point reads `GET /api/custom-screens/preview/<screenId>`, which accepts
loopback requests only and returns the same
bounded visual result envelope without assignment or action payloads. Preview
does not establish a command channel, so its controls cannot invoke host
actions.

Editor validation uses the same loopback-only preview response with a bounded,
memory-only draft lease. At most four leases may exist, each uses a fresh opaque
preview ID, and disposal removes it whether rendering succeeds or fails. Draft
validation never writes the store and exposes no action payload to the renderer.
It does not elevate, invoke a privileged helper, or write protected Windows
state.

The Custom screens store uses the exact version-4 lower-camel JSON shape and
rejects unknown fields. Version 4 is the only accepted store version; there is
no migration, fallback, or alternate reader. Any other or invalid file is
rejected and left unchanged with the same generic invalid-file result.
Screen names are at most 24 characters, panel/trackpad/navigation-ring names 20, button editor
names 24, and visible button labels 16.

### Portable Custom screen packages

The portable package format is JSON in a `.volturascreen` file:

```json
{
  "packageVersion": 1,
  "format": "voltura-air.custom-screen",
  "screen": {}
}
```

Package version 1 is the only package format and contains one exact current
screen definition. JSON uses exact lower-camel field names and rejects unknown
fields. Export removes all device assignments. Import rejects unsupported,
incomplete, oversized, invalid, or host-local packages, shows the screen and
action summary before saving, generates new screen/section/button IDs, and
leaves the imported screen unassigned. Host-local `appLaunch` actions cannot be
exported; portable packages use allow-listed `knownApp` profiles. A matching
portable definition requires explicit duplicate confirmation.

Portable button actions are `text`, `shortcut`, `builtIn`, `urlOpen`,
`knownApp`, `hostAction`, and `laserPointer`. A laser action requires exactly
one `color` value: `default`, `red`, `green`, or `blue`; its button cannot
repeat. `urlOpen` accepts only HTTP and HTTPS and reuses
the host URL permission and opener. `knownApp` accepts `browser`, `spotify`,
`vlc`, `zoom`, `plex`, `windowsPhotos`, or `blender`; the host focuses an
existing normal window when possible, otherwise launches only a fixed detected
profile. `hostAction` accepts only `power.lock`, `power.sleep`,
`power.hibernate`, `power.restart`, `power.shutdown`, `display.off`,
`display.duplicate`, `display.extend`, `display.pcOnly`, or
`display.secondOnly`. No action contains an executable path, shell command, or
arguments.

The `voltura-air://import?id=<uuid>` launch contract requests an approved
package from `https://voltura.se/air/screens`. An optional `source` is accepted
only for that exact HTTPS catalog origin; debug builds additionally accept the
loopback `/screens` development catalog. The host downloads a bounded package,
then applies the same validation and local review as file import. The normal
`.volturascreen` download remains the compatibility fallback when protocol
launch is unavailable.

Failure sets `succeeded: false` with `code` and `message`.
`feature-disabled` and `not-assigned` are recoverable catalog/state failures.

Invoke an available button:

```json
{
  "type": "custom.screen.invoke",
  "operationId": "local-operation-id",
  "screenId": "screen.opaque-id",
  "screenRevision": "opaque-screen-revision",
  "buttonId": "button.opaque-id",
  "enabled": false
}
```

`enabled` is optional and valid only for a Laser pointer button. When omitted,
the owner toggles off the same effective color or recolors to a different
configured color. `true` explicitly enables or recolors, and `false` is an
idempotent owner-only cleanup request. Another device cannot disable, recolor,
or take ownership. Ordinary actions reject the field.

Result:

```json
{
  "type": "custom.screen.invoke.result",
  "operationId": "local-operation-id",
  "screenId": "screen.opaque-id",
  "buttonId": "button.opaque-id",
  "succeeded": false,
  "code": "stale-screen",
  "message": "This custom screen changed on the PC. Refresh it and try again."
}
```

The host revalidates assignment, exact screen revision, button ID,
effective permission, URL/profile/host-action allow-list membership, and
current application availability for every
invocation. `stale-screen` executes nothing; mobile fetches the current screen
before another invocation. Other recoverable codes include `not-assigned`,
`button-not-found`, `permission-denied`,
`action-unavailable`, `input-blocked`, and `dispatch-failed`.

Text, key/shortcut, curated built-in, and trackpad input reuse the protected
remote-input path. Known and host-local application actions reuse the
application-launch permission and service. Website actions reuse the URL-open
permission and HTTP(S)-only service. Host/system actions reuse the matching
sleep, lock, display, restart, or shutdown permission. Restart and shutdown use
the same uninterrupted hold confirmation as Remote; sleep, hibernate, and
display-off require explicit confirmation. Only catalog-marked arrows, seek, and volume actions
may repeat. Logs may identify the opaque screen/button and outcome, but never
literal text, shortcut payloads, executable details, or viewport history.

## Input

```json
{ "type": "pointer.move", "seq": 123, "dx": 12, "dy": -4, "inputContext": "trackpad" }
{ "type": "pointer.button", "seq": 124, "button": "left", "action": "click" }
{ "type": "pointer.button", "button": "left", "action": "down" }
{ "type": "pointer.button", "button": "left", "action": "up" }
{ "type": "pointer.wheel", "seq": 125, "dx": 0, "dy": -18 }
{ "type": "pointer.zoom", "seq": 126, "direction": "in" }
{ "type": "keyboard.text", "seq": 127, "text": "Hello", "inputContext": "dictation" }
{ "type": "keyboard.special", "seq": 128, "key": "Enter", "modifiers": ["Control"] }
```

The Screen-owned absolute messages above participate in the same `inputAck`
contract. `screen.pointer.move` uses the sampled movement acknowledgement and
bounded movement backpressure; button and wheel messages are discrete.

Button actions are `click`, `down`, `up`; `click` sends press/release.
Zoom `in` means spread/pinch-out; `out` means pinch-in. Keyboard text cannot be
empty, but whitespace is valid. Single-letter virtual keys use
`keyboard.special`. Pointer and wheel `dx`/`dy` are finite values from -5000
through 5000. `Undo` and `Redo` map to Ctrl+Z/Ctrl+Y.

`capabilities.inputContextV1: true` advertises an additive functional field on
pointer, Screen pointer, keyboard, and audio/volume commands. Its closed values
are `trackpad`, `keyboard`, `dictation`, `media-controls`, `presentation`,
`custom-screens`, `screen-view`, and `gyro-mouse`. Unknown values, `null`, or an
`inputContext` on any other message violate the exact message schema. The new
PWA includes it only after the host advertises support, so old hosts receive the
old exact shape. New hosts accept omission from old or cached PWAs and do not
guess ambiguous feature categories. Relay movement coalescing preserves the
field and combines adjacent relative movements only when their contexts match.
The capability describes protocol support regardless of telemetry consent; the
host recorder remains a no-op while disabled.

When present, the context must also match the functional command owner:

| Command family     | Allowed `inputContext` values                                                         |
| ------------------ | ------------------------------------------------------------------------------------- |
| `pointer.*`        | `trackpad`, `keyboard`, `presentation`, `custom-screens`, `screen-view`, `gyro-mouse` |
| `screen.pointer.*` | `screen-view`                                                                         |
| `keyboard.text`    | `keyboard`, `dictation`, `screen-view`                                                |
| `keyboard.special` | `keyboard`, `media-controls`, `presentation`, `custom-screens`, `screen-view`         |
| `audio.*`          | `media-controls`, `custom-screens`                                                    |

Omission remains valid for old/cached clients. A closed value on the wrong
command family is a protocol error rather than a telemetry hint to reinterpret.

### Input acknowledgements

When `inputAck` is true, discrete input and sampled movement carry positive
`seq`. Successful Windows dispatch returns:

```json
{ "type": "input.ack", "seq": 123 }
```

Dispatch failure keeps the socket open:

```json
{
  "type": "input.error",
  "seq": 123,
  "code": "VAIR-INPUT-NATIVE-SEND-FAILED",
  "message": "Windows did not accept this input action. Try again."
}
```

Mobile drops the failed action, continues later input, and treats missing recent
acks as unhealthy even if heartbeat succeeds. Movement behind an outstanding
sampled ack or growing WebSocket buffer is bounded and dropped, never replayed.
Discrete button/keyboard input is not dropped by that movement limit. Connection
close never replays physical input.

## Application launch

Fixed launch requires authentication, `remoteLaunch: true`, effective launch
permission, and one supported action:

```json
{ "type": "remote.launch", "action": "openYoutube" }
{ "type": "remote.launch", "action": "startOrActivateKodi" }
```

`openYoutube` opens Chrome at the host-configured URL.
`startOrActivateKodi` activates/runs Kodi. Unknown actions violate protocol.
Clients never send paths, process names, commands, or URLs.

Configurable buttons use advertised opaque IDs:

```json
{
  "host": {
    "appLaunchActions": [
      { "id": "preset.browser", "label": "WWW", "kind": "browser" },
      { "id": "custom.1234", "label": "Notes", "kind": "custom" }
    ]
  }
}
```

```json
{
  "type": "app.launch",
  "operationId": "550e8400-e29b-41d4-a716-446655440000",
  "actionId": "custom.1234"
}
```

```json
{
  "type": "app.launch.result",
  "operationId": "550e8400-e29b-41d4-a716-446655440000",
  "actionId": "custom.1234",
  "succeeded": true,
  "code": "started",
  "message": "Started Notes."
}
```

Expected codes: `permission-denied`, `not-configured`, `invalid-target`,
`not-found`, `start-failed`. Execution failure keeps the socket open; malformed
ID closes it. Paths/arguments stay host-only and are excluded from logs.

## URL opening

```json
{
  "type": "url.open",
  "operationId": "d6420638-df52-47c1-a2bd-fd91a68899aa",
  "url": "example.com/page?q=test"
}
```

Trim input; add `https://` only when no scheme exists. Require absolute HTTP or
HTTPS, non-empty host, no control characters, maximum 2,048 UTF-16 code units.
Preserve explicit HTTP. Reject file paths, commands, malformed URLs, and other
schemes. Windows opens the normalized URL once with the default handler; no
browser fallback.

```json
{
  "type": "url.open.result",
  "operationId": "d6420638-df52-47c1-a2bd-fd91a68899aa",
  "succeeded": true,
  "code": "accepted",
  "message": "Open request sent.",
  "normalizedUrl": "https://example.com/page?q=test"
}
```

Codes: `accepted`, `permission-denied`, `invalid-url`, `unsupported-scheme`,
`launch-failed`. Failures keep the socket open and never return raw native
errors. Success means Windows accepted the request, not that the page loaded.

## Text transfer

`operationId` is a client UUID; `text` is 1–4,096 UTF-16 code units;
`sendEnter` is required.

```json
{
  "type": "text.send",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "text": "Hello from my phone",
  "sendEnter": false
}
```

Focused delivery does not change the clipboard. Clipboard mode only copies.
Managed destinations create a fresh draft or stage clipboard text. Paste occurs
only when the intended window is foreground and not elevated; otherwise success
is clipboard-only. No clipboard synchronization. LF, CRLF, and CR each become
one line break; `sendEnter` adds the final Enter/blank draft line. Host-UI focus
is refused. Partial native delivery fails and requires explicit retry.

```json
{
  "type": "text.send.result",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "succeeded": true,
  "message": "Text pasted into Windows Notepad.",
  "deliveryKind": "pasted"
}
```

`deliveryKind`: `typed`, `pasted`, `clipboard`. Codes:
`VAIR-TEXT-DENIED`, `VAIR-TEXT-HOST-FOCUSED`,
`VAIR-TEXT-NATIVE-SEND-FAILED`, `VAIR-TEXT-CLIPBOARD-FAILED`,
`VAIR-TEXT-DELIVERY-FAILED`; mobile may add
`VAIR-TEXT-RESPONSE-TIMEOUT`. Delivery failures keep the socket open.

## Clipboard read

Only `clipboard.get` reads PC clipboard text. It requires effective **Read PC
clipboard** permission, returns at most 4,096 UTF-16 code units, and alters
neither clipboard.

```json
{ "type": "clipboard.get", "operationId": "820c1314-d8a1-499d-a969-6520f681baea" }
```

```json
{
  "type": "clipboard.get.result",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "succeeded": true,
  "message": "Text fetched from the PC clipboard.",
  "text": "Example PC clipboard text"
}
```

Codes: `VAIR-CLIPBOARD-PERMISSION-DENIED`, `VAIR-CLIPBOARD-NO-TEXT`,
`VAIR-CLIPBOARD-TEXT-TOO-LONG`, `VAIR-CLIPBOARD-UNAVAILABLE`. Permission
denial performs no read.

## Host file manager

`capabilities.fileManager` is present when the host supports Files:

```json
{
  "canBrowse": false,
  "canModify": false,
  "canTransfer": false,
  "hidesProtectedSystemItems": true,
  "maxPageSize": 100
}
```

The capability remains present when permission is denied so mobile can show recovery guidance. `canBrowse` reflects **Browse and open files**. `canModify` additionally requires **Change files**. Optional `canTransfer` reflects **Transfer files**; omission means unavailable. These values resolve from the authenticated device's built-in profile or explicit Custom matrix. `hidesProtectedSystemItems` reports the separate effective default-on global/per-device **Hide protected operating system files and folders** policy. When true, the host removes entries carrying both Windows Hidden and System attributes before it creates panel revisions, counts, pages, selections, or operation references. Access profiles change no pairing or authentication shape.

Clients never send a path. The host issues opaque `sessionId`, drive/shortcut/entry IDs, panel `revision`, `continuation`, and `jobId` values. Each authenticated request has an `operationId` and only the fields listed here:

- `file.session.open`: opens/replaces the originating device's navigation session and returns `file.session.open.result` with drives, known-folder shortcuts, and left/right first pages.
- `file.page.get`: `sessionId`, `panel`, `revision`, `continuation`. A continuation is single-use, belongs to one panel revision, and returns at most 100 entries in `file.page.get.result`.
- `file.navigate`: `sessionId`, `panel`, `revision`, `targetId`; `file.refresh`: `sessionId`, `panel`; and `file.sort`: `sessionId`, `panel`, `sortBy` (`name|size|type|modified`), `descending`. Their matching `.result` contains one replacement page.
- `file.properties.get` and `file.open`: `sessionId`, `panel`, `revision`, `entryId`. Properties accepts an opaque listed entry reference or the reserved `current` location reference and returns bounded name/display path, kind, extension, optional size, timestamps, and attributes. Open accepts only a listed entry reference and delegates to the Windows Shell.
- `file.clipboard.set`: session/panel/revision plus `effect` (`copy|move`), `selectionAll`, at most 512 explicit `entryIds`, and at most 512 `excludedEntryIds`. The host writes a Shell file-drop list and preferred effect to the real Windows clipboard.
- `file.jobs.get` returns `file.jobs.status`. `file.job.create` adds session/panel/revision, `operation` (`copy|move|paste|rename|delete`), an optional bounded new name, and the selection fields. Direct Copy/Move also binds the destination panel and its rendered revision; the host validates both panel revisions together before queueing. Paste resolves a compatible Explorer/Windows file clipboard on the host; no clipboard paths cross the protocol.
- `file.job.control` carries `jobId` and `action` (`pause|resume|cancel|dismiss`). Dismiss removes only the originating device's terminal history entry; `file.job.reorder` carries `direction` (`up|down`); `file.job.conflict.resolve` carries `resolution` (`replace|skip|cancel`) and `applyToAll`. Upload conflicts instead accept `replace|keep-both|cancel` with `applyToAll: false`.

A panel page contains `panel`, opaque `revision`, display-only `displayPath`, optional opaque parent/drive IDs, `sortBy`, `descending`, complete `totalCount`, up to 100 entries, and optional continuation. Entries contain an opaque ID, name, `file|folder`, extension, optional non-negative size, modified time, and at most eight bounded attributes. Folders remain before files for every sort, and pages are slices of that complete host order.

`selectionAll: true` means the complete referenced directory revision minus exclusions, not the loaded pages. Immediately before resolving any entry or selection, including the destination panel for Paste, the host compares the current directory metadata with that revision. A mismatch returns `stale-panel`, queues nothing, performs no partial action, and mobile refreshes the panel. Copy and Move reject a destination that would overwrite a source with itself or place a selected folder inside itself. Expired sessions, consumed continuations, unavailable entries/targets/shares, invalid destinations/sorts/names, clipboard/Shell failures, Recycle Bin ineligibility, full queues, and unauthorized jobs return bounded codes and messages without paths.

Mutation creation returns `file.job.create.result` with a job immediately. At most 32 active or queued jobs are accepted host-wide so every originating device can inspect and control all of its outstanding work. `file.jobs.status` is owner-filtered, contains at most 32 snapshots, keeps active work first and the newest terminal history next, and is also coalesced after changes. A snapshot contains operation, queue state/position, completed/total items and bytes, optional bytes/second, ETA, current display name/message/conflict display name, and pause/resume/cancel availability. States are `queued`, `preparing`, `running`, `paused`, `needs-attention`, `canceling`, `completed`, `failed`, `canceled`, and `interrupted`.

One mutation runs host-wide. Reordering swaps only adjacent queued slots owned by the originating device, so another device's positions are not crossed. Permission revocation closes that device's sessions and immediately removes its queued work while canceling work already preparing or running. Disconnect does neither. The host durably journals active job identity, every partial destination before creating it, and any original destination temporarily moved aside during replacement; copying or replacement aborts before mutation if that recovery record cannot be saved. Restart recovery removes partial copies and restores an original destination that was not committed, retaining unavailable or locked artifacts for another startup attempt, then reports the job as `interrupted` without automatic resume. Each copied or uploaded entry commits from a temporary destination, and cancellation is checked again after conflict resolution and immediately before commit. Case-only Windows renames use a journaled temporary sibling. Move sources are removed only after their destination entries commit successfully; a skipped/failed subtree preserves its source.

### One-file transfer

One host-wide transfer may run beside Screen viewing or Phone webcam. The host owns `FileTransferCoordinator` and always creates a dedicated reliable ordered `voltura-file-transfer` WebRTC data channel; the device never offers. Transfer setup and results use the authenticated control session, while file bytes never use its command queue.

- The Files form of `file.transfer.start` binds `operationId`, `direction` (`download|upload`), opaque session/panel/revision, and a reconnect-key signature. Download includes one opaque `entryId`; upload includes one untrusted `fileName` and a safe non-negative integer `declaredSize`. Zero is valid. Its start transcript is `VolturaAir file-transfer:start:v1` followed on separate lines by client ID, pinned host public key, request ID, direction, session, panel, revision, entry ID, file name, and upload size.
- The exact download-only screenshot form instead carries `source: screen-capture`, `operationId`, the active `screenOperationId`, `displayId`, and `clientSignature`, with no Files session, panel, revision, entry, name, or size fields. Its dedicated transcript is `VolturaAir screen-capture-transfer:start:v1` followed on separate lines by client ID, pinned host public key, request ID, screen operation ID, and display ID. The host requires current Screen viewing and Transfer files permissions, verifies that the requesting device owns that active operation and display, and rechecks ownership and permissions after capture and during transfer. Hosts that support it advertise PNG, 33,177,600 pixels, 64 MiB, and current Transfer-files permission under `screenView.screenshot`; older hosts omit the object and clients hide the action.
- The host returns `file.transfer.start.result`, then `file.transfer.offer` with host-authoritative name/size, bounded SDP, pinned-host signature, and optional Relay ICE/usage fields. Its transcript is `VolturaAir file-transfer:offer:v1` plus client ID, host public key, request ID, transfer ID, direction, name, size, and SHA-256 SDP hash on separate lines. Negotiation expires after 20 seconds.
- The device returns `file.transfer.answer` with bounded SDP and a reconnect-key signature over `VolturaAir file-transfer:answer:v1`, client ID, host public key, request ID, transfer ID, direction, host-authoritative name and size, offer hash, and answer hash. `file.transfer.cancel` targets exactly one established `transferId` or pending start `requestId`, so Cancel remains authoritative before the host has issued a transfer ID. `file.transfer.answer.result`, `file.transfer.cancel.result`, coalesced `file.transfer.status`, and terminal `file.transfer.result` carry setup, cancellation, progress, and outcome only.

Binary records begin with version in the high nibble and kind in the low nibble (`1` data, `2` cumulative acknowledgement), followed by an unsigned 64-bit big-endian offset. Data payload is 1 through 65,536 bytes; acknowledgements have no payload. Senders retain at most 1 MiB unacknowledged and also respect data-channel buffered amount. Upload ACKs follow flushed writes; download ACKs follow completed OPFS writes. No hash, retransmission, encryption, or resume layer is added above WebRTC. Retry starts from zero.

Files downloads require browse and transfer permissions plus one current file revision. The host opens a stable source and supplies its name and length. A screen capture is prepared once in bounded host memory outside the live-capture lock after its native pixel copy, excludes the cursor, and is supplied as a generated source to the same coordinator; it creates no PC-side file. The browser checks estimated quota when available and writes an OPFS partial before exposing a fresh Save/Share activation. Canceling the native share sheet keeps that temporary file ready for retry while its owning view remains open; successful share handoff, explicit discard, fallback-download start, owner exit/start, reload, disconnect, permission loss, cancellation, or failure removes it. Uploads additionally require change permission. The host validates the Windows name, current destination revision, and available volume space before admission. An invalid name requires a replacement; conflicts use Replace, host-generated Keep both, or Cancel. Upload is admitted only by `file.transfer.start`, appears as a non-pausable existing mutation job, and queues behind other mutations. `file.job.create` does not accept `upload`.

The host journals an upload partial before creating it, excludes every journal-owned partial or backup from all client directory revisions, ACKs only flushed bytes, and revalidates the captured destination directory before commit. A later panel refresh or navigation cannot redirect or cancel that upload. The original remains preserved until Replace commits or rolls back. Explicit cancel, 60 seconds without committed progress, control/data-channel loss, disconnect, unpair, owner exit, permission loss, and host shutdown cancel and clean up. Direct transfer duration is not capped and bypasses Relay admission. Relay requests sign the exact purpose `file-transfer`, receive a 60-minute credential, and stop at expiry; media credentials remain 15 minutes. For a screenshot using the official Voltura Cloud Relay, the host rejects before peer creation when aggregate usage is already at the provider cutoff or when `usage + (3 x PNG bytes) + 1 MiB` reaches it, and immediately disposes the PNG. Missing or unavailable official usage fails closed. Custom Relay has no Voltura cutoff. Aggregate Cloudflare analytics may lag and concurrent sessions are not reserved, so this is conservative admission rather than an exact one-byte reservation. Other file transfer bytes count toward the existing TURN warning and cutoff, but screen quality throttling is not applied to file bytes.

Every file control message remains within the existing 64 KiB frame limit. Paths, filenames, clipboard lists, conflict names, temporary names, tokens, keys, proofs, and file contents are excluded from application logs.

### Open applications

Apps uses exact authenticated JSON control messages. `apps.list` has only `type`
and `operationId`. `apps.activate` and `apps.close` additionally contain one
32-character lowercase hexadecimal `revision` and `windowId`.
`apps.preview.answer` has `operationId`, `offerOperationId`, `previewId`, bounded
`answerSdp`, and `clientSignature`; `apps.preview.stop` has `operationId` and
`previewId`. Unknown fields, malformed IDs, and oversized values are rejected.

`apps.list.result` contains `operationId`, `succeeded`, bounded `code` and
`message`, and `windows`. Success additionally has a fresh opaque `revision` and
at most 48 exact window objects: `windowId`, bounded `title`, bounded
`applicationName`, and Boolean `active`, `minimized`, `maximizeSupported`, and
`previewSupported`. Failure omits `revision` and has an empty list. Handles,
process/session IDs, executable paths, command lines, arguments, icons, desktop
IDs, and capture details are never sent. Each refresh replaces the map; actions
require the same authenticated socket, current revision, opaque ID, effective
**Control open applications** permission, and successful native revalidation.
Activation restores or suitably maximizes before requesting focus. Close posts
the normal Windows close message and does not bypass application prompts.

Preview signaling is host-offered and uses `apps.preview.offer`,
`apps.preview.answer.result`, and `apps.preview.ended`. The signed offer transcript
is `VolturaAir apps-preview:offer:v1`, client ID, pinned host public key, list
operation ID, preview ID, and unpadded base64url SHA-256 offer hash on separate
lines. The answer transcript is `VolturaAir apps-preview:answer:v1` followed by
those identity values, offer operation ID, answer operation ID, preview ID,
offer hash, and answer hash. Relay offers may include bounded TURN servers and
expiry, use the existing file-transfer Relay purpose/quota cutoff, and require
relay-only candidates. The peer is a separate reliable ordered
`voltura-apps-preview` data-channel-only connection; it shares lower-level
ICE/TURN and TLS bridge code but no Files session, transfer coordinator, or
Screen/Phone lifecycle.

Binary Apps preview records place version `1` in byte zero's high nibble. A
request (`kind 1`) is exactly the discriminator, 32 ASCII revision bytes, a
count from 1 through 3, and that many 32-byte opaque IDs. The host accepts only
the current revision and captures requested windows sequentially. A header
(`kind 2`) is the discriminator, window ID, availability byte, unsigned 16-bit
big-endian width and height, unsigned 32-bit big-endian encoded length, and MIME
code `1` for JPEG. Available images are at most 1024 by 640 and 1.5 MiB; an
unavailable header has zero dimensions and length. Data (`kind 3`) is the
discriminator, window ID, unsigned 32-bit big-endian offset, and 1–49,152 bytes.
Offsets are exact and sequential. Invalid records close the preview peer.

Listing occurs once on Apps entry and once after explicit refresh, activate,
close, or successful approved-app launch; there is no polling or proactive
push. Preview requests cover only the centered card and immediate neighbors.
The host performs no discovery or capture and owns no Apps preview peer while
the tool is closed. IDs, maps, captures, encoded bytes, browser assembly buffers,
and object URLs are transient and excluded from persistence, logs, and telemetry.

### Interactive Terminal

Terminal setup and lifecycle use the authenticated JSON control connection. `terminal.start` has exact fields `type`, `operationId`, `columns`, `rows`, and `clientSignature`. `terminal.attach` additionally binds exact `terminalId` and non-negative safe `acknowledgedOffset`. `terminal.answer` has `operationId`, `offerOperationId`, `terminalId`, bounded `answerSdp`, and `clientSignature`; `terminal.stop` has only `operationId` and `terminalId`. Columns are 10–500 and rows 5–300. Results are `terminal.start.result`, `terminal.attach.result`, `terminal.answer.result`, and `terminal.stop.result`; the host emits `terminal.offer`, capability/status updates, and `terminal.ended`.

Start and attach signatures use `VolturaAir terminal:start:v1` and `VolturaAir terminal:attach:v1` transcripts followed by the client ID, pinned host public key, operation/session values, dimensions, and acknowledged offset in the documented message order. Offers bind those fields plus the SHA-256/base64url SDP hash under `VolturaAir terminal:offer:v1`; answers bind client ID, pinned host key, start/attach operation ID, answer operation ID, terminal ID, offer hash, and answer hash under `VolturaAir terminal:answer:v1`. Operation replay, invalid shape/SDP/signature, and cross-device ownership fail closed.

The host creates one reliable ordered `voltura-terminal` DataChannel. A transient ICE `disconnected` state is allowed to recover on that peer; `failed`, `closed`, DataChannel close, or record failure detaches it. Binary byte zero contains version `1` in the high nibble and kind in the low nibble: `1` input, `2` output, `3` cumulative output acknowledgement, `4` resize. Input/output records carry an unsigned 64-bit big-endian offset followed by 1–16,384 bytes (input offset is zero). Acknowledgement is exactly nine bytes. Resize is exactly five bytes: header plus unsigned 16-bit big-endian columns and rows. Invalid UTF-8 is terminal data, not JSON; xterm consumes the bytes without command-path decoding.

Queued input is at most 256 KiB. Output offsets are monotonic; the host retains exactly the unacknowledged suffix up to 1 MiB and stops reading the ConPTY pipe at the bound. The peer also stops accepting sends at 1 MiB WebRTC buffered amount. Reconnect requires the same authenticated device, terminal ID, fresh signed attach, and the host's exact last acknowledged boundary within 15 minutes. Relay uses signed purpose `terminal`, 60-minute credentials, and a fresh attach/offer before expiry without restarting PowerShell. No command, output, current directory, or environment value enters logs, telemetry, JSON status, or persistence.

### AI Assistant

Authenticated status may advertise `aiAssistant` with exact Boolean fields
`enabled`, `available`, `permissionGranted`, `canUse`, `requiresRepair`,
`active`, `ownedByClient`, and `working`, plus optional bounded `failureCode`.
`permissionGranted` is the My device profile grant. `canUse` additionally
requires a current host identity plus available bundled knowledge and an
installed Codex command-line component. Older hosts omit the capability and
mobile hides the tool.

Opening the mobile Menu uses the existing `status.get` request. Capability
evaluation checks the local Codex installation and bundled knowledge only; it
does not start Codex, inspect its account, or add background polling.

`ai.assistant.open` has exact fields `type`, `operationId`, and
`clientSignature`. `ai.assistant.ask` adds a non-empty `question` of at most
16,384 UTF-16 code units. `ai.assistant.reset` has the open shape;
`ai.assistant.close` has only `type` and `operationId`. Open and reset signatures
bind `VolturaAir ai-assistant:open:v1` or
`VolturaAir ai-assistant:reset:v1`, client ID, pinned host public key, and
operation ID. Ask binds `VolturaAir ai-assistant:ask:v1`, those same identity
values, and the SHA-256/base64url hash of the trimmed question. Operation IDs
are replay-protected and another paired device receives a busy result.

Results use the corresponding `.result` type with exact `operationId`, Boolean
`succeeded`, nullable bounded `code`, and bounded `message`. Snapshots are
bracketed by `ai.assistant.snapshot.start` and
`ai.assistant.snapshot.complete`. Each `ai.assistant.message` contains a
positive monotonic `sequence`, host-derived 64-character hexadecimal
`messageId` (Codex item identifiers never cross the wire), zero-based
`chunkIndex`, Boolean `finalChunk`, `sender` (`user` or `assistant`), and up to
4,096 UTF-16 code units of text. Complete messages are at most 32,768 code
units; snapshots and the live mobile transcript retain at most the newest 32
user/assistant items from a bounded page of the newest 16 turns. Resume and metadata reads
exclude full thread hydration; the host never requests the complete persistent
history before applying that page bound. Surrogate pairs are
never split between chunks. `ai.assistant.state` is `ready`, `working`, or
`failed`; only authoritative Codex turn state drives `working`. App-server stdio
records are capped at 8 MiB, pre-confirmation notifications and outbound actions
use bounded 64-entry queues, and overflow fails the Assistant session closed.
Because a timed-out `turn/start` may already have been accepted by Codex, an
uncertain result also closes the app-server session before another question can
be accepted. Disconnect, revocation, process exit, close, and host shutdown
dispose the child process and emit no prompt or answer content to logs or
telemetry.

## Presentation

Authenticated status advertises `presentation`. The resolved device-profile
Presentation permission controls
`canControl`, PowerPoint detail, commands, saved-file launch, session tracking,
and report saves. Older hosts may omit the optional capability; mobile then
hides Presentation entry points. Commands are acknowledged; mobile allows one
ordinary command in flight and clears it on disconnect. Idempotent pointer
cleanup may bypass unrelated pending work.

```json
{
  "type": "presentation.command",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "goto",
  "runtimePresentationId": "76ef027fb28347f785537769592f2976",
  "slideNumber": 12
}
```

Targets: `powerpoint`, `google-slides`, `pdf`. Actions: `next`, `previous`,
`start`, `start-current`, `first`, `last`, `goto`, `end`, `black`, `white`,
`pause`, `pointer`, `activate`. `pause` and `pointer` require Boolean `enabled`; `goto`
requires `slideNumber` from 1 through 1,000. PowerPoint accepts an optional
opaque `runtimePresentationId`; other targets reject it and all PowerPoint-only
actions/fields.

PowerPoint actions are admitted through a bounded, serialized STA automation
owner that attaches only to an existing PowerPoint process. Once a COM mutation
has started, the host waits up to five seconds for its authoritative result.
After that response bound it reports `powerpoint-busy`, keeps the automation
gate owned until COM actually returns, and rejects later automation commands as
busy. This frees socket health and cleanup traffic without permitting duplicate
late side effects. Pointer-off cleanup is coalesced and queued behind any
in-flight mutation, so a late Arrow change is followed by AutoArrow restoration.
A sole open presentation is selected automatically;
multiple presentations require a runtime ID.
PowerPoint never uses global input. Ordinary discovery and commands never
launch a file as fallback. Explicit break recovery may reopen only the exact
canonical file already stored in the host-owned session draft; that path never
appears on the wire. Google Slides and PDF/browser retain reviewed
Right/Left/Escape shortcuts; Google Slides also retains `B`.

Unavailable combinations return `unsupported-action` without input. The host
does not infer focused-app state for Google Slides or PDF/browser. While
PowerPoint reports `presenting`, `black` and `white` toggle writable PowerPoint
slideshow state and restore its captured prior state. While the selected
presentation reports `ready`, `currentSlideIndex` is the current editor slide
when PowerPoint exposes one. `next` and `previous` then start from that editor
slide before navigating once; they fail without starting when the editor slide
is unavailable. The same black/white actions toggle Voltura Air's black or
white full-display overlay; keyboard, pointer, touch, stylus, or remote input
may dismiss that overlay. Starting the slideshow or navigating directly to a
numbered slide dismisses it first. Presentation `black` remains distinct from
`system.power` `blackoutDisplay` on the wire.
While that Ready-state overlay is active, the selected presentation's
`slideShowState` is `black` or `white` in command results and status updates;
after any dismissal it returns to PowerPoint's authoritative editor state.

Enabled capability:

```json
{
  "presentation": {
    "canControl": true,
    "canSaveReports": true,
    "laserPointerActive": false,
    "laserPointerColor": null,
    "laserPointerDefaultColor": "red",
    "powerPoint": {
      "state": "ready",
      "foregroundActivationSupported": true,
      "presentations": [
        {
          "runtimePresentationId": "76ef027fb28347f785537769592f2976",
          "name": "Quarterly update.pptx",
          "state": "presenting",
          "slideCount": 24,
          "currentSlideIndex": 12,
          "currentShowPosition": 12,
          "slideShowState": "running"
        }
      ],
      "session": {
        "state": "tracking",
        "runtimePresentationId": "76ef027fb28347f785537769592f2976",
        "presentationName": "Quarterly update.pptx",
        "ownerDeviceName": "Presenter phone",
        "isOwner": true,
        "startedAt": "2026-07-24T09:00:00.000+02:00",
        "elapsedSeconds": 752,
        "breakActive": false,
        "breakElapsedSeconds": 0,
        "currentSlideIndex": 12,
        "slideCount": 24,
        "slideShowState": "running"
      }
    }
  }
}
```

Values reflect effective device permission; laser state is host-authoritative.
Session `ownerDeviceName` and per-connection `isOwner` describe the device that
most recently started or took over the report; they are informational and do
not authorize session actions.
`laserPointerColor` is the concrete active `red`, `green`, or `blue`, or null
when inactive. `laserPointerDefaultColor` is the current Preferences color.
Custom screen controls resolve `default` against that value and use the same
status broadcast as Presentation; there is no separate laser-status message.
Mobile sends `activate` when entering PowerPoint mode only when
`foregroundActivationSupported` is true. Older hosts omit the field, so newer
clients retain their previous no-request behavior against them.
PowerPoint laser activation first verifies the running runtime presentation,
applies Voltura Air's custom cursor, and attempts to set PowerPoint's pointer to
visible Arrow. The native adjustment and AutoArrow restoration are best-effort
and do not turn a successful custom-laser command into a failure. A non-owner
cannot disable or steal another owner's laser through ordinary pointer commands.
Explicitly starting a presentation performs host-owned laser cleanup before the
takeover. Owner
departure/disconnect, slideshow closure, permission/gate revocation, and
shutdown perform mandatory cleanup.

```json
{
  "type": "presentation.command.result",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "next",
  "succeeded": true,
  "message": "Next slide shown.",
  "laserPointerActive": false,
  "runtimePresentationId": "76ef027fb28347f785537769592f2976",
  "presentation": {
    "runtimePresentationId": "76ef027fb28347f785537769592f2976",
    "name": "Quarterly update.pptx",
    "state": "presenting",
    "slideCount": 24,
    "currentSlideIndex": 13,
    "currentShowPosition": 13,
    "slideShowState": "running"
  }
}
```

Codes: `feature-disabled`, `permission-denied`, `unsupported-action`,
`host-ui-blocked`, `input-failed`, `pointer-failed`, `pointer-owner-active`,
`powerpoint-unavailable`, `powerpoint-busy`, `powerpoint-selection-required`,
`powerpoint-target-stale`, `powerpoint-not-presenting`,
`powerpoint-current-slide-unavailable`, `powerpoint-invalid-slide`,
`powerpoint-invalid-state`, and
`powerpoint-automation-failed`, plus `presentation-blank-failed` when the
Ready-state Voltura overlay cannot be shown; mobile may add
`VAIR-PRESENTATION-RESPONSE-TIMEOUT`. Because started Office automation must
finish before the host can report its authoritative state, mobile allows a
longer acknowledgement window for presentation commands than for ordinary
control messages. Expected failures keep the socket open.
PowerPoint success includes a post-command snapshot; shortcut-target success
means Windows accepted input, not that slides changed.

Authenticated clients may request event-independent discovery with
`presentation.powerpoint.refresh` plus an operation ID. Its result contains
`succeeded`, optional `code`, `message`, discovery `state`, and bounded
`presentations`. Mobile correlates the operation ID and may report
`VAIR-POWERPOINT-REFRESH-RESPONSE-TIMEOUT` locally when no matching result
arrives; late and unrelated results do not complete another refresh.

When available, `powerPoint.availablePresentations` contains at most 100
host-derived saved-file candidates with opaque `presentationId`, bounded
display `title`, and `fileName`; canonical paths never cross the wire. The host
filters missing files, deduplicates Windows paths case-insensitively, prefers
the most recently modified report, and omits a saved candidate when that path
is already open.

An authorized explicit saved launch uses:

```json
{
  "type": "presentation.powerpoint.launch",
  "operationId": "launch-1",
  "presentationId": "report-opaque-1"
}
```

The host re-resolves the opaque ID, revalidates the file, automatically saves a
different active session, performs host-owned laser cleanup, opens the exact
path in PowerPoint, waits for automation discovery, then starts the slideshow
and host-owned session.
Results use `presentation.powerpoint.launch.result` with the correlated IDs,
`succeeded`, optional `code`, `message`, and on success the authoritative
`runtimePresentationId` and `presentation`. Launch authorization uses effective
Presentation control permission; the separate generic PowerPoint start button
continues to use application-launch permission. Expected codes include
`powerpoint-source-missing`, `powerpoint-open-failed`,
`powerpoint-open-timeout`, `powerpoint-busy`, `session-save-failed`, and
`session-persistence-failed`.

### Host-owned PowerPoint session

Starting a slideshow through `presentation.command` starts tracking
automatically. An already-running show uses:

```json
{
  "type": "presentation.session",
  "operationId": "session-start-1",
  "action": "start",
  "runtimePresentationId": "76ef027fb28347f785537769592f2976"
}
```

`break` requires Boolean `enabled`. `save` and `discard` accept no optional
fields. Any device with effective Presentation permission may manage the active
or paused host-owned session. Explicitly starting the same presentation transfers control
while preserving the report; starting a different presentation automatically
saves the prior report before PowerPoint is changed. The trusted local
Presentations page can also complete a pending review. Results use
`presentation.session.result` with operation ID, action, succeeded, optional
code, and message. Mobile correlates one session mutation at a time and may
report `VAIR-PRESENTATION-SESSION-RESPONSE-TIMEOUT` locally when no matching
result arrives; late and unrelated results do not complete another mutation.
While completion is in flight, competing session mutations return
`session-saving` or `session-busy`; break mutations during resume return
`session-resuming`. A session that has reached the bounded break count returns
`session-break-limit`. Unexpected report-store failures return
`session-save-failed`. Draft write/delete failures return
`session-persistence-failed`; the socket remains connected and the in-memory
session is retained or rolled back to its last consistent state.

The host uses its monotonic clock for live durations, records bounded ordered
slide visits, and derives per-slide totals. PowerPoint lifecycle/navigation
events update the session; identical automation events and post-command
snapshots do not create duplicate visits. Manual breaks are independent of
black, white, and paused slideshow states. The draft is replaced atomically at
session start and meaningful transitions. Slideshow exit or host restart leaves
the wire-compatible `pending-review` state, presented to the user as a paused
session. When the same presentation is Ready, its known editor slide updates
the paused session's current position without rewriting completed visits.
Continue presentation is the primary mobile action and starts from that editor
slide; Save/Discard remain available as secondary actions. Starting the same
runtime presentation or exact canonical host file resumes that session
automatically with the same report and visit timeline, transfers control to the
requesting device, and excludes time while the slideshow is closed. Starting a
different presentation first saves the previous report and then creates a new
host-owned session.

During a manual break, the host may show a local blackout with the authoritative
break duration. Input may dismiss that overlay without ending the break. Resume
removes it, restores the tracked slideshow to focus, and may reopen the exact
host-side file and restore the last slide before returning a failure. Local file
paths never appear in protocol messages.

### Report save

`canSaveReports` is effective Presentation permission. The legacy mobile-owned
save path remains for Google Slides, PDF/browser, and older clients. Host
derives device key/name from the authenticated connection; payload cannot
supply them. Host-owned PowerPoint saves additionally persist an ordered visit
timeline; older report JSON without visits remains readable.

```json
{
  "type": "presentation.report.save",
  "operationId": "save-820c1314-d8a1-499d-a969",
  "reportId": "report-820c1314-d8a1-499d-a969",
  "target": "powerpoint",
  "startedAt": "2026-07-23T08:00:00.000+02:00",
  "endedAt": "2026-07-23T09:07:07.000+02:00",
  "utcOffsetMinutes": 120,
  "plannedDurationSeconds": 3600,
  "presentationDurationSeconds": 3422,
  "endedDuringBreak": false,
  "breaks": [
    {
      "breakNumber": 1,
      "presentationElapsedSeconds": 1140,
      "breakDurationSeconds": 420,
      "startedAt": "2026-07-23T08:19:00.000+02:00",
      "endedAt": "2026-07-23T08:26:00.000+02:00",
      "sessionSlideMinimum": 1,
      "sessionSlideMaximum": 9,
      "slideNumberAtStart": 9,
      "slideNumberAtEnd": 9
    }
  ],
  "slides": [
    { "slideNumber": 1, "durationSeconds": 130 },
    { "slideNumber": 2, "durationSeconds": 92 }
  ]
}
```

- Operation/report IDs: 1–64 ASCII letters, digits, hyphens.
- Target: Presentation allowlist. Dates: valid offsets.
- `utcOffsetMinutes`: −840 through +840.
- Chronology: monotonic; breaks inside report bounds.
- Wall-clock span/durations: finite, non-negative, maximum seven days.
- Breaks: consecutive from 1, nondecreasing presentation checkpoints, maximum 100.
- Optional slide numbers/ranges and unique slide entries: 1–1,000; maximum
  1,000 entries.
- Unknown optionals are omitted. `null`, duplicate, or undeclared nested fields
  are invalid.
- `endedDuringBreak` is required. If true, final break ends at report end and
  its checkpoint equals final presentation duration.
- The 64 KiB transport limit applies.

The same operation/report pair is idempotent. Reusing a report ID with another
operation returns `report-conflict`. Archive maximum: 1,000.

```json
{
  "type": "presentation.report.save.result",
  "operationId": "save-820c1314-d8a1-499d-a969",
  "reportId": "report-820c1314-d8a1-499d-a969",
  "succeeded": true,
  "message": "Presentation data saved on the PC."
}
```

Codes: `feature-disabled`, `permission-denied`, `invalid-report`,
`device-revoked`, `report-conflict`, `archive-full`, `storage-failed`. Invalid
bounded report semantics return `invalid-report` without closing. Invalid
envelope/correlation is a protocol violation. Failure/timeout retains the
snapshot.

## Power and session

`gestureDebug` defaults false. `inputAck` signals ack/error support and
`inputContextV1` signals the optional closed input-source field. Clients
must not expose/send operations whose capability is absent or false.

```json
{ "type": "system.sleep" }
```

Ignored when **Allow PC sleep** is off.

```json
{
  "power": {
    "lock": true,
    "lockAvailability": "notExplicitlyDisabled",
    "blackoutDisplay": true,
    "displayOff": false,
    "screenSaver": true,
    "screenSaverAvailable": false,
    "signOut": false,
    "restart": false,
    "shutdown": false
  }
}
```

`power` remains present when all actions are false. Booleans are effective
permissions. `lockAvailability`: `notExplicitlyDisabled`, `disabledByPolicy`,
or `unavailable`; missing means `notExplicitlyDisabled`, not proven available.
`screenSaverAvailable` requires Windows screen saving and a configured `.scr`.

```json
{ "type": "system.power", "operationId": "power-lock-7f31", "action": "lock" }
```

Actions: `lock`, `blackoutDisplay`, `displayOff`, `screenSaver`, `signOut`,
`restart`, `shutdown`. Lock, Blackout, and available screen saver default
allowed; display off and session-ending actions default blocked.

Blackout covers all monitors without powering them off and closes on local or
later remote input. Screen saver returns `VAIR-POWER-UNAVAILABLE` when not
configured. Display off may suspend the host/network; acceptance does not imply
reachability, remote wake, or sign-out. Session-ending actions accept no client
path/arguments/command.

```json
{
  "type": "system.power.result",
  "operationId": "power-lock-7f31",
  "action": "lock",
  "succeeded": false,
  "code": "VAIR-POWER-LOCK-DISABLED",
  "message": "Windows locking is disabled. Enable it in the Voltura Air host settings."
}
```

Success means Windows accepted/started the action, not that it completed.
Remote-input denial returns this shape with `VAIR-INPUT-DENIED`.
`operationId`: client-generated 1–64 ASCII alphanumeric/hyphen, echoed exactly;
missing/malformed violates policy.

Codes: `VAIR-POWER-DENIED`, `VAIR-POWER-UNSUPPORTED`,
`VAIR-POWER-UNAVAILABLE`, `VAIR-POWER-LOCK-DISABLED`,
`VAIR-POWER-LOCK-UNAVAILABLE`, `VAIR-POWER-EXECUTION-FAILED`. Action failures
keep the socket open.

## Keep awake

State is reported even when control is blocked:

```json
{
  "awake": {
    "canControl": false,
    "active": true,
    "mode": "timed",
    "expiresAt": "2026-07-13T19:30:00.0000000Z"
  }
}
```

`mode`: `off`, `indefinite`, `timed`, `expiration`. `expiresAt`: UTC ISO-8601,
required for timed/expiration and omitted otherwise. State changes push
`status`.

```json
{ "type": "awake.set", "operationId": "awake-enable-83c2", "enabled": true }
```

True selects indefinite; false selects Off. The message cannot change
**Keep screen on**. Effective Awake permission is required.

```json
{
  "type": "awake.result",
  "operationId": "awake-enable-83c2",
  "enabled": true,
  "succeeded": false,
  "code": "VAIR-AWAKE-DENIED",
  "message": "Keep awake control is disabled by the PC host."
}
```

Power operation-ID grammar/echo rules apply. Codes: `VAIR-AWAKE-DENIED`,
`VAIR-AWAKE-EXECUTION-FAILED`. Action failures keep the socket open; malformed
`enabled` violates protocol. Awake does not edit power plans, require elevation,
or override manual sleep, lid close, or lock-screen behavior.

## Audio

Effective volume permission is required.

```json
{ "type": "audio.get" }
{ "type": "audio.state", "volume": 72, "muted": false }
{ "type": "audio.mute.toggle" }
{ "type": "audio.volume.set", "volume": 45 }
```

`audio.state` follows `audio.get` and accepted audio commands.
`audio.volume.set` clamps to 0–100 and unmutes.
