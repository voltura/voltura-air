# Troubleshooting

This page covers common connection, pairing, input, Windows-action, and recovery
problems.

## Copy diagnostics first

Use **Copy diagnostics** before changing many settings. Host diagnostics include
the host version, selected adapter/IP/port, pairing state, last error, and paired
and connected device counts. Mobile diagnostics include the web-client version,
active host, connection state, last error, browser, display mode, and timestamp.

Diagnostics omit credentials and client identifiers but can contain device
names, local addresses and paths, adapter details, and browser information.
Review them before posting publicly.

## Voltura Air could not start

The startup window grows only when it needs to show an error and keeps its
actions visible below the error content. If cursor recovery is enabled but its
native watchdog cannot start, choose **Disable watchdog and restart** to turn off
that user setting and retry startup. Reinstall Voltura Air to restore a missing
or damaged watchdog, then re-enable it in Preferences.

For another startup error, choose **Copy details** before closing Voltura Air.

## Phone or tablet cannot reach the PC

Check these first:

1. Voltura Air is running on the Windows PC.
2. The phone or tablet is on the same Wi-Fi/LAN as the PC.
3. The phone is not using mobile data for the browser session.
4. Windows Firewall allows Voltura Air on private networks.
5. The QR code was generated after the current network/IP/port was selected.

If the PC changed IP address or automatic port, click **New code** on the PC and scan again. You can also use **Enter host manually** on the mobile pairing screen or in mobile Menu > Settings > Connection.

A valid new QR photo opens **Confirm this device**. If the unavailable screen
returns instead, refresh the mobile app from the PC and scan the latest code.

## QR code expired, already used, or invalid

Pairing QR codes are short-lived and single-use. Click **New code** on the PC and scan the latest QR code. Avoid using a QR page that was left open before the PC changed network or port.
The PC Connect screen shows when its code will refresh and replaces it
automatically before expiry; a scan already in progress has a brief overlap in
which the immediately previous code remains valid.

## Too many pairing attempts

The Windows host temporarily rate-limits repeated failed unauthenticated pairing
attempts from the same remote address. Wait a moment, click **New code**, and
scan again. Successful fresh pairing and valid signed reconnects are not counted
as failures.

## Wrong network adapter selected

Open **Connection** in the Windows host and choose the adapter that is on the same Wi-Fi/LAN as the phone or tablet. Avoid VPN, tunnel, and virtual adapters unless that is intentionally the reachable network.

If DHCP changed the selected adapter's IP address, Voltura Air follows the saved
adapter identity and advertises its new address. If the saved adapter is missing,
it falls back to the recommended adapter and shows a warning.

## Automatic port changed

Automatic mode keeps its last successful port when that port remains available.
Without a usable saved port, it starts at `51395` and selects the next available
port when needed. The host shows the actual port and warns when it selects a new
non-preferred port; scan a fresh QR code after any port change.

Manual port mode does not silently fall back. If the chosen manual port is occupied, choose another port or return to automatic mode.

## Device was disconnected or revoked

If the PC says the device was disconnected, its registered reconnect public key
was removed. Scan a fresh QR code to create and register a new key pair.

## Pairing request invalid

If the PC reports an invalid pairing request, refresh the mobile app from the PC
and scan a fresh QR code.

## What to include in a bug report

Include:

- copied diagnostics from the Windows host;
- copied diagnostics from the mobile app;
- whether both devices are on the same Wi-Fi/LAN;
- whether a VPN, guest Wi-Fi, or mobile data is involved;
- what changed recently: network, firewall, IP address, port, browser, or app version.

Do not include screenshots or text containing live pairing tokens, private
reconnect keys, reconnect challenges, or reconnect proofs.

## Connected but input does nothing

If input acknowledgements stop, the mobile app shows unavailable/retrying and
reconnects. A rejected Windows input action reports a failure without closing
the authenticated connection.

Check these items first:

- Voltura Air is still running on the PC.
- The PC is not on a secure desktop, UAC prompt, lock screen, or other Windows
  surface that rejects normal input injection.
- The phone did not switch network, sleep the browser, or lose LAN reachability.
- If browser storage was cleaned, scan a fresh QR code; refreshing cannot restore
  a removed private reconnect key.

When a higher-integrity application blocks input, the host shows **PC input
paused** and the phone shows **Administrator app active**. Use **Show desktop**
or return to a normal application. Voltura Air cannot control UAC prompts, the
lock screen, or another secure desktop.

