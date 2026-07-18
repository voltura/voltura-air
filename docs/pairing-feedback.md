# Pairing failure feedback

Every blocked pairing or connection state explains the problem and offers a
recovery action.

## Current mobile states

The mobile app derives visible feedback from these connection states:

| State | User-visible meaning |
| --- | --- |
| `needs-pairing` | No active paired PC is available. The user can scan a QR code, choose a saved PC, or enter a host manually. |
| `connecting` | The app is opening `/ws` and sending `pair.hello`. |
| `paired` | The WebSocket is authenticated and commands can be sent. |
| `rejected` | The host accepted the WebSocket but rejected pairing/reconnect. |
| `unavailable` | The browser cannot reach the active PC host, or health checks failed after a previous connection. |
| `disconnected` | The user intentionally disconnected from the active PC. |

A valid `/pair` link imports its token into a pending pairing attempt and removes
`t` from the visible address. The app then asks the user to confirm the mobile
device name before it opens the pairing connection.

## PC pairing-code lifecycle

Each generated pairing code is valid for five minutes. The visible Connect screen
shows a minute-and-second countdown and replaces its code 15 seconds before
expiry. It performs no countdown work while hidden; reopening Connect replaces a
code that has reached its refresh time before showing it. **New code** replaces
the visible code immediately.

The Connect screen groups the countdown, **New code**, and **Copy link** in the
QR status card so the code actions remain visible. Technical connection details
use a collapsed accordion that grows only into the remaining page height; its
body scrolls when the details do not fit. When automatic selection finds
multiple network adapters, the warning emphasizes the selected adapter name.

Rotation retains only the immediately previous code, for no more than 15
seconds, so a scan already in progress can finish. Accepting a code consumes both
available code slots and makes the Connect screen generate and display a new
code. This also applies when a previously paired client explicitly pairs again.

Generated links use the dedicated `/pair` app route. The ordinary `/` route
opens Voltura Air without importing pairing credentials.

Pairing, intentional-disconnect, rejection, and unavailable panels block the
inactive app controls behind them. Their recovery actions remain available in
the panel's scrollable content.

## Failure mapping

Map pairing failures to friendly messages and recovery actions.

| Reason or condition | User meaning | Recovery |
| --- | --- | --- |
| `qr-unreadable` | The uploaded photo did not contain a readable QR code. | Retake the photo, zoom in, avoid glare, or scan a fresh QR code. |
| `qr-not-pairing-link` | The QR code is not a Voltura Air pairing link. | Scan the QR code from the PC Connect screen. |
| `missing-token` | The request did not include a pairing token or valid reconnect secret. | Scan the PC QR code. |
| `expired-token` | The supplied token matches a retained code, but its validity ended before use. | Click **New code** on the PC and scan again. |
| `stale-token` | The PC has no active pairing-code state, normally because the available code was consumed or cleared. | Scan the code currently displayed by the PC. |
| `invalid-token` | The supplied token does not match the current code or its bounded overlap code. | Scan the code currently displayed by the PC. |
| `secret-revoked` | The saved reconnect credential is no longer valid. | Scan a fresh QR code to pair again. |
| `rate-limited` | The PC temporarily blocked repeated failed pairing attempts. | Wait a moment, click **New code** on the PC, and scan again. |
| `invalid-message` | The host rejected the pairing request because it was not in the expected format. | Refresh the mobile app from the PC and scan a fresh QR code. |
| `pair-first` | The host received a non-pairing message before authentication. | Scan a fresh QR code and reconnect. |
| Unknown raw reason | Host sent an unrecognized rejection. | Show a `VAIR-PAIR-*` diagnostic code and let the user copy diagnostics. |
| `host-unreachable` / PC not available | The browser cannot open or keep the WebSocket connection to the PC host. | Try reconnect, scan a fresh QR code, enter the current host/IP:port manually, check same Wi-Fi/LAN, and allow Windows Firewall on private networks. |
| `socket-closed` | The current authenticated WebSocket was closed by the host or network after pairing. | Show the close reason/code when available, reconnect automatically, and do not replay dropped input. |
| `input-ack-timeout` | The WebSocket health check may still be alive, but the host stopped confirming recent input events. | Move to unavailable/retrying and reconnect automatically. |
| `input-dispatch-failed` | The host received input but Windows did not accept the injected input events. | Surface the failed action, keep the authenticated socket alive, and continue processing later input. |

When the browser cannot open the WebSocket, use `host-unreachable`; the client
cannot distinguish firewall, network, VPN, stale-address, or port failures.

Recoverable `input.error` responses are not connection failures. They should be
visible to the user, but a later pointer, click, scroll, or keyboard action
should still be attempted on the same authenticated socket. Socket close events
are connection failures and should be visible on both sides: the host uses the
existing connection status notification setting, and the mobile client shows an
unavailable/reconnecting state with copyable diagnostics.

## Recovery actions

The pairing screen should expose the relevant actions directly near the error:

- **Take photo of QR code** / **Take photo of new QR code** for first pairing and QR/token failures.
- **Try reconnect** for host-unavailable failures.
- **Enter host manually** for changed IP address, changed port, stale QR page, or network troubleshooting.
- **Open troubleshooting help** for same Wi-Fi/LAN, firewall, stale QR, and changed host/port guidance.
- **Copy diagnostics** for repeat failures.

**Try reconnect** keeps the connection panel visible, disables repeat attempts,
and shows progress. Success returns to the previous mode; failure restores
**PC not available**. Automatic retries never replay disconnected input.

From `unavailable`, **Take photo of new QR code** keeps the saved PC and opens the new
device-name confirmation. Events from the previous connection cannot replace
that confirmation.

Manual host entry follows [network and host selection](network-and-host-selection.md)
and attempts connection without leaving the app.

## Layout requirement

The UI must remain usable on small phones and short landscape viewports. Pairing
feedback panels must scroll inside the viewport instead of hiding action buttons
below the screen.

Portrait and narrow layouts stack the status and recovery actions. Landscape
layouts with enough inline space use equal left and right regions: status and
diagnostic information on the left, recovery actions on the right. The layout
stacks before either region becomes too narrow. Landscape does not reserve the
portrait camera-area spacing above the panel.

**Enter host manually** and **Open troubleshooting help** keep their labels and
open shared modal dialogs; they do not expand content inside the feedback panel.
The manual-host dialog provides a close control, **Cancel**, and **Connect**.
The troubleshooting dialog provides a close control and **OK**. Tapping the
scrim dismisses either dialog, and Escape remains a secondary hardware-keyboard
path.

Dialog title and actions remain visible while the body is the only scroll owner.
The dialog follows the visible viewport through keyboard show/hide and rotation,
so the input, validation, and actions remain reachable in portrait and
landscape.

## Diagnostics

Copied diagnostics are for troubleshooting only and must not include secrets or full pairing tokens. They may include:

- pairing state;
- failure reason;
- diagnostic code;
- current page URL with credential values redacted;
- browser user agent;
- display mode;
- timestamp.

Diagnostic codes should use the `VAIR-PAIR-*` shape so a user can paste a short
identifier into an issue or support request without exposing secrets.
