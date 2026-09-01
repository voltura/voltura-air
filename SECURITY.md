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

Direct LAN is designed for trusted devices on the same local network. Optional
Relay is an internet transport for paired devices; it does not turn the routing
service into a trusted command endpoint.

Optional Secure Direct loads the controller from the official HTTPS origin and
uses the relay service only for bounded one-off WebRTC signaling. The host
accepts the resulting controller channel only when libdatachannel proves that
the selected local address is the configured private IPv4 adapter and the
selected remote address is private IPv4. Public, loopback, wrong-interface,
malformed, or unverifiable selections fail closed. No STUN or TURN server is
configured for this controller transport. Existing
pairing/reconnect proofs, the pinned host identity, and per-device permissions
still authenticate every controller session; route possession alone grants no
command access.

Standard Local serves the mobile web app over HTTP on the local network. This
keeps setup simple for browsers and phones on the same LAN, but it also means
other software or devices able to observe, interfere with, or reach local
network traffic may affect pairing, connection, and command delivery. Enhanced
Direct loads the official HTTPS controller and protects established commands in
a DTLS DataChannel, but LAN reachability and network metadata remain inside the
trusted-local-network boundary. Pair only on networks you trust, keep stale
devices removed, and do not use Direct on hostile or untrusted Wi-Fi.

Voltura Air protects access with short-lived pairing tokens, P-256
proof-of-possession reconnects, per-device permissions, bounded protocol
messages, and recoverable command denials. The browser keeps the private
reconnect key locally; the Windows host stores only that reconnect key's public
half. The host separately owns a persistent PC identity private key in the
signed-in user's Windows key store, and paired browsers pin its public half. These
controls do not make it a sandbox against malware already running as the same
Windows user; same-user software can generally act with that user's privileges.

The optional Files tool has separate browse/open, change, and transfer
permissions, each resolved through the device access profile or explicit Custom matrix. Authenticated
clients submit only bounded opaque session, location, entry, revision,
continuation, and job references; the host never accepts a client path. It
removes protected Hidden+System entries by default before issuing client-visible
counts or references, with a global setting and per-device override, and
revalidates the complete directory revision before clipboard, Shell, or mutation
work begins. Deletion is limited to Windows Recycle Bin eligibility, and permission
revocation closes sessions and cancels owned work. One-file transfer additionally
requires a reconnect-key-signed start and answer plus a pinned-host-signed offer.
Bytes use a dedicated reliable WebRTC DTLS data channel with bounded records,
acknowledgements, backpressure, and stall cleanup. Upload names and destination
revisions are revalidated host-side; journaled partial and backup ownership preserves
an original through commit or rollback. This is remote operation with the signed-in
Windows user's authority, not a sandbox against that user or same-user malware.

Terminal requires the effective **Terminal** permission, a current pinned host
identity, signed WebRTC negotiation, and same-device ownership of the one
host-wide session. Its reliable DTLS data channel has bounded records, queues,
acknowledged output, and reconnect lifetime. Windows PowerShell runs through
ConPTY with the signed-in user's normal unelevated token; it is not sandboxed or
command-restricted. A kill-on-close Job Object terminates the shell and its child
process tree when ownership, permission, pairing, reconnect, or host lifetime ends.

Apps requires **Control open applications** and resolves only bounded opaque
window references from the current Windows session and virtual desktop. Focus
and normal-close actions require the current connection revision and revalidate
the native window identity and policy before acting. Static previews separately
require **View PC screen**, use a short-lived WebRTC DTLS data channel, and remain
bounded and transient. Native handles, process IDs, executable paths, commands,
and arguments never cross the control protocol.

AI Assistant is exposed only to an identity-current paired device using exactly
the My device profile. The host owns a hidden local Codex app-server over stdio;
no Codex listener or credential is exposed to the device. Open, ask, and reset
operations are signed against the paired device, pinned host identity, operation
ID, and (for ask) the question hash. One device owns the session and operation
IDs are replay-protected.

Before a turn, the host discovers configured MCP integration names without
sending a prompt, restarts Codex with each one disabled, and verifies the
effective configuration. It also disables Codex apps, plugins, web search,
browser/computer use, hooks, multi-agent work, shell and command execution, and
tool network access. The thread runs with approval policy `never` in the read-only
sandbox. Its only host-executed tools read the bundled Voltura Air knowledge and
search likely document filenames below the local user profile while rejecting
hidden, system, reparse-point, and sensitive working directories. This makes
process control and mutation unavailable at the execution boundary. It is not a
data-isolation promise: model requests use the signed-in user's Codex service,
and returned names or paths may be private. The client therefore presents an
explicit powerful-read-only disclosure. Prompts, answers, paths, environment
values, and credentials are excluded from Voltura Air logs and telemetry.

Files **View** composes existing boundaries rather than granting a combined
permission: the host first authorizes and completes `file.open` under the
effective Browse/open policy, and Screen then independently requires its
effective global/per-device permission, current host identity trust, and normal
encrypted screen-start authorization.

Fresh pairing keeps the QR short: it contains one short-lived token, version,
and optional routing hint, not a PC identity key or fingerprint. After opening,
the token authenticates a challenge-response exchange that pins the host's
persistent P-256 public identity and registers the browser's reconnect public
key without transmitting the token on the control transport. A saved client without a
valid PC identity pin must pair again.

