# Security Policy

## Supported versions

Security reports should target the latest public release and the current `main` branch.

Older releases may receive fixes only when the issue is severe and a safe patch is practical.

## Reporting a vulnerability

Voltura Air receives input from a paired phone, tablet, or browser and injects that input into Windows. Security issues should be reported carefully.

Do not publish exploit details in a public issue.

Preferred reporting path:

1. Use GitHub private vulnerability reporting if it is enabled for this repository.
2. If private vulnerability reporting is not available, contact Voltura AB through an available private channel.
3. If no private channel is available, open a minimal public issue asking for maintainer contact. Do not include reproduction details, exploit code, tokens, device secrets, screenshots with private network information, or sensitive logs.

Please include, when safe to share privately:

- Affected Voltura Air version or commit.
- Windows version and browser/device used.
- Clear reproduction steps.
- Expected impact.
- Whether the issue requires local network access, a paired device, or physical access to the PC.

## Security boundaries

Voltura Air is designed for trusted devices on the same local network. It is not
intended to expose PC control over the public internet.

The Windows host serves the mobile web app over HTTP on the local network. This
keeps setup simple for browsers and phones on the same LAN, but it also means
the local network is part of the trust boundary: other software or devices able
to observe, interfere with, or reach local network traffic may affect pairing,
connection, and command delivery. Pair only on networks you trust, keep stale
devices removed, and do not use Voltura Air on hostile or untrusted Wi-Fi.

Voltura Air protects access with short-lived pairing tokens, P-256
proof-of-possession reconnects, per-device permissions, bounded protocol
messages, and recoverable command denials. The browser keeps the private
reconnect key locally; the Windows host stores only that reconnect key's public
half. The host separately owns a persistent PC identity private key in the
signed-in user's Windows key store, and paired browsers pin its public half. These
controls do not make it a sandbox against malware already running as the same
Windows user; same-user software can generally act with that user's privileges.

Fresh pairing keeps the QR short: it contains one short-lived token, version,
and optional routing hint, not a PC identity key or fingerprint. After opening,
the token authenticates a challenge-response exchange that pins the host's
persistent P-256 public identity and registers the browser's reconnect public
key without transmitting the token on the WebSocket. A saved client without a
valid PC identity pin must pair again.

Optional Screen viewing negotiates a direct LAN WebRTC peer through the
authenticated `/ws` control session. The reconnect key signs the start request
and answer; the pinned PC identity signs the exact offer hash. Invalid,
mismatched, or expired signaling is rejected before capture begins. Screen
video uses DTLS-SRTP and cursor/status records use a DTLS-protected data channel,
which provide confidentiality, integrity, and replay protection in transit.
The HTTP app/signaling metadata and existing JSON command traffic retain the
trusted-LAN threat model described above.

The public Custom screens community library is a separate internet-facing
service. Its account does not authorize access to a Windows host. Treat every
downloaded `.volturascreen` package as untrusted: Voltura Air accepts catalog
imports only from the official HTTPS origin, bounds and validates the package,
shows a local review, removes device assignments, and generates new local IDs
before saving. Imported application actions may refer to approved applications
that exist only on the author's PC and remain subject to local host permissions.

When testing or deploying:

- Download only from the official product page or official GitHub releases.
- Pair only devices you trust.
- Remove stale paired devices from the Windows host Settings Devices page.
- Do not forward the Voltura Air host port from your router to the internet.
- Review a Custom screen's panels and action summary before importing it, even
  when it came from the moderated community library.
