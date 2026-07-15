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

If the PC changed IP address or automatic port, click **New code** on the PC and scan again. You can also use **Enter host manually** on the mobile pairing screen or in mobile Menu > Settings > Connection.

After a valid new QR photo is read, the mobile app stops the unavailable PC retry and shows **Confirm this device**. The old PC remains saved. If the app returns directly to the unavailable screen instead, refresh the mobile app from the PC and retry with the latest code.

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

If the mobile app and Windows host report a protocol/version mismatch, refresh the mobile app from the PC and scan a fresh QR code. If the mobile app was installed to the home screen, use **Refresh app** in mobile Menu > Settings > App.

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


## Connected but input does nothing

When the host supports input acknowledgements, the mobile app adds sequence
numbers to discrete input and sampled pointer movement. The host confirms
dispatched input with `input.ack`. Movement behind an outstanding acknowledgement
and movement in a growing WebSocket send buffer are bounded and then dropped,
so a slow path cannot build a long pointer tail. If acknowledgements stop, or
Windows rejects injected input, the mobile app shows unavailable/retrying and
reconnects.

Check these items first:

- Voltura Air is still running on the PC.
- The PC is not on a secure desktop, UAC prompt, lock screen, or other Windows
  surface that rejects normal input injection.
- The phone did not switch network, sleep the browser, or lose LAN reachability.
- Refresh the mobile page or scan a fresh QR code if browser storage was cleaned.

Custom pointer applies across the Windows desktop. Switch it off in **Preferences > Custom pointer** or from a paired device to restore the configured Windows
cursor scheme immediately. The cursor watchdog also restores that scheme if the
host exits unexpectedly. The host shows **Remote control paused** once and the
phone shows **Administrator app active** when a higher-integrity foreground app
blocks injected input. Choose **Show desktop** on the phone
to minimize desktop windows through the Windows shell, or choose **Continue** to
return to the client controls. A compact recovery toast remains available to reopen
the dialog until a normal foreground application returns, when it clears automatically. Input to UAC prompts, the lock screen,
and other secure desktops remains outside host control.

## Pointer movement feels delayed or continues after touch ends

Voltura Air coalesces movement to browser animation frames and deliberately
drops new movement when acknowledgement or WebSocket congestion indicates that
it would arrive late. Normal pointer input also bypasses the WPF dispatcher
unless a Blackout display curtain is actually active. A continuing movement
tail therefore indicates a stalled/outdated host or a severely interrupted LAN
connection rather than motion that the current client will intentionally replay.

Confirm that the phone loaded the web client served by the currently running
host, then restart the host and refresh the mobile page. Check Wi-Fi signal,
guest-network isolation, VPNs, and PC load. Application logging is off by
default and pointer movement is never logged; large log reads do not hold the
protocol writer lock.

## Send text to PC fails or goes to the wrong place

Check **Preferences > Text destination** on the PC. The default is the Windows application that owns keyboard focus when delivery begins; click the intended field, cell, document, or insertion point immediately before sending. Clipboard mode intentionally copies only. Managed destinations make a new item and paste only after Voltura Air confirms that exact window is foreground and not elevated. If the mobile result says the text was copied, open the destination and paste manually; do not resend until you have checked the clipboard result.

Voltura Air deliberately refuses focused-app text transfer while its own protected host window has focus. It also cannot inject text into the Windows lock screen, a UAC/secure desktop, or an elevated application when the host is running without the matching elevation level. The mobile draft is retained after failure so you can correct the destination and explicitly retry; check for partial text before retrying because Windows can reject an input sequence after accepting part of it.

The optional Keyboard **Paste to PC** control uses the same destination and acknowledgement path. Long-press it and choose the device browser's native **Paste** action. Browser clipboard permission is not requested and Voltura Air never reads the clipboard in the background.

## An application button is missing or fails

Open **Preferences > Application launch buttons** on the Windows host. Only
enabled presets and locally approved custom buttons are sent to the phone. Also
check **Preferences > Global permissions** and the paired device's permission
override for **Allow paired devices to start applications**. When that effective
permission is off, the host does not advertise any application buttons.

Spotify, VLC, and PowerPoint presets require the corresponding application to
be installed and registered with Windows. A custom button must point to an
existing absolute `.exe` path. If that file is moved or removed after approval,
the host rejects the launch until the entry is edited and approved again.
Arguments are passed directly to the selected executable; command shells,
scripts, relative paths, and paths received from phones are not supported.

The mobile Fn panel reports permission, missing application, stale button,
invalid target, and Windows start failures without disconnecting. Enable
**Preferences > Write application log** for the opaque action ID and outcome.
Custom paths and arguments are excluded from the log. Launch result feedback
under the buttons clears automatically after four seconds.

## Lock PC is disabled or fails

Open the Windows host and go to **Preferences > Windows locking**. If the policy
value is missing or zero, select **Test Lock PC**; Voltura Air tests the native
Windows lock action without writing an unnecessary registry value. If the policy
explicitly disables locking, select **Enable Windows locking**. After local
confirmation, Voltura Air writes and reads back
`HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableLockWorkstation`
as a 64-bit-view `REG_DWORD` value of `0`, broadcasts a user-policy refresh, and
tests the native lock request. This normally needs no administrator rights or
UAC and does not read or alter automatic Windows sign-in. If Windows or an
administrator has made the current-user policy key read-only, Voltura Air
reports that the setting is protected rather than requesting elevation or
trying to override managed policy.

The mobile app distinguishes a host permission denial, an unsupported action,
a Windows policy that disables locking, an unavailable policy check, and a
native Windows failure. These failures leave the connection active so later
remote actions can still be attempted.

For an exact trace, enable **Preferences > Write application log** before
testing. **Diagnostics > Application log** then shows filterable remote-command
and Windows-host activity, including policy write/readback failures, the received
power command, action outcome, response, and Win32 error when one is available.
Logging is off by default, retains 2 days by default, and excludes typed text,
pointer coordinates, pairing tokens, and reconnect secrets. Diagnostics can copy
the filtered view, open `%APPDATA%\Voltura Air\Logs`, or delete the log files.

If the explicit DWORD zero reads back successfully but Lock, Win+L, and the
mobile action still fail, Windows management policy or another program may be
controlling the feature. Voltura Air does not modify machine policy or attempt
additional automatic repairs.

## Turn off display looks disconnected

**Turn off display** intentionally cuts all display output, including HDMI to a
TV or home-theater receiver. Some PCs treat the Windows `SC_MONITORPOWER`
command as sleep or Modern Standby. The host and network connection then
suspend, so the mobile client cannot send a wake command and will report the PC
unavailable after a prompt follow-up health check. Use a physical keyboard or mouse
to wake the PC. This is Windows and hardware behavior rather than a pairing or
firewall failure. Check the Application log for the accepted `displayOff`
command and Windows System events for Modern Standby reason `SC_MONITORPOWER`
when confirming this case.

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
and inspect Diagnostics for `keep_awake`, `awake.set`, or an
`VAIR-AWAKE-EXECUTION-FAILED` result when Windows rejects a request.
