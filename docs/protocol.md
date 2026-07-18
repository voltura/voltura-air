# Protocol

This document defines the WebSocket wire contract: message shapes, semantics,
authentication, capability negotiation, bounds, acknowledgements, and errors.
See [features.md](features.md) for product behavior,
[network-and-host-selection.md](network-and-host-selection.md) for host
selection, and [pairing-feedback.md](pairing-feedback.md) for connection UX.

Voltura Air uses JSON messages over a WebSocket connection at `/ws`. The host
accepts missing WebSocket `Origin` headers, same-origin requests, configured
development client origins, and loopback/private LAN origins. Clearly unrelated
public origins are rejected before the WebSocket is accepted.

The host accepts at most 64 concurrent WebSocket sessions. Every session must
send `pair.hello` within 10 seconds, and an authenticated session is closed
after 2 minutes without a client message. Text messages are limited to 64 KiB
across all WebSocket fragments; oversized messages close with status 1009 and
binary messages are rejected. These are resource and stale-connection bounds,
not authentication mechanisms.

Host sends are serialized per authenticated socket and have a 5-second operation
deadline; close handshakes have a 1-second deadline. Repeated local status changes
are coalesced by one host-owned broadcaster so settings activity cannot create an
unbounded queue of send tasks. A stalled or removed socket cannot bypass the send
gate or hold host shutdown indefinitely.

The Windows host generates an absolute HTTP or HTTPS pairing link on the exact
`/pair` app route, without URL credentials or a fragment. A generated link
contains exactly one `t` and one `v`, and at most one `h`. The mobile parser
requires that route and parameter cardinality. The ordinary `/` app route does
not import pairing credentials.

Host-generated pairing links use these query parameters:

- `t`: required 32-character URL-safe Base64 short-lived pairing token.
- `v`: required semantic host application-version metadata. The mobile parser
  validates its shape but does not use it as authentication, compatibility
  enforcement, or a cache buster.
- `h`: optional PC host hint for `/ws` traffic when the web app is served from
  a different origin than the Windows host. `h` can be a full origin such as
  `http://192.168.1.20:51395` or a port such as `51395`, which resolves against
  the current page host.

The mobile app can add these non-secret identity parameters to its current
address:

- `d`: non-secret client identifier used by browser and Home Screen launches.
- `n`: non-secret mobile device display name.

After importing a valid link into its pending pairing state, the mobile app
removes `t` from the current history entry before device-name confirmation and
network authentication. The route and non-secret parameters can remain.

`/pair` is an application route, not a caching mechanism. App-shell navigation
is network-first in the service worker, and the host serves the HTML entry point
with `Cache-Control: no-store, no-cache, must-revalidate`. The cached `/` shell
is used only when a navigation network request fails. Token uniqueness and
expiry protect pairing; neither the `/pair` path nor `v` makes a stale token
valid.

## Host hints

The `h` host hint is connection routing metadata only. It is not a secret and
must not be treated as proof that a device is paired. A WebSocket session still
has to authenticate through `pair.hello` with either a valid `pairToken` or a
stored reconnect `secret`.

The reference client accepts these host forms:

- an origin such as `http://192.168.1.50:51395`;
- an address and port such as `192.168.1.50:51395`;
- a full Voltura Air pairing link;
- a port number, which resolves against the current page host.

The manual-entry validation and state-preservation behavior for these forms is
defined in [network-and-host-selection.md](network-and-host-selection.md).

If the host value has no pairing token, authentication can complete only with a
valid stored reconnect secret. Host hints and saved profiles never bypass
pairing. Profile creation, selection, and recovery behavior are defined in
[network-and-host-selection.md](network-and-host-selection.md).

## Pairing

The client must start every WebSocket session with `pair.hello`. `deviceName`
is the current display name for the mobile device. A QR pairing attempt sends a
short-lived single-use `pairToken`; a normal reconnect sends the stored
`secret`. If `pairToken` is present, the host validates that explicit pairing
attempt even when the request also contains a reconnect secret. The host uses a
reconnect secret only when `pairToken` is absent and stores only a hash of each
reconnect secret.

The mobile app stores a generated `clientId` in browser storage and also keeps
that same non-secret value in the page URL as `d`. This lets a browser-created
Home Screen bookmark reopen with the same logical client identity even if the
standalone web app starts with separate storage. The value is not treated as an
authentication secret; reconnect still requires the stored `secret`, and a
fresh storage container must pair once with a valid `pairToken` before it can
store its own secret.

