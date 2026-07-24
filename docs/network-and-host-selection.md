# Network and host selection

Authority for adapter/port selection, saved PCs, host hints, and manual recovery.
Wire shape: [protocol](protocol.md). Failure UX:
[pairing feedback](pairing-feedback.md).

## Adapter

- Default: rank active private IPv4 LAN adapters above VPN/tunnel/virtual
  adapters.
- **Choose another adapter** saves adapter identity plus current IP, so DHCP
  address changes keep the selection.
- Missing saved adapter: use the recommended adapter and warn.
- Multiple adapters: neutral summary; chooser explains same-LAN requirement.
- VPN/virtual selection: reachability warning.
- Returning to automatic adapter selection does not change the port setting.

## Port

- Automatic mode reuses its available last-successful port; otherwise tries
  `51395`, then following ports.
- A non-preferred automatic port is shown with a warning; scan a new QR code
  after any port change.
- Manual mode requires a valid free port and never falls back.
- The collapsed header distinguishes active, unsaved, and
  saved-pending-restart ports without predicting automatic selection.
- Saving adapter/port persists all pending connection settings and restarts the
  host. Pending values never appear active.

## Manual mobile host

Accepted:

- `192.168.1.50:51395`
- `http://192.168.1.50:51395`
- full Voltura Air pairing link
- port resolved against the current page host

Host entries require HTTP/HTTPS, explicit valid port, and no credentials, path,
query, or fragment. Pairing links follow [protocol](protocol.md). Invalid input
stays editable and changes no active/saved profile.

A valid host is saved only after acceptance. A pairing link opens device-name
confirmation and keeps its token semantics. **Forget** removes a saved profile.
Missing input acknowledgements or health failure enters unavailable/retrying.

Changed selection, fallback, validation, persistence, and recovery use the
[network/boundary validation route](setup.md#validation-by-change).
