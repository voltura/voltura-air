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

When a QR link contains a pairing token, the app asks the user to confirm the
mobile device name before it sends the pairing request.

## Failure mapping

Map pairing failures to friendly messages and recovery actions.

| Reason or condition | User meaning | Recovery |
| --- | --- | --- |
| `qr-unreadable` | The uploaded photo did not contain a readable QR code. | Retake the photo, zoom in, avoid glare, or scan a fresh QR code. |
| `qr-not-pairing-link` | The QR code is not a Voltura Air pairing link. | Scan the QR code from the PC Connect screen. |
| `missing-token` | The request did not include a pairing token or valid reconnect secret. | Scan the PC QR code. |
| `expired-token` | The active QR token expired before use. | Click **New code** on the PC and scan again. |
| `stale-token` / `used-token` / `token-already-used` | The QR code was already used or replaced. | Click **New code** on the PC and scan the latest QR code. |
| `invalid-token` | The pairing token is invalid, old, replaced, or from another PC. | Scan a fresh QR code from the PC. |
| `device-revoked` / `secret-revoked` | The saved device credential is no longer valid. | Scan a fresh QR code to pair again. |
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

- **Take photo of QR code** / **Scan new QR code** for first pairing and QR/token failures.
- **Try reconnect** for host-unavailable failures.
- **Enter host manually** for changed IP address, changed port, stale QR page, or network troubleshooting.
- **Open troubleshooting help** for same Wi-Fi/LAN, firewall, stale QR, and changed host/port guidance.
- **Copy diagnostics** for repeat failures.

**Try reconnect** keeps the connection panel visible, disables repeat attempts,
and shows progress. Success returns to the previous mode; failure restores
**PC not available**. Automatic retries never replay disconnected input.

From `unavailable`, **Scan new QR code** keeps the saved PC and opens the new
device-name confirmation. Events from the previous connection cannot replace
that confirmation.

Manual host entry follows [network and host selection](network-and-host-selection.md)
and attempts connection without leaving the app.

## Layout requirement

The UI must remain usable on small phones and short landscape viewports. Pairing
feedback panels must scroll inside the viewport instead of hiding action buttons
below the screen.

When troubleshooting help and manual host entry are both expanded, these controls
must remain reachable:

- primary QR/reconnect action;
- optional secondary QR action;
- manual host input and submit button;
- troubleshooting help;
- copy diagnostics.

## Diagnostics

Copied diagnostics are for troubleshooting only and must not include secrets or full pairing tokens. They may include:

- pairing state;
- failure reason;
- diagnostic code;
- current page URL;
- browser user agent;
- display mode;
- timestamp.

Diagnostic codes should use the `VAIR-PAIR-*` shape so a user can paste a short
identifier into an issue or support request without exposing secrets.
