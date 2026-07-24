# Troubleshooting

## Copy diagnostics first

Copy host and mobile diagnostics before changing several settings. They omit
credentials/client IDs but may contain device names, local addresses/paths,
adapter details, and browser information; review before sharing publicly.

## Voltura Air could not start

For a cursor-watchdog error, choose **Disable watchdog and restart**. Reinstall
to restore a missing/damaged watchdog before re-enabling it. For other startup
errors, choose **Copy details** before closing.

## Device cannot reach the PC

Confirm:

1. The Windows host is running.
2. Both devices use the same Wi-Fi/LAN; mobile data is not carrying the browser.
3. Windows Firewall allows Voltura Air on private networks.
4. The QR code matches the active adapter/IP/port.

After an address/port change, click **New code** and rescan or use
**Enter host manually**. If a valid scan returns to unavailable, refresh the
mobile app from the PC and scan the latest code.

## QR code expired, used, or invalid

Click **New code** and scan it. Codes are short-lived/single-use; avoid QR pages
opened before a network or port change.

## Too many pairing attempts

Wait briefly, click **New code**, and scan again. The host temporarily limits
repeated failed unauthenticated attempts from one address.

## Wrong adapter or port

In Windows **Connection**, choose an adapter on the device's Wi-Fi/LAN; avoid
VPN/tunnel/virtual adapters unless intentionally reachable. Apply with
**Save and restart** or cancel with **Discard changes**.

After any automatic port change, scan a fresh QR code. For an occupied custom
port, choose another or return to automatic, then **Save and restart**.

## Device revoked

Removal deletes its registered reconnect public key. Scan a fresh QR code to
pair again.

## Pairing request invalid

Refresh the mobile app from the PC and scan a fresh QR code.

## Bug report contents

Include host/mobile diagnostics, LAN/VPN/guest-network/mobile-data context, and
relevant network, firewall, address, port, browser, or version changes. Never
include live pairing links/tokens, private reconnect keys, challenges, or
proofs.

## Connected but input does nothing

Check that:

- the host still runs;
- Windows is not showing UAC, secure desktop, lock screen, or another
  higher-integrity surface;
- the phone retained LAN reachability and foreground browser state;
- browser storage still contains its key; otherwise pair again.

**PC input paused** / **Administrator app active** means a higher-integrity app
blocks injection. Use **Show desktop** or focus a normal app. Input rejection
does not close the connection; lost acknowledgements enter
unavailable/retrying.

## Custom pointer remains

Reopen Voltura Air, which reloads the configured Windows cursor scheme, then
disable **Custom pointer**. You can also reload the chosen scheme in Windows
Mouse settings.

## Pointer is delayed or continues after release

Restart the host, refresh the mobile page from that host, and check Wi-Fi,
guest-network isolation, VPNs, and PC load. Application logging never records
pointer movement.

## Send text fails or targets the wrong place

Check **Preferences > Text destination** and focus the intended Windows field
immediately before sending. Clipboard mode only copies. If the result says
copied, paste manually and inspect it before retrying.

Host-UI focus, lock/secure desktop, or a higher-integrity target blocks text
delivery. The mobile draft survives failure; check for partial text before
retry.

## Application button missing or failing

Check **Preferences > Application launch buttons**, global launch permission,
and the device override. Disabled permission advertises no buttons.

Presets require a discoverable installed app. Custom buttons require an existing
absolute `.exe`; edit/reapprove moved files. Shells, scripts, relative paths,
and phone-supplied paths are unsupported. The mobile Fn panel reports failure;
optional Application log records action ID/outcome.

## Presentation disabled or controls wrong app

Enable **Preferences > Developer tools > Enable alpha features** and the global/
device Presentation permission. Blackout has a separate permission. Focus the
intended viewer, select its matching target, and start Google Slides presenting
before sending controls.

## Lock PC disabled or failing

In **Developer tools > Windows locking**, use **Test Lock PC**. If permitted,
**Enable Windows locking** can clear an explicit current-user block. Also check
global/device Lock permission. A protected policy reports failure without
closing the connection.

## Turn off display looks disconnected

Display off includes HDMI and may enter sleep/Modern Standby. Wake with physical
keyboard/mouse. A PIN/fingerprint screen reflects Windows sign-in policy;
Voltura Air did not sign out.

## Keep awake ends

Check **Preferences > Keep awake** mode/deadline. Timed modes return to Off;
exiting releases the request. It prevents idle sleep only while the signed-in
host runs and cannot override manual Sleep, lid close, power button, or
lock-screen policy.

**Keep screen on** is host-only and uses more power. For disabled mobile control,
enable global/device Keep awake permission. Optional Diagnostics records
`keep_awake`, `awake.set`, and `VAIR-AWAKE-EXECUTION-FAILED`.
