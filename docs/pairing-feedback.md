# Pairing and connection feedback

Pairing/connection UX. Wire reasons: [protocol](protocol.md). Adapter, port,
saved-PC, manual-host behavior:
[network selection](network-and-host-selection.md).

## States

| State | User-visible meaning |
| --- | --- |
| `needs-pairing` | No active paired PC; scan, choose a saved PC, or enter a host. |
| `connecting` | Opening the connection and authenticating. |
| `paired` | Authenticated and ready for commands. |
| `rejected` | The host rejected pairing or reconnect. |
| `unavailable` | The active PC cannot be reached or recent input/health checks failed. |
| `disconnected` | The user intentionally disconnected. |

A valid pairing link imports its token, removes `t` from the visible address,
asks for device-name confirmation, then connects. Pairing, rejection,
unavailable, and intentional-disconnect panels block inactive controls while
keeping recovery actions usable.

## Failure map

| Reason or condition | Meaning and recovery |
| --- | --- |
| `qr-unreadable` | No readable QR code; retake with the code clear and current. |
| `qr-not-pairing-link` | Not a Voltura Air link; scan the PC Connect code. |
| `expired-token` | Code expired; click **New code** and scan again. |
| `stale-token` | No active code state; scan the code currently on the PC. |
| `invalid-token` | Code does not match; scan the current PC code. |
| `device-revoked` | Pairing was removed; pair again with a fresh code. |
| `invalid-proof` | Saved reconnect proof failed; pair again to replace it. |
| `rate-limited` | Too many failures; wait, create a new code, and retry. |
| `invalid-message` or `pair-first` | Refresh the mobile app from the PC and pair again. |
| Unknown rejection | Show a `VAIR-PAIR-*` code and offer copied diagnostics. |
| `host-unreachable` | The browser cannot reach the PC; reconnect, rescan, enter the current host, and check LAN/firewall. |
| `socket-closed` | Host/network closed an authenticated connection; show available close details and reconnect without replaying input. |
| `input-ack-timeout` | Input delivery is unconfirmed; enter unavailable/retrying and reconnect. |
| `input-dispatch-failed` | Windows rejected one action; show it and keep the authenticated connection for later actions. |

The browser cannot distinguish firewall, VPN, stale address, port, or other
network causes of `host-unreachable`. Recoverable `input.error` is not a
connection failure.

## Recovery

Expose only relevant actions near the error:

- **Take photo of QR code** for first pairing and QR/token failures.
- **Try reconnect** for unreachable hosts.
- **Enter host manually** for address/port recovery.
- **Open troubleshooting help** for LAN, firewall, and stale-code guidance.
- **Copy diagnostics** for repeated failures.

Reconnect keeps the panel visible, prevents duplicate attempts, shows progress,
and never replays disconnected input. Re-pair from `unavailable` keeps the saved
PC while opening device-name confirmation; stale connection events cannot
replace that confirmation.

## Layout

Feedback scrolls within small/short viewports so actions remain reachable.
Portrait stacks status and recovery. Wide-enough landscape uses equal status
and action regions and stacks before either becomes unusable.

Manual-host and troubleshooting actions open shared dialogs. Dialog title and
actions stay visible; only the body scrolls. Keyboard show/hide and rotation
must keep input, validation, and actions reachable.

## Diagnostics

Copied diagnostics may include state, failure reason, `VAIR-PAIR-*` code,
credential-redacted page URL, browser user agent, display mode, and timestamp.
They never include full pairing tokens, private reconnect keys, challenges, or
proofs.
