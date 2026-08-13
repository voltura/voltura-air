# Phone-as-Webcam feasibility spike

Isolated Windows 11/iPhone browser spike. Its reviewed virtual-camera native source
now lives under the production Phone-webcam owner and remains usable by this spike;
the spike executable, signaling, and web page are not referenced by production.

## Gate status

| Gate | Status | Evidence |
| --- | --- | --- |
| Synthetic virtual camera | Passed on 2026-08-13 | Windows enumerated `Voltura Air Webcam (Windows Virtual Camera)`; the installed FrameServer source authenticated to the spike host and received one NV12 1920×1080 frame of exactly 3,110,400 bytes. |
| Direct iPhone video | Live render passed on 2026-08-13 | iPhone Chrome established a host/host UDP route and sent H.264. After enforcing the actual WebRTC sender policy, retaining decoder configuration across recovery, limiting pending encoded video to one frame, and pacing the virtual camera at 30 fps, VLC rendered responsive live phone video with no noticeable lag in the real-device check. The numeric latency threshold has not been remeasured on this corrected path. |
| Relay iPhone video | Live render passed on 2026-08-13 | iPhone Chrome authenticated the existing host route, obtained the existing 15-minute TURN configuration, and rendered live phone video through the Windows virtual camera. The host offer and browser answer are both rejected unless every candidate is `typ relay`; the host bridge established its external connection to `turn.cloudflare.com` over TLS/TCP 443. Production Relay code, endpoints, quota policy, and service configuration were unchanged. |
| Desktop compatibility | Teams and Chrome passed on 2026-08-13; Edge pending | Chrome's WebRTC `getUserMedia` sample and current Teams both selected and rendered `Voltura Air Webcam`. Teams initially showed black while Chrome was actively consuming the camera, then rendered correctly after the Chrome consumer was closed and Teams reopened the camera. Sequential compatibility is proved; simultaneous multi-consumer use is not. |
| Explicit stop/start | Passed once on 2026-08-13 | Stopping and starting capture from the iPhone page restored live video in Teams without restarting the spike host. |
| Camera switching | Passed on 2026-08-13 | Every camera exposed by Chrome on an iPhone 17 Pro Max switched live during the Relay session and continued rendering automatically through the original peer and host. The page continued to report 1920×1080 at 30 fps. |
| Clean removal | Earlier implementation passed; corrected transaction pending real-device rerun | The original helper completed removal with `state=camera-removed`. Independent review then removed its global Frame Server stop and corrected file/COM rollback, existing-install handling, and standard-user SID ownership. Those corrected paths build cleanly but have not been rerun against the installed camera. |
| Numeric benchmark | Pending | Earlier benchmark results describe the superseded sender and unpaced-camera implementation and are not evidence for the corrected path. The corrected path has not been remeasured against the numeric latency threshold. |

The spike establishes Direct and Relay live rendering from iPhone Chrome through the
current-user Windows virtual camera into VLC, Chrome, and Teams. The numeric ≤300 ms
requirement and Edge compatibility remain unproven.

## Build

Run these from the repository root:

```powershell
dotnet restore apps/webrtc-spike-host/WebRtcSpike.Host.csproj
dotnet build apps/webrtc-spike-host/WebRtcSpike.Host.csproj -c Release --no-restore
dotnet test apps/webrtc-spike-host/tests/WebRtcSpike.Tests.csproj -c Release
pwsh scripts/build-phone-webcam-native.ps1
```

The native source is the synthetic-source portion of Microsoft's Windows Camera
virtual-camera sample, retained under its MIT notice in
`../windows-host/Features/PhoneWebcam/Native/MICROSOFT-WINDOWS-CAMERA-LICENSE.txt`.
It exposes only NV12 1920×1080/30.
IPC version 1 carries a monotonic host frame sequence plus the source's wrapping
90 kHz RTP timestamp; the RTP value is not an absolute capture clock and is not used
as an end-to-end latency timestamp.

## Install, status, probe, remove

Only the helper requests elevation. Camera creation/removal runs as the current user;
elevation is limited to writing the helper's embedded, verified media source under
`C:\Program Files\Voltura Air Webcam` and registering its COM source in HKLM. The
helper holds a non-replaceable read handle to its own executable across UAC consent;
the elevated process never trusts a sibling DLL from the user-writable app directory.

```powershell
$setup = 'artifacts\native\PhoneWebcam\VolturaAir.WebcamSetup.exe'
& $setup install
& $setup status
& $setup probe
& $setup remove
```

