# WebRTC spike host

Disposable Windows 11 console offerer for the secure-context + direct-LAN WebRTC
feasibility spike. It is not referenced by the Voltura Air solution, host, workspaces,
installer, or release flow.

## Run

Deploy the sibling `apps/secure-web-spike` files first, then run:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj
```

The default signaling endpoint is `https://voltura.se/spike/signal.php`. For a local
signaling-endpoint check only, override it with:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj -- --signal http://127.0.0.1:8080/signal.php
```

The host creates one high-entropy room, uploads a complete SDP offer, prints the Safari
URL, polls for one complete answer, and then stops using signaling. No STUN or TURN
server is configured. The copied `datachannel.dll` comes from the existing production
host binary but the spike does not reference production source or projects.

If Windows Firewall prompts, allow this executable on **Private networks only**. The
spike never changes firewall rules itself.

Once connected, the console prints the selected local/remote addresses and ICE
candidates, normal test messages, and motion/orientation values. After the DataChannel
has opened, backgrounding or closing Safari may end the browser session but does not
terminate the spike host. Press Ctrl+C to stop the host.

## Hardware test

Use a current iPhone with Safari on the same Wi-Fi/LAN as the Windows 11 PC:

1. Open the printed URL and confirm `Secure context` is `true`.
2. Wait for `DataChannel: open` on both devices.
3. Press **Send test message** and verify the console receives it.
4. Press **Enable motion sensors**, grant permission, move the phone, and verify the
   console prints changing values.
5. Record the candidate pair shown by the page and host. It must be a host/local path,
   not `relay`.
6. Preferred proof: after connection, remove WAN access while retaining the LAN/Wi-Fi;
   messages and sensor updates must continue.

Android is not part of this spike's pass criteria and must be recorded as **not tested —
hardware unavailable**.
