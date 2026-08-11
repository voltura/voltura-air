# Network and host selection

Authority for adapter/port selection, saved PCs, host hints, and manual recovery.
Wire shape: [protocol](protocol.md). Failure UX:
[pairing feedback](pairing-feedback.md).

## Connection method

- **Direct local network** is the default and retains adapter, port, listener,
  host-served PWA, and direct pairing behavior.
- **Cloud relay through Voltura** binds the local listener to loopback and opens
  an authenticated outbound relay connection. It hides adapter/port controls
  and never opens Direct automatically after failure.
- Changing either method uses the existing save/restart/rollback flow.
- Relay retains its persistent opaque route and paired devices when disabled.
  An optional custom endpoint is HTTPS-only and bounded to 512 characters.
- Existing settings without a connection method normalize to Direct.
- **Enhanced capabilities** is a default-off Direct preference. With Direct, it
  makes `/s` the primary QR while retaining the local listener, `/ws`, and a
  Standard Local link using the same active token. Internet is required to load
  the hosted app and finish signaling; established controller traffic stays on
  the selected private IPv4 LAN. Relay always includes enhanced capabilities
  because `/a` already loads the secure hosted app; the saved Direct preference
  is retained but does not alter Relay. `/a` and the existing Relay transport
  remain unchanged. There is no probing, fallback, or automatic transport
  switching.

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

The host identity is not a routing value and never appears in the pairing URL
or saved-host address. A fresh short QR supplies one bootstrap token; the opened
authenticated pairing exchange pins the PC public identity alongside the saved
profile. A missing or mismatched pin requires a fresh scan of that PC rather
than adding identity data to future QR codes.

Relay profiles save service ID, opaque route, endpoint type, and pinned host
identity. The official endpoint comes from `relay-service.json`; custom
endpoints use the same protocol. Moving service infrastructure behind the same
hostname needs no profile change. A hostname change requires a new QR/profile
update, not different host or mobile code.

Official `/a` and `/s` profiles for the same route share the hosted profile ID,
client ID, reconnect key, host pin, permissions, and display name; the opened
path selects the transport. Local HTTP profiles and browser storage are not
copied, linked, migrated, invalidated, or deleted when enhanced capabilities
changes.

Changed selection, fallback, validation, persistence, and recovery use the
[network/boundary validation route](setup.md#validation-by-change).
