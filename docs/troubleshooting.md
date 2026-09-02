# Troubleshooting

## Copy diagnostics first

Copy host and mobile diagnostics before changing several settings. They omit
credentials/client IDs but may contain device names, local addresses/paths,
adapter details, and browser information; review before sharing publicly.

## Voltura Air could not start

Choose **Copy details** before closing.

## Add, repair, or remove Phone Webcam

Open Windows **Installed apps**, choose **Voltura Air → Modify**, then check Phone
Webcam to install or repair it, or uncheck it to remove it. This reuses the retained
Voltura Air installer and leaves the main application installed.

## Phone microphone is unavailable

Open **Phone webcam** in the Windows app and select **Check again**. Optional phone
audio requires the base VB-CABLE device. VB-CABLE is third-party donationware, is
not included or distributed with Voltura Air, and must be obtained directly from
[VB-Audio](https://vb-audio.com/Cable/) under the licence applicable to your use.

If Voltura Air reports that VB-CABLE is installed but unavailable, enable `CABLE
Input` in Windows Sound settings or restart Windows, then check again. In Teams or
the receiving browser/app, choose `CABLE Output` as the microphone. Detection
failure does not prove absence and does not open a website automatically.

During an active Phone webcam session started with **Use microphone**, choose
**Test audio** on the Windows Phone webcam page to monitor `CABLE Output` through
the default speakers. Keep the phone away from the speakers to avoid feedback.
If the button is absent, confirm the phone session is actively streaming with its
microphone enabled. If testing reports that the default output is VB-CABLE, choose
speakers or headphones as the default Windows output and try again.

## PC sound is unavailable in View PC screen

Video remains available if PC sound cannot start or stops. Confirm that Windows
has an enabled default output device for multimedia, then choose or reconnect
speakers or headphones in Windows Sound settings. Voltura Air follows a later
default-output change automatically; you do not need to stop the screen view.

Each new screen-view connection starts muted on the phone, tablet, or browser.
Choose **Sound** in the live view to begin playback. If the browser blocks
playback, choose **Sound** again. This control changes only playback on the viewing
device; it does not mute or change volume on the PC.

If sound breaks up or loses detail while video remains usable, choose **Standard**
or **Low** under the paired device's **Menu → Settings → Screen viewing**. Low
uses mono and the least network capacity; Standard keeps stereo with lower use;
High keeps the best detail. The PC default is under **Preferences → Screen
viewing**, and Windows **Devices → Screen viewing** can override it for one
pairing. Changing the choice does not restart the screen view, and returning to
**Use PC default** resumes the PC setting.

## Device cannot reach the PC

Confirm:

1. The Windows host is running.
2. Both devices use the same Wi-Fi/LAN; mobile data is not carrying the browser.
3. Windows Firewall allows Voltura Air on private networks.
4. The QR code matches the active adapter/IP/port.

After an address/port change, click **New code** and rescan or use
**Enter host manually**. If a valid scan returns to unavailable, refresh the
mobile app from the PC and scan the latest code.

## Secure Direct unavailable

Enhanced device features require internet access to load the hosted controller and
complete setup, plus a private IPv4 path between the device and the adapter
selected on the PC. Keep both devices on the same LAN and check captive portals,
guest-network isolation, VPNs, and the selected adapter.

Retry the current Secure Direct PC first. If setup still fails, use the recovery
offered by the mobile app to pair explicitly through **Standard Local** or
**Cloud relay**. Voltura Air does not switch transports automatically. A lost
internet connection after the DataChannel is established should not end an
otherwise healthy Secure Direct session; copy diagnostics if it does.

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

## Gyro mouse is unavailable or does not move

Gyro requires a sensor-equipped phone or tablet using enhanced device features.
Open Gyro from its button so the browser can request motion access; on iPhone,
approve the motion/orientation prompt. If Retry does not show the prompt again in
a bookmarked Home Screen app, fully close and reopen Voltura Air, then try Gyro
again. If access remains denied, use the browser or device settings to restore it.

Keep the page visible and hold the Trackpad surface or a mouse button while
moving the device. Gyro deliberately stops on release, when the page is hidden,
or when the connection or Trackpad changes. If the UI reports no sensor data,
confirm behavior on a real device rather than desktop emulation and retain Touch
as the fallback.

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

Check the global and device Presentation permission. Blackout has a separate
permission. Focus the intended viewer, select its matching target, and start
Google Slides presenting before sending controls.

## Files on PC is unavailable or a folder will not open

Check **Browse and open files** globally and for the paired device. Copy, Move,
Paste, Rename, and Delete also need **Change files**. **View** additionally needs
the normal PC Screen permission and a current trusted pairing.

Windows can expose compatibility links and protected operating-system folders
that the signed-in user cannot enumerate. Voltura Air leaves the current panel
unchanged and reports the refusal instead of disconnecting. The recommended
**Hide protected operating system files and folders** setting is enabled by
default and can be overridden per device. Mapped drives must be available in
the same signed-in Windows session as the host.

If a directory changes between listing and an operation, Files refreshes it and
does not start a partial action. A canceled or failed operation keeps the
selection for retry; a completed selection-based operation clears it. Terminal
operation history can be removed from **File operations**.

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

## Simulated activity has no effect

Check **Preferences > Keep awake > Simulate activity every 59 seconds**. The
setting is host-only and sends one F15 key-up after each full interval; it does
not move the pointer, click, call a presence API, or override lock, sleep,
secure-desktop, integrity-level, or application-specific idle rules. An
application that handles F15 may react to it.

If Windows rejects a pulse, Voltura Air remains enabled, shows one tray warning
for that continuous failure streak, and retries after the next interval. Enable
optional application logging to record the failure and later recovery. Active
remote input takes priority: a coincident pulse is skipped without waiting or
building a queue.
