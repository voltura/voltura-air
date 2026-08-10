# Secure web spike

Disposable, framework-free HTTPS page and PHP signaling endpoint for the
secure-context + direct-LAN WebRTC feasibility spike. It is intentionally outside the
Voltura Air site publisher and production client/relay flows.

## Manual deployment

Create the `/spike/` directory on `voltura.se` and upload exactly these four files from
this directory:

```text
index.html
app.js
style.css
signal.php
```

Do not upload this README and do not add the directory to the normal Voltura Air site
publisher. The page must be available as `https://voltura.se/spike/` and signaling as
`https://voltura.se/spike/signal.php`.

The PHP endpoint accepts only bounded JSON POST requests. It stores each offer/answer
in PHP's non-web-accessible temporary directory under a SHA-256 room filename, uses
file locks, expires rooms after five minutes, and deletes a room as soon as the Windows
host retrieves its answer. It never forwards DataChannel or sensor messages.

## Removal

After the feasibility result is recorded:

1. Delete the four deployed files and the `/spike/` web directory.
2. Temporary server-side room files expire after five minutes. If shell access is
   available, the hosting administrator may also delete the
   `voltura-air-webrtc-spike` child directory beneath PHP's configured temporary
   directory.
3. Delete the two isolated `apps/webrtc-spike-host` and `apps/secure-web-spike`
   repository folders in a separate cleanup change when the evidence is no longer
   needed.

## Expected evidence

The page displays secure-context support, browser and sensor API availability,
signaling/ICE/DataChannel states, the selected candidate pair from `getStats()`, last
sent/received messages, sensor values, and the number of throttled sensor updates sent.
Sensor transmission is capped at roughly 20 Hz. No STUN or TURN server is configured.