Accepting a valid `pairToken` generates a new reconnect secret and consumes both
the current and overlap token slots. For an already-known `clientId`, the host
also revokes the previous secret and existing active sockets while retaining one
paired-device record.

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone",
  "pairToken": "short-lived-token"
}
```

Reconnect request:

```json
{
  "type": "pair.hello",
  "clientId": "browser-generated-id",
  "deviceName": "iPhone",
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
      "mode": "off",
      "expiresAt": null
    },
    "sleep": true,
    "volume": true,
    "presentation": null,
    "remoteLaunch": true,
    "urlOpen": { "canOpen": false },
    "textTransfer": true,
    "clipboardRead": false
  },
  "host": {
    "hostVersion": "1.2.3",
    "webClientBuildId": "0f7c918ea4b24dd687ed15c30745d8cf",
    "pcName": "WINDOWS-PC",
    "defaultRemoteMode": "standard",
    "selectedAdapterName": "Wi-Fi - Intel(R) Wi-Fi 6 AX200",
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
      "mode": "off",
      "expiresAt": null
    },
    "sleep": true,
    "volume": true,
    "presentation": null,
    "remoteLaunch": true,
    "urlOpen": { "canOpen": false },
    "textTransfer": true,
    "clipboardRead": false
  },
  "host": {
    "hostVersion": "1.2.3",
    "webClientBuildId": "0f7c918ea4b24dd687ed15c30745d8cf",
    "pcName": "WINDOWS-PC",
    "defaultRemoteMode": "standard",
    "selectedAdapterName": "Wi-Fi - Intel(R) Wi-Fi 6 AX200",
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

Host metadata is included after authentication in `pair.accepted` and `status`.
It is not a secret and must not be used as authentication state. Most fields are diagnostics metadata; `defaultRemoteMode` and `pointerSpeed` are authenticated host profile hints used by the mobile app.
The adapter name can reveal local hardware/vendor details, so it should only be copied when the user explicitly chooses **Copy diagnostics**.
`defaultRemoteMode` is the host's advisory initial Remote mode for that PC (`standard`, `youtube`, or `kodi`). The mobile app uses it only when the current phone/browser has no saved Remote mode override for that PC.
`remoteLaunch` is an authenticated capability. When `true`, the host allows this paired device to trigger the fixed host-defined launch actions documented below and exposes its approved configurable buttons through `host.appLaunchActions`. The host does not expose the configured YouTube URL, executable paths, or arguments through this metadata.
`appLaunchActions` is an authenticated array of `{ id, label, kind }` summaries. It is empty when the effective **Allow paired devices to start applications** permission is disabled. `id` is an opaque host-owned identifier; clients must not derive a path or command from it. `label` is the host-managed 1–10 character button label. `kind` is one of `browser`, `spotify`, `vlc`, `powerpoint`, or `custom` and is presentation metadata only.
`urlOpen` is an authenticated capability object emitted by hosts that support reviewed URL opening. `canOpen` is the effective **Open web addresses** permission for the paired device. The permission defaults off and is independent from application launch and text transfer.
`textTransfer` is an authenticated capability. When `true`, the client may use the host-acknowledged `text.send` operation described below. `host.textTransferTarget` contains only `{ mode, displayName, available }`, where `mode` is `focused`, `clipboard`, or `configured`. Executable paths, process identifiers, window handles, matching rules, and clipboard contents are never included.
`clipboardRead` is an authenticated capability. `true` means the effective **Read PC clipboard** permission allows this paired device; `false` means the host blocks the operation.
`pointerSpeed` is the effective pointer speed for the authenticated paired device: the host default unless that device has an override. It is included only on authenticated `pair.accepted` and `status` messages. When the Windows host profile changes, the host may push the same lightweight `status` message to active sockets; the mobile app does not add a polling loop, timer, or extra battery cost for pointer-speed sync.
`customPointerEnabled` is the host-wide Custom pointer state. It is not a paired-device preference: changing it affects the whole Windows desktop.
`inputBlockedByElevation` is `true` only while Windows reports that a
higher-integrity foreground application blocks normal injected input. The host
pushes authenticated `status` when this state changes.
`webClientBuildId` identifies the exact compiled mobile web bundle currently served by the host. Vite generates a new opaque ID for every build, and the same ID is embedded in the JavaScript bundle and written to `web-build-id.txt`. When auto-refresh is enabled, the client clears its service worker and caches and reloads only when the host build ID differs from the ID embedded in the running client. This build ID is separate from `hostVersion` and does not affect installer or release versioning.
When host developer mode is enabled in **Preferences** -> **Developer tools**, host metadata also includes `developerMode: true` and a `developerSessionId` for the current host run.

QR pairing tokens are valid for five minutes. The Connect screen rotates the
visible token 15 seconds before expiry and keeps only the immediately previous
token valid for at most that 15-second overlap. Successful pairing consumes both
available token slots, and the Connect screen generates the next visible code.

Rejected response:

```json
{
  "type": "pair.rejected",
  "reason": "invalid-token"
}
```

The mobile client derives the user-visible `VAIR-PAIR-*` diagnostic code from
`reason`; the host does not send a diagnostic-code field.

Known pairing rejection reasons:

| Reason | Meaning |
| --- | --- |
| `pair-first` | The client sent a non-pairing message before authentication. |
| `missing-token` | No `pairToken` or valid reconnect `secret` was supplied. |
| `invalid-token` | The supplied token does not match the retained current or overlap code. |
| `expired-token` | The supplied token matches a retained code whose validity ended. |
| `stale-token` | No active pairing-code state is available. |
| `secret-revoked` | The stored reconnect credential is no longer valid. |
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

Set this device's pointer speed override on the PC after pairing has been accepted. This is sent only from a user action such as changing the mobile pointer speed slider:

```json
{ "type": "pointer.speed.set", "pointerSpeed": 65 }
```

Turn the host-wide Custom pointer on or off from a paired device:

```json
{ "type": "custom.pointer.set", "enabled": true }
```

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
active, without resetting a timer for every movement frame, slows checks after
the foreground app is idle, and closes the WebSocket while the browser page or
installed app is backgrounded.
The host's 2-minute receive deadline is reset by any valid client message, so
the passive 60-second foreground health check keeps an otherwise idle session
open without adding server-side polling.

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

Remote launch actions are fixed host-defined actions. They are only accepted after authentication, only when `capabilities.remoteLaunch` is `true`, and only for supported action names. The client must not send executable paths, process names, shell commands, or URLs.

```json
{ "type": "remote.launch", "action": "openYoutube" }
```

```json
{ "type": "remote.launch", "action": "startOrActivateKodi" }
```

`openYoutube` opens Chrome with the host-configured YouTube URL. `startOrActivateKodi` focuses Kodi when it is already running or starts Kodi when it is not. Unsupported action names are rejected as invalid protocol shape. The host ignores valid launch actions when the effective **Allow paired devices to start applications** permission is disabled.

Configurable application buttons use a separate message. The client sends only
an `actionId` that was advertised in authenticated host metadata. It must never
send a path, URL, process name, arguments, or shell command.

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
{ "type": "app.launch", "actionId": "custom.1234" }
```

The host authenticates the socket, applies the effective launch permission,
resolves the opaque ID against its current settings, revalidates custom paths,
and returns a result without closing the connection for an execution failure.

```json
{
  "type": "app.launch.result",
  "actionId": "custom.1234",
  "succeeded": true,
  "code": "started",
  "message": "Started Notes."
}
```

Failure codes include `permission-denied`, `not-configured`,
`invalid-target`, `not-found`, and `start-failed`. A malformed ID is a protocol
shape violation and closes the authenticated socket. Custom `.exe` paths and
arguments are approved, stored, validated, and executed only by the Windows
host; they are excluded from protocol metadata and application logs.

## Open URL on the PC

URL opening is a separate acknowledged operation. The client generates an
`operationId` and sends the reviewed draft; it does not send a browser path,
command, or fallback choice.

```json
{
  "type": "url.open",
  "operationId": "d6420638-df52-47c1-a2bd-fd91a68899aa",
  "url": "example.com/page?q=test"
}
```

The host trims the value and adds `https://` only when it has no explicit
scheme. It then requires an absolute HTTP or HTTPS URI with a non-empty host,
no control characters, and at most 2,048 UTF-16 code units. Explicit HTTP is
preserved. File paths, commands, malformed URLs, and schemes such as
`javascript:`, `data:`, `mailto:`, and `file:` are rejected rather than
modified or executed.

After permission and validation succeed, the host calls `Process.Start` once
with the normalized absolute URI and `UseShellExecute = true`. Windows therefore
uses the signed-in user's registered default handler for HTTP or HTTPS. The host
does not locate or fall back to Chrome, Edge, Brave, Opera, or another browser.

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

Result codes are `accepted`, `permission-denied`, `invalid-url`,
`unsupported-scheme`, and `launch-failed`. Native process errors are mapped to
friendly text and never returned raw. Validation and launch failures keep the
authenticated socket open. A successful result says **Open request sent**
because the host knows
only that Windows accepted the shell-launch request, not whether a page loaded.
When application logging is enabled, the host writes a sanitized `url.open`
command receipt with action `open_url` and a matching command outcome. Neither
entry contains the submitted or normalized URL.

## Text transfer

Text transfer uses a separate acknowledged operation so the client can distinguish complete delivery from ordinary input-health acknowledgements. `operationId` is a client-generated UUID, `text` must contain 1–4,096 UTF-16 code units, and `sendEnter` is required. The default focused destination sends to the application that owns keyboard focus and does not change the Windows clipboard. Clipboard mode copies only. A host-configured managed destination either creates a fresh self-identifying draft or stages text on the Windows clipboard. Paste-driven destinations paste only after the exact intended window is foreground and not elevated above the host; otherwise they report clipboard-only success. The host never synchronizes either device clipboard.

```json
{
  "type": "text.send",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "text": "Hello from my phone",
  "sendEnter": false
}
```

The host preserves multiline text by translating LF, CRLF, and CR line breaks into physical Enter key events for paste-driven destinations; CRLF produces one Enter. Generated text, Word, and Excel drafts preserve line breaks in their file formats. Notepad++ opens a generated text draft by file path instead of receiving new-item or paste shortcuts. **Send text + Enter** adds a final Enter or trailing blank draft line. The host refuses focused delivery while the protected Voltura Air host UI has focus. A native partial-input failure is reported as failure and requires an explicit retry; clients keep the draft and warn users to inspect the destination before retrying.

```json
{
  "type": "text.send.result",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "succeeded": true,
  "code": null,
  "message": "Text pasted into Windows Notepad.",
  "deliveryKind": "pasted"
}
```

`deliveryKind` is `typed`, `pasted`, or `clipboard`. Failure codes are `VAIR-TEXT-HOST-FOCUSED`, `VAIR-TEXT-NATIVE-SEND-FAILED`, `VAIR-TEXT-CLIPBOARD-FAILED`, and `VAIR-TEXT-DELIVERY-FAILED`. The mobile client can also produce `VAIR-TEXT-RESPONSE-TIMEOUT` when no matching result arrives. Delivery failures keep the authenticated socket open.

## Explicit PC clipboard read

`clipboard.get` is the only protocol operation that reads PC clipboard text. The client must generate `operationId`; the host reads only after the paired device's effective **Read PC clipboard** permission allows it. It returns at most 4,096 UTF-16 code units, does not alter the PC clipboard, and does not write to the device/browser clipboard. The web app displays the result for manual selection and copying.

```json
{ "type": "clipboard.get", "operationId": "820c1314-d8a1-499d-a969-6520f681baea" }
```

```json
{
  "type": "clipboard.get.result",
  "operationId": "820c1314-d8a1-499d-a969-6520f681baea",
  "succeeded": true,
  "code": null,
  "message": "Text fetched from the PC clipboard.",
  "text": "Example PC clipboard text"
}
```

When permission is blocked, no clipboard read occurs and the host returns `VAIR-CLIPBOARD-PERMISSION-DENIED`. Other expected failures are `VAIR-CLIPBOARD-NO-TEXT`, `VAIR-CLIPBOARD-TEXT-TOO-LONG`, and `VAIR-CLIPBOARD-UNAVAILABLE`.

## Presentation commands

Presentation mode is a default-off alpha feature. The host advertises it only while **Preferences > Developer tools > Enable alpha features** is enabled and enforces the same gate before any command reaches input injection. It uses a separate acknowledged command set rather than sending arbitrary `keyboard.special` messages. The client supplies one bounded operation ID, a reviewed target profile, and one fixed action. The official client permits only one in-flight presentation command, disables every presenter control until its matching result arrives, and clears pending state on disconnect instead of replaying a slide change later.

```json
{
  "type": "presentation.command",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "next"
}
```

Targets are `powerpoint`, `google-slides`, and `pdf`. Actions are `next`, `previous`, `start`, `end`, `black`, and `pointer`; a recognized action can still be unavailable for a target and then returns `unsupported-action` without injecting input. Mappings are:

| Target | Next | Previous | Start | End | Black | Pointer |
| --- | --- | --- | --- | --- | --- | --- |
| PowerPoint | Right | Left | F5 | Esc | B | Ctrl+L |
| Google Slides | Right | Left | unavailable | Esc | B | L |
| PDF/browser | Right | Left | unavailable | Esc | unavailable | unavailable |

These low-level mappings are intentionally user-selected. The host does not
inspect the focused process, presentation file, slide number, or application
state. Clients must not send an unavailable target/action combination. The
separately permissioned `system.power` `blackoutDisplay` action is distinct from
the presentation `black` shortcut. The target scope follows the current
[PowerPoint presentation shortcuts](https://support.microsoft.com/en-us/office/use-keyboard-shortcuts-to-deliver-powerpoint-presentations-1524ffce-bd2a-45f4-9a7f-f18b992b93a0)
and [Google Slides shortcuts](https://support.google.com/docs/answer/1696717).

With the alpha gate off, the capability is explicitly unavailable:

```json
{ "presentation": null }
```

With the gate on, the host advertises the paired device's effective global/per-device permission:

```json
{ "presentation": { "canControl": true } }
```

After validation and permission enforcement, the host performs one shortcut injection and returns the matching operation, target, and action:

```json
{
  "type": "presentation.command.result",
  "operationId": "2fd6j9q-01az82x-18c8qtm-0kj3y5s",
  "target": "powerpoint",
  "action": "next",
  "succeeded": true,
  "code": null,
  "message": "Next slide command sent."
}
```

Failure codes are `feature-disabled`, `permission-denied`, `unsupported-action`, `host-ui-blocked`, and `input-failed`. `feature-disabled` is returned before input work when the alpha gate is off. The client can additionally report `VAIR-PRESENTATION-RESPONSE-TIMEOUT` if no matching result arrives. Expected denial and native input failures keep the authenticated socket open. Success confirms that Windows accepted the shortcut sequence; it does not claim that an application changed slides.

Input delivery acknowledgement:

When `capabilities.inputAck` is `true`, the mobile client adds a positive `seq`
number to every discrete pointer/keyboard input and to periodic movement
messages. After the host dispatches that input to Windows, it sends `input.ack`
for the same sequence. Movement acknowledgements are deliberately sampled at a
low rate rather than added to every animation frame.

```json
{ "type": "input.ack", "seq": 123 }
```

If the host accepts the WebSocket message but cannot dispatch the input to
Windows, it sends `input.error` for that action and keeps the authenticated
socket open. The mobile client shows the failed action, drops that action, and
continues with later pointer or keyboard input. The host reserves socket closure
for malformed protocol, authentication failure, revoked pairing, shutdown, or
other connection-level failures.

```json
{
  "type": "input.error",
  "seq": 123,
  "code": "VAIR-INPUT-NATIVE-SEND-FAILED",
  "message": "Windows did not accept this input action. Try again."
}
```

When the host closes an authenticated socket for invalid protocol, the client
must treat it as a connection failure and must not replay dropped physical-input
commands after reconnecting.

The mobile client treats missing acknowledgements for recent input as an
unhealthy connection and reconnects. Heartbeat success alone is not enough to
keep the UI in the paired state when input delivery is not being confirmed.
While a sampled movement acknowledgement is outstanding, the official client
allows only a small bounded number of later movement frames. It also stops
adding movement when the browser reports a growing WebSocket send buffer.
Congested movement is dropped, not replayed after the finger is lifted. This
keeps pointer latency bounded without per-move acknowledgements, extra polling,
or idle battery cost; discrete button and keyboard input is not discarded by
the movement limit.

## System

The host reports optional PC features in `capabilities`. Capability values
reflect host-enforced permissions and host settings for the active device.
`capabilities.gestureDebug` defaults to `false`; `capabilities.inputAck` is
`true` when the host confirms input delivery with `input.ack` / `input.error`.
`capabilities.presentation` is `null` while the reusable, default-off alpha gate
is disabled. While the gate is enabled, it is an object whose `canControl` value
is the active device's effective Presentation control permission. Clients must
not expose or send capability-gated operations while their corresponding
capability is absent, false, or `null`.

Put the PC to sleep:

```json
{ "type": "system.sleep" }
```

The host ignores `system.sleep` when the effective **Allow PC sleep**
permission is disabled.

The host reports each Power & session permission separately in
`capabilities.power`. The object remains present when every action is disabled.

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

The `lock` Boolean reports the effective host permission. `lockAvailability`
reports the explicit current-user Windows policy state as
`notExplicitlyDisabled`, `disabledByPolicy`, or `unavailable`. A missing value
is `notExplicitlyDisabled`; that means no explicit user block was found, not
that locking is proven to work. This keeps permission denial distinct from a
Windows policy that prevents workstation locking. `screenSaverAvailable` is
true only when Windows reports screen saving enabled and an actual `.scr`
program is configured. The separate `screenSaver` Boolean remains the effective
host permission.

Request a fixed Windows power or session action:

```json
{ "type": "system.power", "action": "lock" }
```

Supported action values are `lock`, `blackoutDisplay`, `displayOff`,
`screenSaver`, `signOut`, `restart`, and `shutdown`. The host validates the fixed
action name, checks platform availability, and checks its effective
global/per-device permission before execution. Lock, blackout, and an available
screen saver are allowed by default; display off and the three session-ending
actions are blocked by default.

`blackoutDisplay` creates a borderless, topmost black WPF window for every
connected monitor. It does not change display power state, so Windows, the host,
and networking remain active. Local mouse, keyboard, touch, or pen input closes
the blackout windows. The host also closes them before dispatching any later
remote pointer or keyboard message, so the client reliably restores the view.

`screenSaver` sends Windows' native screen-saver system command. It returns
`VAIR-POWER-UNAVAILABLE` without execution when no enabled and configured screen
saver is exposed by Windows.

`displayOff` sends the Windows `SC_MONITORPOWER` command, including to
HDMI-connected TVs and receivers. Some PCs treat that explicit monitor-off
request as sleep or Modern Standby, suspending the host and network connection.
The protocol cannot reliably wake such a PC because no client message can reach
the suspended host; physical keyboard or mouse input may be required. The
client must use normal connection-health handling rather than treating the
accepted command as proof that the host remains reachable. Windows may present
its sign-in UI after
resuming; `displayOff` does not sign out the session and the protocol does not
carry Windows credentials. Sign out, restart, and shut down use the fixed Windows
`shutdown.exe` executable with fixed arguments; client-provided paths,
arguments, and shell commands are never accepted.

Every well-formed `system.power` request receives a result. Success means that
Windows accepted or started the request; it is not an assertion that a later
restart or shutdown completed.

```json
{
  "type": "system.power.result",
  "action": "lock",
  "succeeded": false,
  "code": "VAIR-POWER-LOCK-DISABLED",
  "message": "Windows locking is disabled. Enable it in the Voltura Air host settings."
}
```

Failure codes distinguish `VAIR-POWER-DENIED`,
`VAIR-POWER-UNSUPPORTED`, `VAIR-POWER-UNAVAILABLE`, `VAIR-POWER-LOCK-DISABLED`,
`VAIR-POWER-LOCK-UNAVAILABLE`, and `VAIR-POWER-EXECUTION-FAILED`.
These action-level failures are recoverable and do not close the authenticated
WebSocket. A malformed message still violates protocol policy.

## Keep awake

The host reports the shared Awake state and the active device's effective
permission in `capabilities.awake`. State is reported even when control is
blocked.

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

`mode` is `off`, `indefinite`, `timed`, or `expiration`. `expiresAt` is a UTC
ISO-8601 timestamp for timed and expiration modes and `null` otherwise. Tray,
Preferences, timer expiry, and remote changes all update the same host-owned
state. The host broadcasts a fresh `status` message to connected clients when
that state changes.

The mobile protocol intentionally exposes only a basic on/off action:

```json
{ "type": "awake.set", "enabled": true }
```

`enabled: true` selects indefinite mode and `enabled: false` selects Off. The
request never carries display behavior; the existing **Keep screen on** choice
from the Windows host remains authoritative. The host rejects the request when
the effective global/per-device **Allow paired devices to control Keep awake**
permission is off.

Every valid request receives a result and keeps the authenticated socket open:

```json
{
  "type": "awake.result",
  "enabled": true,
  "succeeded": false,
  "code": "VAIR-AWAKE-DENIED",
  "message": "Keep awake control is disabled by the PC host."
}
```

Failure codes are `VAIR-AWAKE-DENIED` and
`VAIR-AWAKE-EXECUTION-FAILED`. A malformed `enabled` value violates normal
protocol validation. Keep awake uses the signed-in user's Windows execution
state, does not edit the selected power plan, does not require elevation, and
does not override manual sleep, lid close, or lock-screen behavior.

## Audio

The host ignores audio mute and volume commands when the effective **Allow
volume control** permission is disabled.

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
