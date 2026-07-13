# Manual network and host selection

Voltura Air is local-first. The Windows host advertises a LAN origin to phones, tablets, and browsers through the QR pairing link. The mobile app should connect to that origin over WebSocket `/ws`.

## Windows host behavior

- Automatic network mode inspects active private IPv4 adapters and ranks likely real LAN adapters above VPN, tunnel, and virtual adapters.
- Manual network mode saves the selected adapter identity as well as the selected IP address. This lets the same adapter be reselected if DHCP later gives it a different address.
- If the saved adapter is unavailable, the host falls back to the recommended adapter and shows a warning instead of advertising a stale address.
- If multiple adapters are available, the host should explain which adapter was selected and tell the user to choose the adapter on the same Wi-Fi/LAN as the phone if pairing fails.
- If the selected adapter looks like VPN or virtual networking, the host should warn that it may not be reachable from the phone or tablet.

## Port behavior

- Automatic port mode starts from the preferred Voltura Air port `51395`.
- If the preferred port is occupied, the host tries the next ports and exposes the actual selected port.
- If the automatic port changed, the host should show a warning and tell the user to scan a fresh QR code.
- Manual port mode does not silently fall back. If the chosen manual port is invalid or occupied, startup/settings validation should show a clear error.

## Mobile manual host behavior

Manual host entry is a recovery path for changed IPs, changed ports, guest Wi-Fi, mobile data, stale QR pages, and firewall/network mistakes.

The mobile app should accept:

- `192.168.1.50:51395`
- `http://192.168.1.50:51395`
- a full Voltura Air pairing link
- a port number that resolves against the current host

After a valid manual host is entered, the mobile app should create or update a saved PC profile, select it as active, and attempt to connect without navigating away from the app. Saved profiles should remain deletable through **Forget**.

A reachable WebSocket is not the only health signal. When the host advertises input acknowledgement support, missing input acknowledgements should move the app to unavailable/retrying just like health-check failures.

Turning off the PC display is a Windows power transition rather than a network-selection change. On some Modern Standby hardware, `SC_MONITORPOWER` suspends the host and its WebSocket as soon as video output stops. Voltura Air cannot reliably override or remotely wake that state, so the client requires confirmation, probes the host after one second, and uses the normal health deadline to report the PC unavailable instead of displaying an extended connected state.

## Testing focus

- Adapter identity survives DHCP IP change.
- Missing saved adapter falls back with a warning.
- Multiple adapters produce user guidance.
- VPN or virtual adapter selection produces a warning.
- Preferred port occupied selects the next automatic port and exposes a warning.
- Manual port occupied remains a validation error.
- Manual host entry creates/selects a saved PC profile.
- PC host closed after QR page loaded becomes `host-unreachable` with retry and recovery actions.
