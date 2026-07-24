# Protocol

JSON WebSocket contract at `/ws`. Product behavior belongs in
[features](features.md); routing in
[network selection](network-and-host-selection.md); connection UX in
[pairing feedback](pairing-feedback.md).

## Transport and JSON

- Allowed origins: missing, same-origin, configured development, loopback, and
  private LAN. Unrelated public origins are rejected before upgrade.
- Maximum 64 sessions; `pair.hello` deadline 10 seconds; authenticated receive
  idle timeout 2 minutes.
- Maximum text message 64 KiB across fragments. Oversize closes with 1009;
  binary is rejected.
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

The host creates an absolute HTTP/HTTPS `/pair` URL with no credentials or
fragment; `/` imports no pairing credential.

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

Tokens last five minutes. Connect rotates 15 seconds before expiry and retains
only the prior token for up to that 15-second overlap. Successful pairing
consumes both slots and creates a new visible token.

## Authentication

Every session starts with `pair.hello`. Fresh pairing generates P-256 keys:
mobile retains the private key; the host receives a Base64url uncompressed
public key. `clientId` is non-secret. A client without the registered private
key must pair again.

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone",
  "pairToken": "short-lived-token",
  "reconnectPublicKey": "base64url-uncompressed-p256-public-key"
}
```

A valid token/public key consumes both token slots. For an existing `clientId`,
it replaces the public key and revokes active sockets without adding another
device record.

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
    "clipboardRead": false
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
    "inputBlockedByElevation": false
  }
}
```

Fresh/reconnect acceptance has the same shape and never includes private keys,
reconnect credentials, challenges, proofs, or tokens.

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
  `inputBlockedByElevation`: higher-integrity foreground block.
- `webClientBuildId`: served client bundle, independent of `hostVersion`.
- Developer mode adds `developerMode: true` and `developerSessionId`.

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
{ "type": "custom.pointer.set", "enabled": true }
{ "type": "health.ping" }
{ "type": "health.pong" }
```

`deviceName` must contain non-whitespace text; mobile substitutes its default
before sending a blank edit. Pointer speed is sent only from user action.
`health.pong` is liveness only; it contains no metadata/capability/audio state.
Any valid client message resets the receive timeout.

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

Button actions are `click`, `down`, `up`; `click` sends press/release.
Zoom `in` means spread/pinch-out; `out` means pinch-in. Keyboard text cannot be
empty, but whitespace is valid. Single-letter virtual keys use
`keyboard.special`. `Undo` and `Redo` map to Ctrl+Z/Ctrl+Y.

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

## Presentation

The default-on alpha gate omits `presentation` and blocks commands/report saves
when explicitly off. Commands are acknowledged; mobile allows one ordinary
command in flight and clears it on disconnect. Idempotent pointer cleanup may
bypass unrelated pending work.

```json
{
  "type": "presentation.command",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "next"
}
```

Targets: `powerpoint`, `google-slides`, `pdf`. Actions: `next`, `previous`,
`start`, `end`, `black`, `pointer`. `pointer` requires Boolean `enabled`; other
actions forbid it.

| Target | Next | Previous | Start | End | Black |
| --- | --- | --- | --- | --- | --- |
| PowerPoint | Right | Left | F5 | Esc | B |
| Google Slides | Right | Left | unavailable | Esc | B |
| PDF/browser | Right | Left | unavailable | Esc | unavailable |

Unavailable combinations return `unsupported-action` without input. The host
does not infer focused-app state. Presentation `black` is distinct from
`system.power` `blackoutDisplay`.

Enabled capability:

```json
{
  "presentation": {
    "canControl": true,
    "canSaveReports": true,
    "laserPointerActive": false
  }
}
```

Values reflect effective device permission; laser state is host-authoritative.
A non-owner cannot disable another owner's laser. Owner departure/disconnect,
End, permission/gate revocation, and shutdown disable it; owner cleanup remains
accepted after availability revocation.

```json
{
  "type": "presentation.command.result",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "next",
  "succeeded": true,
  "message": "Next slide command sent.",
  "laserPointerActive": false
}
```

Codes: `feature-disabled`, `permission-denied`, `unsupported-action`,
`host-ui-blocked`, `input-failed`, `pointer-failed`; mobile may add
`VAIR-PRESENTATION-RESPONSE-TIMEOUT`. Expected failures keep the socket open.
Success means Windows accepted input, not that slides changed.

### Report save

`canSaveReports` is effective Presentation permission. Mobile freezes the local
snapshot until matching success. Host derives device key/name from the
authenticated connection; payload cannot supply them.

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