## Custom pointer remains after Voltura Air closes

Keep **Preferences > Custom pointer > Use cursor recovery watchdog** selected.
Reopen Voltura Air and switch **Custom pointer** off to recover, or reload the
selected pointer scheme from Windows Mouse settings.

## Pointer movement feels delayed or continues after touch ends

The client drops movement that would arrive late. Continued movement after
release can indicate an outdated or stalled host, heavy PC load, or an
interrupted LAN connection.

Confirm that the phone loaded the web client served by the currently running
host, then restart the host and refresh the mobile page. Check Wi-Fi signal,
guest-network isolation, VPNs, and PC load. Application logging is off by
default and does not record pointer movement.

## Send text to PC fails or goes to the wrong place

Check **Preferences > Text destination** on the PC. The default is the Windows
application that owns keyboard focus when delivery begins; click the intended
field, cell, document, or insertion point immediately before sending. Clipboard
mode intentionally copies only. Managed destinations create a new item or draft;
paste-driven destinations paste only after Voltura Air confirms that exact
window is foreground and not elevated. If the mobile result says the text was
copied, open the destination and paste manually; do not resend until you have
checked the clipboard result.

Text transfer is blocked while the Voltura Air host window, Windows lock screen,
a secure desktop, or a higher-integrity application has focus. The mobile draft
remains after failure. Check for partial text before retrying.

## An application button is missing or fails

Open **Preferences > Application launch buttons** on the Windows host. Only
enabled presets and locally approved custom buttons are sent to the phone. Also
check **Preferences > Global permissions** and the paired device's permission
override for **Allow paired devices to start applications**. When that effective
permission is off, the host does not advertise any application buttons.

Spotify, VLC, and PowerPoint presets require the corresponding application to
be installed where Voltura Air can discover it through Windows registration or
a supported install location. A custom button must point to an existing absolute
`.exe` path. If that file is moved or removed after approval, the host rejects
the launch until the entry is edited and approved again.
Arguments are passed directly to the selected executable; command shells,
scripts, relative paths, and paths received from phones are not supported.

The mobile Fn panel reports launch failures without disconnecting. Enable
**Write application log** in **Preferences > Application** or
**Diagnostics > Application log** for the action ID and outcome.

## Presentation controls are disabled or affect the wrong app

Enable **Preferences > Developer tools > Enable alpha features**, then enable
**Allow paired devices to control presentations** globally or for the selected
device. **Blackout** also requires its separate permission.

Keep the intended slideshow or viewer focused and choose its matching target on
the phone. Google Slides must already be presenting. After a disconnect, wait
for reconnection and press the intended control again.

## Lock PC is disabled or fails

Open **Preferences > Developer tools > Windows locking**. Use **Test Lock PC**
when available. If Windows explicitly disables locking, use **Enable Windows
locking** and confirm the local change. A protected policy reports an error.

Also check the global and per-device **Lock PC** permission. A lock failure
leaves the connection active.

For details, enable **Write application log** in
**Preferences > Application** or **Diagnostics > Application log** before
testing. The Application log records the policy check and action result.

## Turn off display looks disconnected

**Turn off display** cuts all display output, including HDMI. Some PCs enter
sleep or Modern Standby, making Voltura Air unavailable. Use a physical keyboard
or mouse to wake the PC. If logging was enabled, the Application log records the
request result.

If the display wakes to fingerprint or PIN, Windows locked the existing session
according to its sign-in policy; Voltura Air did not sign the user out. Running
apps should still be present after authentication.

## Keep awake does not stay active

Open **Preferences > Keep awake** and confirm the current mode and status. Off
uses the selected Windows power plan; interval and expiration modes return to
Off when their deadline passes. Exiting Voltura Air also releases the request.

Keep awake prevents idle sleep only while Voltura Air runs in the signed-in
user session. It does not override choosing Sleep, closing a laptop lid, a
power-button action, or Windows lock-screen behavior. **Keep screen on** adds a
display requirement and uses more power, but it remains a host-only setting.
If the mobile Keep awake row is disabled, enable **Allow paired devices to
control Keep awake** globally or for that device. Enable the application log
and inspect Diagnostics for `keep_awake`, `awake.set`, or a
`VAIR-AWAKE-EXECUTION-FAILED` result when Windows rejects a request.