The isolated host-to-camera pipe gate can be run without a phone or browser. Start
the host in one terminal, then run `probe` from another:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj -- --pipe-test
& $setup probe
```

Installation states are `files-copied`, `com-registered`, and `camera-created`.
Installation refuses to overwrite any existing file or registration. If `status`
reports `update-required`, remove and enable the feature again so the new embedded
payload is installed. Removal reverses
the states without stopping the shared Windows Frame Server service. It first attempts
to stage the DLL; if the DLL is in use, the complete registration and files remain.
If a later removal step fails, the staged DLL and COM registration are restored before
the current-user camera is recreated. Close all camera consumers before retrying.
Never manually delete only one of the three states.

The removal transaction can exercise recovery immediately after the DLL is staged:

```powershell
$env:VOLTURA_WEBCAM_FAULT = 'remove-after-stage'
& $setup remove
Remove-Item Env:VOLTURA_WEBCAM_FAULT
& $setup remove
```

The first command must fail without deleting the staged DLL; the second removal must
recover that staged-only state and finish with neither installed nor staged file left.
`remove-after-camera-remove` simulates a cleanup-only shutdown failure after the
current-user camera has already been removed; system-file removal must still finish.
`unregister-after-delete` simulates an unregister failure after deleting the COM tree;
the helper must restore and verify the complete registration and installed DLL.
The bounded helper-level assertion runs that exact delete/fail/restore/verify sequence
against an installed spike camera:

```powershell
& $setup test-unregister-rollback
```

It succeeds only with `state=unregister-rollback-verified` after checking the owner
SID, DLL path, and threading model of the restored COM registration.

## Run Direct

Deploy the sibling `apps/secure-web-spike` first, stop the normal Voltura Air host, and
run:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj
```

For a local signaling-endpoint check:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj -- `
  --signal http://127.0.0.1:8080/signal.php
```

Open the printed fragment URL in the iPhone browser under test. The fragment has independent random
room and 256-bit AES-GCM key tokens. The server sees only ciphertext. Press **Allow camera access**,
choose a camera, press **Start webcam**, and record the capture settings (the current target is
1920×1080 at approximately 30 fps). Stop, page hiding, or track loss stops all camera tracks without creating a
new room, peer, or host process. Ctrl+C stops the host.

Record the actual iPhone model, iOS version, browser and browser version, selected candidate pair, Windows
Camera result, failure cases, and observed cleanup here before advancing to Relay.

## Direct latency benchmark

Do not open VLC for this run. Start the same host with its benchmark consumer:

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj -- --benchmark
```

Open the printed URL, prepare/select the phone camera, and start the webcam. A QR
pattern then opens on the PC. Point the phone camera at the entire pattern. The host
captures the virtual camera for ten seconds after the first valid timestamp and writes
`apps/webrtc-spike-host/artifacts/webcam-benchmark-direct.json` with frame count,
effective fps, p50/p95 latency, drops,
CPU, and peak memory. Frame count and effective fps describe distinct changing QR
sequence values decoded by the benchmark consumer; drops are gaps in that sequence.
Repeated virtual-camera samples are ignored. Exit code zero requires the decoded
source to be full HD (1920×1080 in either orientation), effective fps of at least 28,
for every accepted measured pattern, and p95 latency at or below 300 ms.

## Relay

```powershell
dotnet run --project apps/webrtc-spike-host/WebRtcSpike.Host.csproj -- --relay
```

This opens the existing current-user Relay identity and calls the existing TURN
endpoint. Relay is selected automatically and enforced on both peers only when usable credentials are returned.
The existing quota-derived 4/2 Mbps bitrate and TLS/TCP 443 bridge are reused without
new endpoints or constants. Finish within the returned credential lifetime; there is
no renewal or peer replacement.

## Remaining evidence

- Current Edge camera-selection/rendering smoke check. Windows Camera was unavailable
  on the test PC; VLC, the WebRTC camera sample in Chrome, and Teams are the successful
  desktop consumers used so far. Teams passed after Chrome released the camera;
  simultaneous multi-consumer use remains unproven.
- Two additional start/stop cycles, permission loss, mid-frame disconnect, host exit,
  consumer exit, and one clean install/remove run with the corrected transactional
  helper. One explicit stop/start cycle already
  restored live Teams video without restarting the host, and all cameras exposed by
  Chrome on the tested iPhone switched successfully on the original Relay peer.
- Switching from iPhone Chrome to Messenger long enough for iOS to suspend the page
  caused iOS to close the original Relay peer. Returning to Chrome did not resume it.
  The host remained alive and returned the virtual camera to its waiting frame, while
  the page stopped capture and correctly reported the terminal transport state. This is
  a failed background-recovery gate, not a Relay-rendering failure. Automatic peer
  replacement and renegotiation remain deliberately outside this spike; a production
  implementation must create a fresh peer and reconnect automatically when the PWA
  returns to the foreground.
- Android remains **not tested**.

The existing phone-input row in `docs/ideas.md` records the current feasibility result.
