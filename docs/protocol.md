# Protocol

Voltura Air uses JSON messages over a WebSocket connection at `/ws`.

In hot-reload development, the pairing page can be served by Vite while `/ws`
stays on the .NET host. In that case the QR URL includes `t` for the pairing
token and `h` for the PC host URL. The mobile app uses `h` as the PC WebSocket
origin. After pairing, the short-lived `t` token is removed from the address,
but non-secret `h` can remain so reloads and Home Screen bookmarks still know
which .NET host should receive `/ws` traffic.

## Pairing

The client must start every WebSocket session with `pair.hello`. `deviceName`
is the current display name for the mobile device. On a first-time pairing the
client also sends `pairToken`; on reconnect it sends the stored `secret`.

The mobile app stores a generated `clientId` in browser storage and also keeps
that same non-secret value in the page URL as `d`. This lets a browser-created
Home Screen bookmark reopen with the same logical client identity even if the
standalone web app starts with separate storage. The value is not treated as an
authentication secret; reconnect still requires the stored `secret`, and a
fresh storage container must pair once with a valid `pairToken` before it can
store its own secret.

When a valid `pairToken` is accepted for an already-known `clientId`, the host
rotates the secret, revokes old active sockets for that client, and keeps one
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
    "sleep": true
  }
}
```

Connection status response:

```json
{
  "type": "status",
  "connected": true,
  "message": "Connected",
  "pcName": "WINDOWS-PC",
  "capabilities": {
    "sleep": true
  }
}
```

Rejected response:

```json
{
  "type": "pair.rejected",
  "reason": "invalid-token"
}
```

Forget this device on the PC after pairing has been accepted:

```json
{ "type": "pair.disconnect" }
```

Rename this device on the PC after pairing has been accepted. The host updates
the paired device record, stores the rename timestamp, refreshes connected UI
such as the tray/device manager, and uses that timestamp for latest-activity
ordering.

```json
{ "type": "device.rename", "deviceName": "Joakim iPhone" }
```

The host trims `deviceName`; if the client sends a blank name, the host stores
`Mobile device`.

Connection heartbeat after pairing has been accepted:

```json
{ "type": "status.ping" }
```

Host response:

```json
{
  "type": "status.pong",
  "pcName": "WINDOWS-PC",
  "capabilities": {
    "sleep": true
  }
}
```

## Input Events

Pointer movement:

```json
{ "type": "pointer.move", "dx": 12, "dy": -4 }
```

Mouse button:

```json
{ "type": "pointer.button", "button": "left", "action": "click" }
```

Wheel scroll:

```json
{ "type": "pointer.wheel", "dx": 0, "dy": -18 }
```

Zoom gesture. `direction: "in"` is a two-finger spread/pinch-out and zooms in. `direction: "out"` is a two-finger pinch-in and zooms out.

```json
{ "type": "pointer.zoom", "direction": "in" }
```

The mobile app has a **Pinch zoom** trackpad setting. It is enabled by default and controls whether pinch/spread gestures emit `pointer.zoom` messages.

Text input:

```json
{ "type": "keyboard.text", "text": "Hello" }
```

Special key:

```json
{ "type": "keyboard.special", "key": "Enter", "modifiers": ["Control"] }
```

Undo and redo shortcut aliases:

```json
{ "type": "keyboard.special", "key": "Undo" }
```

```json
{ "type": "keyboard.special", "key": "Redo" }
```

The Windows host translates `Undo` to `Ctrl+Z` and `Redo` to `Ctrl+Y`.

## System

The host reports optional PC features in `capabilities`. The mobile app only
shows the keyboard sleep button when `capabilities.sleep` is `true` and the
local **Show sleep button** keyboard setting is enabled.

Put the PC to sleep:

```json
{ "type": "system.sleep" }
```

## Audio

The host reports the default Windows output device state after pairing, after
heartbeat pings, and after accepted audio commands:

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
