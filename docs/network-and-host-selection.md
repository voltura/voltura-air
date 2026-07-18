# Network and host selection

This document defines network-adapter selection, port selection, host hints,
saved PC profiles, and manual connection recovery. Wire shapes are in
`protocol.md`; connection failure presentation is in `pairing-feedback.md`.

Voltura Air is local-first. The Windows host advertises a LAN origin to phones,
tablets, and browsers through the QR pairing link. The mobile app connects to
that origin over WebSocket `/ws`.

## Windows host behavior

- Automatic network mode inspects active private IPv4 adapters and ranks likely real LAN adapters above VPN, tunnel, and virtual adapters.
- Manual network mode saves the selected adapter identity as well as the selected IP address. This lets the same adapter be reselected if DHCP later gives it a different address.
- If the saved adapter is unavailable, the host falls back to the recommended adapter and shows a warning instead of advertising a stale address.
- With multiple adapters, the host shows the selected adapter and guidance to
  choose the phone's Wi-Fi/LAN adapter when pairing fails.
- VPN and virtual adapters show a reachability warning.

## Port behavior

- Automatic port mode reuses the last successful automatic port when it remains
  available. Without a usable saved port, selection starts at the preferred
  Voltura Air port `51395` and tries the next ports.
- The host exposes the actual selected port. It warns when automatic selection
  has to choose a new non-preferred port; after any port change, scan a fresh QR
  code.
- Manual port mode does not fall back. An invalid or occupied manual port shows
  a validation error.

## Mobile manual host behavior

Manual host entry recovers from changed IPs or ports and stale QR pages.

The mobile app accepts:

- `192.168.1.50:51395`
- `http://192.168.1.50:51395`
- a full Voltura Air pairing link
- a port number that resolves against the current host

Host entries require HTTP or HTTPS, a valid explicit port, and no credentials,
path, query, or fragment. Pairing links must satisfy the generated-link contract
in [protocol.md](protocol.md). Invalid input remains in the field with a specific
validation message and does not change the active or saved PC profile.

A valid host entry starts connection recovery and is saved only after the host
accepts it. A valid pairing link opens the existing device-name confirmation and
uses its pairing token; it is not reduced to a host-only connection. **Forget**
removes a saved profile.

Missing advertised input acknowledgements move the app to
unavailable/retrying, as do health-check failures.

## Testing focus

- Adapter identity survives DHCP IP change.
- Missing saved adapter falls back with a warning.
- Multiple adapters produce user guidance.
- VPN or virtual adapter selection produces a warning.
- With no usable saved automatic port, an occupied preferred port selects the
  next automatic port and exposes a warning.
- Manual port occupied remains a validation error.
- Manual host entry creates/selects a saved PC profile.
- PC host closed after QR page loaded becomes `host-unreachable` with retry and recovery actions.
