# Protocol

Voltura Air uses JSON messages over a WebSocket connection at `/ws`. The host
accepts missing WebSocket `Origin` headers, same-origin requests, configured
development client origins, and loopback/private LAN origins. Clearly unrelated
public origins are rejected before the WebSocket is accepted.

Pairing links use query parameters:

- `t`: short-lived pairing token.
- `v`: mobile app version expected by the Windows host. The host includes this
  on QR links so browsers and installed PWAs can fetch the current app shell
  instead of reusing stale cached code.
- `h`: optional PC host hint for `/ws` traffic when the web app is served from
  a different origin than the Windows host. `h` can be a full origin such as
  `http://192.168.1.20:51395` or a port such as `51395`, which resolves against
  the current page host.
- `d`: non-secret client identifier used by browser and Home Screen launches.
- `n`: non-secret mobile device display name.

The mobile app removes `t` from the address after pairing. Non-secret
parameters can remain in the address.

## Host hints and manual PC profiles

The `h` host hint is connection routing metadata only. It is not a secret and
must not be treated as proof that a device is paired. A WebSocket session still
has to authenticate through `pair.hello` with either a valid `pairToken` or a
stored reconnect `secret`.

The mobile app can also accept a manually entered host from recovery UI or
Settings. Manual host input may be:

- an origin such as `http://192.168.1.50:51395`;
- an address and port such as `192.168.1.50:51395`;
- a full Voltura Air pairing link;
- a port number, which resolves against the current page host.

After valid manual host input, the mobile app creates or updates a saved PC
profile, selects it as the active PC, and attempts to connect without navigating
away from the app. If the manually entered host does not include a pairing token,
the connection can only complete when the browser already has a valid stored
secret for that PC. Otherwise the host rejects the request and the user must
scan a fresh QR code.

Manual host profiles are a recovery path for changed IP addresses, changed
automatic ports, guest Wi-Fi mistakes, stale QR pages, and firewall/network
troubleshooting. They do not bypass pairing.

## Pairing

The client must start every WebSocket session with `pair.hello`. `deviceName`
is the current display name for the mobile device. On a first-time pairing the
client also sends a short-lived single-use `pairToken`; on reconnect it sends
the stored `secret`. The Windows host stores only a hash of the reconnect
secret.

The mobile app stores a generated `clientId` in browser storage and also keeps
that same non-secret value in the page URL as `d`. This lets a browser-created
Home Screen bookmark reopen with the same logical client identity even if the
standalone web app starts with separate storage. The value is not treated as an
authentication secret; reconnect still requires the stored `secret`, and a
fresh storage container must pair once with a valid `pairToken` before it can
store its own secret.

