# Secure webcam spike page

Disposable, framework-free HTTPS page plus bounded PHP signaling for the isolated
phone-as-webcam spike. It is outside normal Voltura Air publishing and production
client/Relay flows.

## Deploy

Upload exactly `index.html`, `capture-generation.js`, `submitted-peer.js`, `app.js`, `style.css`, and `signal.php` to an HTTPS
directory. Do not add this folder to the normal publisher. Camera capture requires a
secure browser context.

The printed URL fragment is `room.key`: both are independent 256-bit random values.
The page AES-GCM decrypts the host offer and encrypts its answer with the room as
authenticated data. PHP validates only bounded envelope fields, stores only
ciphertext, expires rooms after five minutes, consumes the offer once, and deletes the
room when the host consumes the answer.

The page contains only camera selection, preview, transport selection, and explicit
**Start webcam**/**Stop webcam** controls. It requests exact 1920×1080 and approximately
30 fps, negotiates H.264 only, and replaces a selected camera track on the original
peer. Stop, page hiding, and track loss release capture. Transport failure is reported
without reconnect, renegotiation, a new peer, or a new room.

Run `php tests/signal-store-test.php` to verify that an answer cannot be consumed
twice even when a second reader opened the state file before the first consumption.
Run `node tests/capture-generation.test.js` to verify that hiding the page invalidates
an in-flight capture start before it can activate a camera.
Run `node tests/submitted-peer.test.js` to verify that a hidden page retains an answer
which may already have committed while detaching its camera track.

## Remove

Delete the six deployed files and the web directory. Unconsumed PHP temporary room
files expire after five minutes; an administrator may also delete only the
`voltura-air-webrtc-spike` child of PHP's configured temporary directory.
