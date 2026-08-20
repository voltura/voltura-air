# Voltura Air TODO

Approved unfinished work is ordered here. Current behavior belongs in
[features](features.md); possible directions belong in [ideas](ideas.md).

## Priority

1. Release-blocking correctness, security, connection, input, data-loss,
   recovery, and resource-lifetime defects.
2. Work promoted from `ideas.md` after its outcome, priority, ownership, and
   validation boundary are decided.

## Phone webcam physical acceptance

- On physical Windows 11, validate video-only and optional-audio sessions through
  Enhanced Direct and Relay, with the base VB-CABLE device installed by the user.
- Select `CABLE Output` in Teams and a browser and confirm audible output, browser-local
  Mute, restart behavior, endpoint-loss termination, and video-only regression behavior.
- Confirm every terminal path releases phone camera and microphone tracks, WebRTC,
  bounded decode queues, WASAPI, the virtual-camera frame pipe, and native resources.
- Inspect application logs and confirm they contain no captured video, audio, SDP,
  device identifiers, endpoint names, levels, credentials, or proofs.
- Complete representative visual acceptance for the phone workspace, Windows Phone
  webcam page, and installer finish states before publication.