When a valid `pairToken` is accepted for an already-known `clientId`, the host
rotates the secret, revokes existing active sockets for that client, and keeps one
paired-device record instead of adding a duplicate browser/Home Screen entry.

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone",
  "pairToken": "short-lived-token",
  "secret": "stored-secret-for-reconnect"
}
```

Successful response:

```json
{
  "type": "pair.accepted",
  "clientId": "browser-generated-id",
  "pcName": "WINDOWS-PC",
  "secret": "secret-to-store",
  "paired": true,
  "capabilities": {
    "gestureDebug": false,
    "inputAck": true,
    "sleep": true,
    "volume": true
  },
  "host": {
    "hostVersion": "0.1.0",
    "pcName": "WINDOWS-PC",
    "defaultRemoteMode": "standard",
    "selectedAdapterName": "Wi-Fi - Intel(R) Wi-Fi 6 AX200",
    "selectedIp": "192.168.1.50",
    "selectedPort": 51395,
    "webSocketUrl": "ws://192.168.1.50:51395/ws"
  }
}
```

Request current connection status after pairing has been accepted:

```json
{ "type": "status.get" }
```

Host response:

```json
{
  "type": "status",
  "connected": true,
  "message": "Connected",
  "pcName": "WINDOWS-PC",
  "capabilities": {
    "gestureDebug": false,
    "inputAck": true,
    "sleep": true,
    "volume": true
  },
  "host": {
    "hostVersion": "0.1.0",
    "pcName": "WINDOWS-PC",
    "defaultRemoteMode": "standard",
    "selectedAdapterName": "Wi-Fi - Intel(R) Wi-Fi 6 AX200",
    "selectedIp": "192.168.1.50",
    "selectedPort": 51395,
    "webSocketUrl": "ws://192.168.1.50:51395/ws"
  }
}
```

Host metadata is included after authentication in `pair.accepted` and `status`.
It is diagnostics metadata only. It is not a secret and must not be used as authentication state.
The adapter name can reveal local hardware/vendor details, so it should only be copied when the user explicitly chooses **Copy diagnostics**.
`defaultRemoteMode` is the host's advisory initial Remote mode for that PC (`standard`, `youtube`, or `kodi`). The mobile app uses it only when the current phone/browser has no saved Remote mode override for that PC.
When host developer mode is enabled in **Preferences** -> **Developer tools**, host metadata also includes `developerMode: true` and a `developerSessionId` for the current host run. The mobile app uses this to auto-refresh installed web app code during development even when the release version has not changed.

Rejected response:

```json
{
  "type": "pair.rejected",
  "reason": "invalid-token",
  "diagnosticCode": "VAIR-PAIR-INVALID-TOKEN"
}
```

Known pairing rejection reasons:

| Reason | Meaning |
| --- | --- |
| `pair-first` | The client sent a non-pairing message before authentication. |
| `missing-token` | No `pairToken` or valid reconnect `secret` was supplied. |
| `invalid-token` | A supplied pairing token was not accepted by the host. |
| `expired-token` | The active pairing token expired before use. |
| `stale-token` | The token was already consumed or replaced by a newer QR code. |
| `device-revoked` / `secret-revoked` | The stored device credential is no longer valid. |
| `protocol-version-mismatch` | The client and host pairing protocol versions are incompatible. |
| `rate-limited` | Too many failed unauthenticated pairing attempts were made from the same remote address. |
| `invalid-message` | The pairing request was not valid JSON protocol shape. |

The mobile client must treat unknown reasons as diagnosable pairing failures and
show a diagnostic code instead of raw protocol text.

After authentication, the host accepts only known message types with the expected
JSON shape. Unknown or malformed authenticated messages are closed with a
WebSocket policy violation and are not dispatched as input.

Forget this device on the PC after pairing has been accepted:

```json
{ "type": "pair.disconnect" }
```

Rename this device on the PC after pairing has been accepted. The host updates
the paired device record, stores the rename timestamp, refreshes connected UI
such as the tray and Settings Devices page, and uses that timestamp for
latest-activity ordering.

```json
{ "type": "device.rename", "deviceName": "Joakim iPhone" }
```

The host trims `deviceName`; if the client sends a blank name, the host stores
`Mobile device`.

Lightweight connection health check after pairing has been accepted:

```json
{ "type": "health.ping" }
```

Host response:

```json
{ "type": "health.pong" }
```

`health.pong` is only a liveness signal. It does not carry host metadata,
capabilities, or audio state. The mobile app keeps faster checks while input is
active, slows checks after the foreground app is idle, and closes the WebSocket
while the browser page or installed app is backgrounded.

## Input Events

Pointer movement:

```json
{ "type": "pointer.move", "seq": 123, "dx": 12, "dy": -4 }
```

Mouse button. `action: "click"` sends a complete press/release click. `down`
and `up` are used when the mobile app needs to hold a button while pointer
movement continues, such as dragging or resizing a window.

```json
{ "type": "pointer.button", "seq": 124, "button": "left", "action": "click" }
```

```json
{ "type": "pointer.button", "button": "left", "action": "down" }
```

```json
{ "type": "pointer.button", "button": "left", "action": "up" }
```

Wheel scroll:

```json
{ "type": "pointer.wheel", "seq": 125, "dx": 0, "dy": -18 }
```

Zoom gesture. `direction: "in"` is a two-finger spread/pinch-out and zooms in. `direction: "out"` is a two-finger pinch-in and zooms out.

```json
{ "type": "pointer.zoom", "seq": 126, "direction": "in" }
```

The mobile app has a **Pinch zoom** trackpad setting. It controls whether
pinch/spread gestures emit `pointer.zoom` messages.

Text input:

```json
{ "type": "keyboard.text", "seq": 127, "text": "Hello" }
```

Special key:

```json
{ "type": "keyboard.special", "seq": 128, "key": "Enter", "modifiers": ["Control"] }
```

Single-letter shortcuts can be sent as special keys when an app needs a real
virtual-key press instead of Unicode text input. For example, the mobile live
keyboard maps a one-character `f` insertion to:

```json
{ "type": "keyboard.special", "key": "F" }
```

Undo and redo shortcut aliases:

```json
{ "type": "keyboard.special", "key": "Undo" }
```

```json
{ "type": "keyboard.special", "key": "Redo" }
```

The Windows host translates `Undo` to `Ctrl+Z` and `Redo` to `Ctrl+Y`.

Input delivery acknowledgement:

When `capabilities.inputAck` is `true`, the mobile client adds a positive `seq`
number to pointer and keyboard input messages. After the host dispatches the
input to Windows, it sends `input.ack` for the same sequence.

```json
{ "type": "input.ack", "seq": 123 }
```

If the host accepts the WebSocket message but cannot dispatch the input to
Windows, it sends `input.error`, reports a disconnected status, and closes the
socket so the mobile client can show unavailable/retrying instead of dead
controls.

```json
{
  "type": "input.error",
  "seq": 123,
  "code": "VAIR-INPUT-DISPATCH-FAILED",
  "message": "Windows did not accept input events."
}
```

The mobile client treats missing acknowledgements for recent input as an
unhealthy connection and reconnects. Heartbeat success alone is not enough to
keep the UI in the paired state when input delivery is not being confirmed.

## System

The host reports optional PC features in `capabilities`. Capability values
reflect host-enforced permissions and host settings for the active device.
`capabilities.gestureDebug` defaults to `false`; `capabilities.inputAck` is
`true` when the host confirms input delivery with `input.ack` / `input.error`.
The mobile app only shows the gesture debug entry when the host explicitly enables it. The mobile app only
shows the keyboard sleep button when `capabilities.sleep` is `true` and the
local **Show sleep button** keyboard setting is enabled.

Put the PC to sleep:

```json
{ "type": "system.sleep" }
```

The host ignores `system.sleep` when the effective **Allow PC sleep**
permission is disabled.

## Audio

The mobile app only shows volume controls when `capabilities.volume` is `true`
and the local **Show volume control** trackpad setting is enabled. The host
ignores audio mute and volume commands when the effective **Allow volume
control** permission is disabled.

Request the default Windows output device state:

```json
{ "type": "audio.get" }
```

The host reports the default Windows output device state after `audio.get` and
after accepted audio commands:

```json
{ "type": "audio.state", "volume": 72, "muted": false }
```

Toggle the default output device mute state:

```json
{ "type": "audio.mute.toggle" }
```

Set the default output device master volume. The host clamps `volume` to
`0-100` and unmutes the device when applying the value.

```json
{ "type": "audio.volume.set", "volume": 45 }
```
