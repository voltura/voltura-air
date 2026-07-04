# Troubleshooting

This page covers the most common Voltura Air connection and pairing failures.

## Copy diagnostics first

Use **Copy diagnostics** before changing many settings. Diagnostics are designed for support and issue reports. They include version, selected host/IP/port, connection state, last error, connected-device counts, and browser information.

Diagnostics must not include pairing secrets, pairing tokens, device tokens, or secret hashes. If a copied diagnostic ever contains a value that looks like a live token or secret, do not paste it publicly.

## Phone or tablet cannot reach the PC

Check these first:

1. Voltura Air is running on the Windows PC.
2. The phone or tablet is on the same Wi-Fi/LAN as the PC.
3. The phone is not using mobile data for the browser session.
4. Windows Firewall allows Voltura Air on private networks.
5. The QR code was generated after the current network/IP/port was selected.

If the PC changed IP address or automatic port, click **New code** on the PC and scan again. You can also use **Enter host manually** on the mobile pairing screen or in mobile Settings.

## QR code expired, already used, or invalid

Pairing QR codes are short-lived and single-use. Click **New code** on the PC and scan the latest QR code. Avoid using a QR page that was left open before the PC changed network or port.

## Too many pairing attempts

The Windows host temporarily rate-limits repeated failed unauthenticated pairing
attempts from the same remote address. Wait a moment, click **New code**, and
scan again. Successful fresh pairing and saved-secret reconnects are not counted
as failures.

## Wrong network adapter selected

Open **Connection** in the Windows host and choose the adapter that is on the same Wi-Fi/LAN as the phone or tablet. Avoid VPN, tunnel, and virtual adapters unless that is intentionally the reachable network.

If the selected adapter was saved before DHCP changed the IP address, Voltura Air should follow the saved adapter identity and advertise the new address. If the saved adapter is missing, it falls back to the recommended adapter and shows a warning.

## Automatic port changed

Voltura Air prefers port `51395`. If that port is occupied, automatic mode selects the next available port and shows the actual selected port. Scan a fresh QR code after a port change.

Manual port mode does not silently fall back. If the chosen manual port is occupied, choose another port or return to automatic mode.

## Device was disconnected or revoked

If the PC says the device was disconnected, the stored mobile credential is no longer valid. Scan a fresh QR code to pair the device again.

## App version mismatch

If the mobile app and Windows host report a protocol/version mismatch, refresh the mobile app from the PC and scan a fresh QR code. If the mobile app was installed to the home screen, use **Refresh app** in mobile Settings.

## Pairing request invalid

If the PC reports an invalid pairing request, refresh the mobile app from the PC
and scan a fresh QR code. The host rejects malformed `pair.hello` messages and
closes unknown or malformed authenticated messages before dispatching input.

## What to include in a bug report

Include:

- copied diagnostics from the Windows host;
- copied diagnostics from the mobile app;
- whether both devices are on the same Wi-Fi/LAN;
- whether a VPN, guest Wi-Fi, or mobile data is involved;
- what changed recently: network, firewall, IP address, port, browser, or app version.

Do not include screenshots or text containing live pairing tokens, secrets, or secret hashes.
