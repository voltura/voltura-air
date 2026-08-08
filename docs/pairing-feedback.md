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

A valid Direct pairing link imports its token and removes `t` from the visible address.
An official Relay link keeps the token only in the URL fragment, removes the
fragment after import, and stores the opaque route profile.
asks for device-name confirmation, then connects. Pairing, rejection,
unavailable, and intentional-disconnect panels block inactive controls while
keeping recovery actions usable.

The browser gives an initial Direct connection 3 seconds to open and authenticate.
Relay connections get 10 seconds because VPNs, managed networks, DNS, TLS, and
WebSocket inspection can add material startup latency. The longer Relay window
does not change pairing, identity verification, encryption, retry behavior, or
Direct-mode responsiveness.

Photo decoding is one bounded attempt at a time. While the selected photo is
being decoded, the primary action reads **Reading QR code...**, shows pending
feedback, and is visibly and natively disabled; secondary photo/manual actions
that could start a competing attempt are disabled too. Success, failure, or a
newer attempt clears that state. Direct QR uses `t`, `v`, and optional `h`;
Relay QR uses opaque route, `v`, and fragment token. PC identity is
authenticated after opening rather than increasing QR density.

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
| `host-identity-missing` or identity mismatch | The saved PC has no valid pinned identity; scan that PC's fresh short QR and verify it again. |
| `rate-limited` | Too many failures; wait, create a new code, and retry. |
| `invalid-message` or `pair-first` | Refresh the mobile app from the PC and pair again. |
| Unknown rejection | Show a `VAIR-PAIR-*` code and offer copied diagnostics. |
| `host-unreachable` in Direct mode | The browser cannot reach the PC; reconnect, rescan, enter the current host, and check LAN/firewall. |
| `host-unreachable` in Relay mode | The browser cannot reach the PC through the configured relay; keep the unavailable panel stable while retrying, and check the running host, PC internet access, and permitted VPN/work-network restrictions. |
| `relay-encryption-failed` or Relay identity mismatch | The encrypted session did not authenticate; reconnect, then scan a fresh QR if repeated. |
| `turn-unavailable` | Screen relay credentials are unavailable or quota-blocked; commands remain connected and Screen can be retried later. |
| `socket-closed` | Host/network closed an authenticated connection; show available close details and reconnect without replaying input. |
| `input-ack-timeout` | Input delivery is unconfirmed; enter unavailable/retrying and reconnect. |
| `input-dispatch-failed` | Windows rejected one action; show it and keep the authenticated connection for later actions. |

The browser cannot identify the underlying cause of `host-unreachable`. Direct
mode therefore names LAN, firewall, stale address, and port as possibilities;
Relay mode names PC internet access, VPN or managed-network restrictions, and
relay availability as possibilities. Recoverable `input.error` is not a
connection failure.

## Recovery

Expose only relevant actions near the error:

- **Take photo of QR code** for first pairing and QR/token failures.
- **Try reconnect** for unreachable hosts.
- **Enter host manually** for address/port recovery.
- **Open troubleshooting help** for transport-specific LAN/firewall or
  internet/VPN/work-network guidance.
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
They never include full pairing tokens, token IDs, private reconnect keys,
host-identity private keys, challenges, or proofs.

When **Write application log** is enabled, host-observed pairing-handshake and
authenticated-inactivity timeouts are written through the normal Application Log
pipeline with the transport (`direct` or `relay`) and configured timeout. A
browser-side startup timeout that occurs before any connection reaches the PC
cannot be written to the PC Application Log.