Optional Screen viewing negotiates a WebRTC peer through the authenticated
control session: `/ws` for Standard Local, its encrypted virtual WebSocket for
Relay, or the DTLS DataChannel for Secure Direct. The reconnect key signs the
start request and answer; the pinned PC identity signs the exact offer hash. Invalid,
mismatched, or expired signaling is rejected before capture begins. Screen
video and Windows system-output audio use separate DTLS-SRTP tracks on one peer,
and cursor/status/audio-availability records use a DTLS-protected data channel,
which provide confidentiality, integrity, and replay protection in transit.
Standard Local's HTTP app/control metadata and JSON command traffic retain the
trusted-LAN threat model described above. Enhanced Direct protects its control
channel with DTLS, while participant addresses, timing, and hosted setup metadata
retain the stated local-network and signaling-service boundaries.

In Relay mode the Windows host binds only to loopback and both endpoints open
outbound connections. A separate persistent routing identity authenticates the
one host allowed for an opaque route. The existing pairing/reconnect proof runs
unchanged, followed by signed ephemeral P-256 ECDH. The transcript binds route,
client ID, both ephemeral keys, nonce, and pinned host identity. HKDF-SHA256 and
AES-256-GCM protect all accepted-session frames with direction and monotonic
counters. Fresh tokens are consumed only after encryption succeeds. The relay
therefore forwards ciphertext and cannot grant permission or synthesize valid
commands. It can still deny service and observe routing/network metadata and
encrypted frame sizes.

Relay screen media retains WebRTC DTLS-SRTP and uses short-lived relay-only TURN
credentials. Signed credential requests require the active host routing key and
reject timestamp/nonce replay. Usage thresholds restrict TURN issuance without
affecting command authentication. A self-hosted relay changes endpoint and
deployment ownership, not these application-layer security contracts.

Screen negotiation accepts exactly send-only H.264 and stereo Opus offers with
matching receive-only answers. The same **View PC screen** decision authorizes
both tracks. Audio capture, bounded encoding queues, and endpoint notifications
are session-owned; their failure reports audio unavailable without weakening or
terminating the video authorization. No microphone, recording, or remote audio
control message is part of this boundary.

Relay file transfer uses the exact signed TURN purpose `file-transfer` and a
60-minute credential; existing media credentials remain 15 minutes. Transfer
content remains DTLS-protected from Relay, counts toward the same usage warning
and cutoff, and is canceled at credential expiry.

Phone webcam reverses the authenticated Screen media direction: the paired browser's
reconnect key signs the exact bounded start request and answer, and the pinned PC
identity signs the offer hash. The host accepts one H.264 track and, only when
explicitly requested and locally available, one Opus track; it rejects mismatched
media and stale signaling and enforces relay-only candidates in Relay mode. Opus
processing is duration-bounded and writes only to the verified local VB-CABLE endpoint.
The host page's explicit audio test opens the corresponding base `CABLE Output`
capture endpoint with a duration-bounded buffer, rejects VB-CABLE itself as the
default playback target, and releases capture/playback on page or session teardown.
Permission or pairing revocation disposes the peer, decoders, bounded queues, output, and phone
capture. The elevated native installer extracts its embedded media source from the
locked setup executable and verifies the payload before machine-wide COM
registration; it does not trust a replaceable sibling DLL across UAC. The Frame
Server media source receives only authenticated, versioned, fixed-size local frames
and owns no network credentials.

Pending relay host sockets do not reserve a route before routing-key proof.
The relay also supplies a route-scoped opaque source key so one device source
cannot consume another source's host pairing-failure allowance; the key is not
logged or persisted by the host.

The public Custom screens community library is a separate internet-facing
service. Its account does not authorize access to a Windows host. Treat every
downloaded `.volturascreen` package as untrusted: Voltura Air accepts catalog
imports only from the official HTTPS origin, bounds and validates the package,
shows a local review, removes device assignments, and generates new local IDs
before saving. Imported application actions may refer to approved applications
that exist only on the author's PC and remain subject to local host permissions.

The optional Usage statistics ingest endpoint is public and spoofable; an
open-source host cannot safely embed an authentication secret. Telemetry is
therefore directional product evidence only, never billing, entitlement,
security, or exact-user truth. The endpoint accepts one exact 4 KiB JSON schema,
uses fixed prepared statements, deduplicates batch UUIDs, and rate-limits by
domain-separated installation and transient source-IP HMACs plus a service-wide
daily cap. It ignores User-Agent and stores neither raw UUIDs, IP addresses,
request bodies, nor event history. The shared catalog database credential is a
known blast-radius limitation; purpose-built PHP files, telemetry-only tables,
fixed SQL, and the absence of a generic query or cleanup entry point contain it.

The aggregate Usage statistics dashboard reuses the existing Custom Screens
administrator role, session, CSRF, layout, and theme. It never selects or
renders installation hashes. Destructive actions require an exact preview,
recheck the preview counts under the deletion transaction, require explicit
confirmation and a second submit, and commit at most 1,000 rows against a fixed
telemetry-table allow-list. Automatic cleanup is lease-controlled, indexed, and
bounded to 500 rows per telemetry table per pass. Ingest code does not load the
administrator cleanup owner. Host telemetry logs contain only approved counter
names/counts and fixed delivery outcomes; UUIDs, endpoint URLs, bodies, source
addresses, and input content are excluded.

When testing or deploying:

- Download only from the official product page or official GitHub releases.
- Pair only devices you trust.
- Remove stale paired devices from the Windows host Settings Devices page.
- Do not forward the Voltura Air host port from your router to the internet.
- Review a Custom screen's panels and action summary before importing it, even
  when it came from the moderated community library.
