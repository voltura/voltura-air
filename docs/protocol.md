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

An unauthenticated relay host candidate never owns the route. It has ten
seconds to prove the routing key and is replaced by a newer candidate; only the
authenticated socket blocks another host. A device Connected envelope carries
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
  idle timeout 2 minutes.
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

Wire changes update the test server-frame catalog and follow
[risk-based validation](setup.md#validation-by-change).

## Pairing link

The host creates an absolute HTTP/HTTPS `/pair` URL containing one short-lived
bootstrap secret and no fragment. It does not include a host identity key,
fingerprint, reconnect key, or second identifier; `/` imports no pairing
credential.

| Parameter | Contract |
| --- | --- |
| `t` | Required 32-character URL-safe Base64 short-lived token. |
| `v` | Required semver metadata; validated, but never authentication, compatibility enforcement, or cache busting. |
| `h` | Optional WebSocket host origin or port; a port resolves against the page host. Routing only, never authentication. |
| `d` | Optional non-secret client ID added by mobile. |
| `n` | Optional non-secret device name added by mobile. |

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
    "remoteInput": true,
    "gestureDebug": false,
    "inputAck": true,
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
- `textTransferTarget`: exactly `{ mode, displayName, available }`; mode is
  `focused`, `clipboard`, or `configured`. It excludes paths, process/window
  IDs, matching rules, and clipboard content.
- `pointerSpeed`: effective device speed. `customPointerEnabled`: host-wide.
  `showModeButtons` and `controlDepth`: effective per-device appearance values.
  `inputBlockedByElevation`: higher-integrity foreground block.
- `webClientBuildId`: the client bundle served by a Direct host, independent of
  `hostVersion`. It can refresh only a Direct host-served PWA. Relay opens the
  public hosted PWA, so a differing PC bundle ID never triggers a refresh.
- Developer mode adds `developerMode: true` and `developerSessionId`.
- `screenView` is always present for a supporting host so the tool remains
  discoverable. `enabled`, `permissionGranted`, and `requiresRepair` explain
  why `canView` is false.

Adapter metadata may reveal local hardware and appears only in explicit redacted
diagnostics.

Rejection:

```json
{ "type": "pair.rejected", "reason": "invalid-token" }
```

| `reason` | Meaning |
| --- | --- |
| `pair-first` | Non-pairing message before authentication. |
| `invalid-token` | No match with current/overlap token. |
| `expired-token` | Matching retained token expired. |
| `stale-token` | No active token state. |
| `device-revoked` | No device record for `clientId`. |
| `invalid-proof` | Signature failed for the session challenge/public key. |
| `rate-limited` | Too many failed unauthenticated attempts from the address. |
| `invalid-message` | Invalid pairing JSON shape. |

Mobile derives `VAIR-PAIR-*`; no diagnostic-code field is sent. Unknown reasons
remain diagnosable instead of exposing raw protocol text.

Authenticated utility messages:

```json
{ "type": "pair.disconnect" }
{ "type": "device.rename", "deviceName": "Joakim iPhone" }
{ "type": "pointer.speed.set", "pointerSpeed": 65 }
{ "type": "appearance.mode-buttons.set", "showModeButtons": false }
{ "type": "appearance.control-depth.set", "controlDepth": false }
{ "type": "custom.pointer.set", "enabled": true }
{ "type": "health.ping" }
{ "type": "health.pong" }
```

`deviceName` must contain non-whitespace text; mobile substitutes its default
before sending a blank edit. Pointer speed and appearance changes are sent only
from user action. Appearance changes set an override for the authenticated
device; the host Devices page can restore inheritance from the global default.
`health.pong` is liveness only; it contains no metadata/capability/audio state.
Any valid client message resets the receive timeout.

## Encrypted screen viewing

Screen viewing is video-only, one display and one viewer at a time, and capped
at 1920 x 1080 and 30 capture cycles per second. These bounded control messages
use the authenticated `/ws` session:

```json
{ "type": "screen.view.sources.get", "operationId": "screen-sources-1" }
{ "type": "screen.view.start", "operationId": "screen-start-1", "displayId": "display-1-1", "clientSignature": "base64url-p1363-signature" }
{ "type": "screen.view.answer", "operationId": "screen-start-1", "answerSdp": "bounded WebRTC answer SDP", "clientSignature": "base64url-p1363-signature" }
{ "type": "screen.view.source.set", "operationId": "screen-source-1", "displayId": "display-1-2" }
{ "type": "screen.view.stop", "operationId": "screen-stop-1" }
```

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
{ "type": "screen.view.ended", "reason": "host-stopped", "message": "The PC stopped screen viewing." }
{ "type": "screen.view.ended", "reason": "permission-revoked", "message": "The PC stopped screen viewing and disallowed this device." }
```

The client clears the video and disables stage input immediately. The two
listed reasons are the complete current contract; other reason values are
rejected rather than interpreted as a legacy variant.

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
hardware Media Foundation transform converts the frame to baseline H.264 at up
to 1920 x 1080 and 30 frames per second. The RTP sender supports sender reports,
NACK retransmission, receiver keyframe requests, and receiver bitrate estimates;
buffered media and event data have fixed upper bounds. Source switching resets
the duplication/encoder session and forces a keyframe. Permission revocation,
disconnect, lock/session loss, display removal, stop, or host shutdown releases
the peer, encoder, capture session, native resources, and any direct mouse
buttons held by that Screen session. Source switches, permission loss, and
native input failure also release held direct buttons. Pointer coordinates are
never logged.

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
12-column width, `content`/`fill` height and weight, zero-to-three button rows,
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
`trackpadFullscreenControl`; maximizing is local UI state and Restore returns
the section to its saved responsive position. Buttons contain only
visual/accessibility fields, row, repeat state, and resolved
availability/reason. A Laser pointer button additionally receives only
`laserPointerColor` (`default`, `red`, `green`, or `blue`); a missing field
identifies an ordinary button. Buttons for protected host actions additionally receive a
host-derived `confirmation` value (`confirm` or `hold`) and bounded warning
text. Screen JSON cannot select or weaken that safety policy. Literal text,
shortcut payloads, URLs, executable details, known-app mappings, and host
action IDs are never sent.

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
{ "type": "pointer.move", "seq": 123, "dx": 12, "dy": -4 }
{ "type": "pointer.button", "seq": 124, "button": "left", "action": "click" }
{ "type": "pointer.button", "button": "left", "action": "down" }
{ "type": "pointer.button", "button": "left", "action": "up" }
{ "type": "pointer.wheel", "seq": 125, "dx": 0, "dy": -18 }
{ "type": "pointer.zoom", "seq": 126, "direction": "in" }
{ "type": "keyboard.text", "seq": 127, "text": "Hello" }
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
{ "type": "app.launch", "operationId": "550e8400-e29b-41d4-a716-446655440000", "actionId": "custom.1234" }
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
{ "canBrowse": false, "canModify": false, "hidesProtectedSystemItems": true, "maxPageSize": 100 }
```

The capability remains present when permission is denied so mobile can show recovery guidance. `canBrowse` reflects **Browse and open files**. `canModify` additionally requires **Change files**. Both permissions default off and resolve global plus per-device policy. `hidesProtectedSystemItems` reports the effective default-on global/per-device **Hide protected operating system files and folders** policy. When true, the host removes entries carrying both Windows Hidden and System attributes before it creates panel revisions, counts, pages, selections, or operation references.

Clients never send a path. The host issues opaque `sessionId`, drive/shortcut/entry IDs, panel `revision`, `continuation`, and `jobId` values. Each authenticated request has an `operationId` and only the fields listed here:

- `file.session.open`: opens/replaces the originating device's navigation session and returns `file.session.open.result` with drives, known-folder shortcuts, and left/right first pages.
- `file.page.get`: `sessionId`, `panel`, `revision`, `continuation`. A continuation is single-use, belongs to one panel revision, and returns at most 100 entries in `file.page.get.result`.
- `file.navigate`: `sessionId`, `panel`, `revision`, `targetId`; `file.refresh`: `sessionId`, `panel`; and `file.sort`: `sessionId`, `panel`, `sortBy` (`name|size|type|modified`), `descending`. Their matching `.result` contains one replacement page.
- `file.properties.get` and `file.open`: `sessionId`, `panel`, `revision`, `entryId`. Properties accepts an opaque listed entry reference or the reserved `current` location reference and returns bounded name/display path, kind, extension, optional size, timestamps, and attributes. Open accepts only a listed entry reference and delegates to the Windows Shell.
- `file.clipboard.set`: session/panel/revision plus `effect` (`copy|move`), `selectionAll`, at most 512 explicit `entryIds`, and at most 512 `excludedEntryIds`. The host writes a Shell file-drop list and preferred effect to the real Windows clipboard.
- `file.jobs.get` returns `file.jobs.status`. `file.job.create` adds session/panel/revision, `operation` (`copy|move|paste|rename|delete`), an optional bounded new name, and the selection fields. Direct Copy/Move also binds the destination panel and its rendered revision; the host validates both panel revisions together before queueing. Paste resolves a compatible Explorer/Windows file clipboard on the host; no clipboard paths cross the protocol.
- `file.job.control` carries `jobId` and `action` (`pause|resume|cancel|dismiss`). Dismiss removes only the originating device's terminal history entry; `file.job.reorder` carries `direction` (`up|down`); `file.job.conflict.resolve` carries `resolution` (`replace|skip|cancel`) and `applyToAll`.

A panel page contains `panel`, opaque `revision`, display-only `displayPath`, optional opaque parent/drive IDs, `sortBy`, `descending`, complete `totalCount`, up to 100 entries, and optional continuation. Entries contain an opaque ID, name, `file|folder`, extension, optional non-negative size, modified time, and at most eight bounded attributes. Folders remain before files for every sort, and pages are slices of that complete host order.

`selectionAll: true` means the complete referenced directory revision minus exclusions, not the loaded pages. Immediately before resolving any entry or selection, including the destination panel for Paste, the host compares the current directory metadata with that revision. A mismatch returns `stale-panel`, queues nothing, performs no partial action, and mobile refreshes the panel. Copy and Move reject a destination that would overwrite a source with itself or place a selected folder inside itself. Expired sessions, consumed continuations, unavailable entries/targets/shares, invalid destinations/sorts/names, clipboard/Shell failures, Recycle Bin ineligibility, full queues, and unauthorized jobs return bounded codes and messages without paths.

Mutation creation returns `file.job.create.result` with a job immediately. At most 32 active or queued jobs are accepted host-wide so every originating device can inspect and control all of its outstanding work. `file.jobs.status` is owner-filtered, contains at most 32 snapshots, keeps active work first and the newest terminal history next, and is also coalesced after changes. A snapshot contains operation, queue state/position, completed/total items and bytes, optional bytes/second, ETA, current display name/message/conflict display name, and pause/resume/cancel availability. States are `queued`, `preparing`, `running`, `paused`, `needs-attention`, `canceling`, `completed`, `failed`, `canceled`, and `interrupted`.

One mutation runs host-wide. Reordering swaps only adjacent queued slots owned by the originating device, so another device's positions are not crossed. Permission revocation closes that device's sessions and immediately removes its queued work while canceling work already preparing or running. Disconnect does neither. The host durably journals active job identity, every partial destination before creating it, and any original destination temporarily moved aside during replacement; copying or replacement aborts before mutation if that recovery record cannot be saved. Restart recovery removes partial copies and restores an original destination that was not committed, retaining unavailable or locked artifacts for another startup attempt, then reports the job as `interrupted` without automatic resume. Each copied entry commits from a temporary destination, and cancellation is checked again after conflict resolution and immediately before commit. Case-only Windows renames use a journaled temporary sibling. Move sources are removed only after their destination entries commit successfully; a skipped/failed subtree preserves its source.

Every file message remains within the existing 64 KiB frame limit. Paths, filenames, clipboard lists, conflict names, temporary names, tokens, keys, and proofs are excluded from application logs.

## Presentation

Authenticated status advertises `presentation`. Effective global and per-device
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
cannot disable or steal another owner's laser, and presentation switching is
blocked while it is active. Owner
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

The host re-resolves the opaque ID, revalidates the file, rejects active
session or laser ownership, opens the exact path in PowerPoint, waits for
automation discovery, then starts the slideshow and host-owned session.
Results use `presentation.powerpoint.launch.result` with the correlated IDs,
`succeeded`, optional `code`, `message`, and on success the authoritative
`runtimePresentationId` and `presentation`. Launch authorization uses effective
Presentation control permission; the separate generic PowerPoint start button
continues to use application-launch permission. Expected codes include
`powerpoint-source-missing`, `powerpoint-open-failed`,
`powerpoint-open-timeout`, `powerpoint-busy`, `session-active`, and
`pointer-owner-active`.

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
fields. Only the starting device owns these mobile actions; other authorized
devices may still navigate. The trusted local Presentations page can also
complete a pending review. Results use
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
automatically with the same report, visit timeline, and owner. Time while the
slideshow is closed is excluded. A different presentation returns
`session-active` and cannot inherit the paused session.

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
- Breaks: consecutive from 1, nondecreasing presentation checkpoints, maximum
  100.
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

`gestureDebug` defaults false. `inputAck` signals ack/error support. Clients
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
