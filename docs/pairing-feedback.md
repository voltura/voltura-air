# Pairing failure feedback

Goal: no user should sit stuck on the mobile pairing screen without an explanation or a next action.

## Current mobile states

The mobile app currently uses a compact connection state model and derives the
visible pairing feedback from state plus message text:

| State | User-visible meaning |
| --- | --- |
| `needs-pairing` | No active paired PC is available. The user can scan a QR code, choose a saved PC, or enter a host manually. |
| `connecting` | The app is opening `/ws` and sending `pair.hello`. |
| `paired` | The WebSocket is authenticated and commands can be sent. |
| `rejected` | The host accepted the WebSocket but rejected pairing/reconnect. |
| `unavailable` | The browser cannot reach the active PC host, or heartbeat failed after a previous connection. |
| `disconnected` | The user intentionally disconnected from the active PC. |

When a QR link contains a pairing token, the app asks the user to confirm the
mobile device name before it sends the pairing request.

## Failure mapping

Pairing failures should be mapped to friendly messages, not raw protocol strings.

| Reason or condition | User meaning | Recovery |
| --- | --- | --- |
| `qr-unreadable` | The uploaded photo did not contain a readable QR code. | Retake the photo, zoom in, avoid glare, or scan a fresh QR code. |
| `qr-not-pairing-link` | The QR code is not a Voltura Air pairing link. | Scan the QR code from the PC Connect screen. |
| `missing-token` | The request did not include a pairing token or valid reconnect secret. | Scan the PC QR code. |
| `expired-token` | The active QR token expired before use. | Click **New code** on the PC and scan again. |
| `stale-token` / `used-token` / `token-already-used` | The QR code was already used or replaced. | Click **New code** on the PC and scan the latest QR code. |
| `invalid-token` | The pairing token is invalid, old, replaced, or from another PC. | Scan a fresh QR code from the PC. |
| `device-revoked` / `secret-revoked` | The saved device credential is no longer valid. | Scan a fresh QR code to pair again. |
| `protocol-version-mismatch` | The mobile app and host do not speak the same protocol version. | Refresh the mobile app from the PC and scan again. |
| `rate-limited` | The PC temporarily blocked repeated failed pairing attempts. | Wait a moment, click **New code** on the PC, and scan again. |
| `invalid-message` | The host rejected the pairing request because it was not in the expected format. | Refresh the mobile app from the PC and scan a fresh QR code. |
| `pair-first` | The host received a non-pairing message before authentication. | Scan a fresh QR code and reconnect. |
| Unknown raw reason | Host sent an unrecognized rejection. | Show a `VAIR-PAIR-*` diagnostic code and let the user copy diagnostics. |
| `host-unreachable` / PC not available | The browser cannot open or keep the WebSocket connection to the PC host. | Try reconnect, scan a fresh QR code, enter the current host/IP:port manually, check same Wi-Fi/LAN, and allow Windows Firewall on private networks. |

When the browser cannot open the WebSocket at all, treat the failure as
`host-unreachable`. Do not pretend to know whether the exact cause is firewall,
wrong Wi-Fi, mobile data, VPN, stale QR, changed IP, or changed port.

## Recovery actions

The pairing screen should expose the relevant actions directly near the error:

- **Take photo of QR code** / **Scan new QR code** for first pairing and QR/token failures.
- **Try reconnect** for host-unavailable failures.
- **Enter host manually** for changed IP address, changed port, stale QR page, or network troubleshooting.
- **Open troubleshooting help** for same Wi-Fi/LAN, firewall, stale QR, and changed host/port guidance.
- **Copy diagnostics** for repeat failures.

Manual host entry should not just navigate away. A valid manual host should
create or update a saved PC profile, select it as active, and attempt to connect
inside the app. Saved profiles remain removable with **Forget**.

Manual host input may be a full origin, address plus port, full Voltura Air
pairing link, or a port number that resolves against the current page host.

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

## Host guidance

Prefer specific host rejection reasons when they can be distinguished safely:

- `missing-token` when no token or valid reconnect secret was sent.
- `expired-token` when the active token exists but is expired.
- `stale-token` when the QR token was already consumed or replaced.
- `invalid-token` when a token was sent but does not match the active token.
- `device-revoked` or `secret-revoked` when a previously paired device no longer has valid credentials.
- `protocol-version-mismatch` when the client and host protocol versions are incompatible.
- `rate-limited` when repeated failed unauthenticated pairing attempts are temporarily blocked.
- `invalid-message` when `pair.hello` is malformed.
- `pair-first` when the first client message is not `pair.hello`.

Network reachability failures usually happen before the protocol can return a
host rejection reason. In those cases, the mobile app should show
`host-unreachable` recovery guidance.
