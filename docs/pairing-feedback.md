# Pairing failure feedback

Goal: no user should sit stuck on the mobile pairing screen without an explanation or a next action.

## User-visible pairing states

The mobile web app should present pairing and reconnect progress in plain language:

- Waiting for QR scan.
- Reading QR code.
- Confirming device name.
- Connecting to PC.
- Pairing request sent.
- Paired.
- Pairing failed.
- Reconnecting.
- PC not available.

Implementation may keep a smaller internal connection state if the UI can still show these user-visible states from message and failure context.

## Failure reasons

Pairing failures should be mapped to friendly messages, not raw protocol strings.

| Reason | User meaning | Recovery |
| --- | --- | --- |
| `qr-unreadable` | The uploaded photo did not contain a readable QR code. | Retake the photo, zoom in, or scan a fresh QR code. |
| `qr-not-pairing-link` | The QR code is not a Voltura Air pairing link. | Scan the QR code from the PC Connect screen. |
| `expired-token` | The QR code expired. | Click **New code** on the PC and scan again. |
| `stale-token` | The QR code was already used or replaced. | Click **New code** on the PC and scan again. |
| `missing-token` | The link does not contain a pairing token. | Scan the PC QR code. |
| `invalid-token` | The pairing token is invalid or not accepted. | Scan a fresh QR code from the PC. |
| `host-unreachable` | The browser cannot reach the PC host. | Check same Wi-Fi/LAN, Windows Firewall, changed IP, or changed port. |
| `device-revoked` / `secret-revoked` | The device was disconnected on the PC. | Scan a fresh QR code to pair again. |
| `protocol-version-mismatch` | The mobile app and host do not speak the same protocol. | Refresh the mobile app from the PC and scan again. |
| Unknown raw reason | Host sent an unrecognized rejection. | Show a diagnostic code and let the user copy diagnostics. |

## Recovery actions

The pairing screen should expose the relevant actions directly near the error:

- **Scan new QR code** for QR/token failures.
- **Try reconnect** for host-unavailable failures.
- **Enter host manually** for network, IP, or port changes.
- **Open troubleshooting help** for same Wi-Fi/LAN, firewall, and stale QR guidance.
- **Copy diagnostics** for repeat failures.

The UI must remain usable on small phones and short landscape viewports. Pairing feedback panels must scroll inside the viewport instead of hiding action buttons below the screen.

## Diagnostics

Copied diagnostics are for troubleshooting only and must not include secrets or full pairing tokens. They may include:

- Pairing state.
- Failure reason.
- Diagnostic code.
- Current page URL.
- Browser user agent.
- Display mode.
- Timestamp.

## Host guidance

Prefer specific host rejection reasons when they can be distinguished safely:

- `missing-token` when no token was sent.
- `expired-token` when the active token exists but is expired.
- `stale-token` when the QR token was already consumed or replaced.
- `invalid-token` when a token was sent but does not match the active token.
- `device-revoked` or `secret-revoked` when a previously paired device no longer has valid credentials.

When the browser cannot open the WebSocket at all, treat the failure as `host-unreachable` and present Wi-Fi/LAN, firewall, and changed host/port as likely causes rather than pretending to know the exact network problem.
