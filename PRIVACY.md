# Privacy policy

Voltura Air defaults to direct operation between a Windows PC and paired
devices on the same local network. An optional Cloud relay connection method is
available and does not require a Voltura account. Voltura AB does not provide
advertising or product-behavior analytics for Voltura Air.
The separate Custom screens community library has an optional account system
for people who choose to submit, rate, or manage shared screens.

## Data handled by Voltura Air

The mobile browser stores in local site storage:

- a random client identifier and the device name chosen by the user;
- saved PC addresses and names;
- private reconnect keys for paired PCs;
- each paired PC's public identity key and fingerprint;
- app, keyboard, remote, and trackpad settings; and
- text snippets the user explicitly saves.

The Windows host stores in the current Windows user's registry, application-data
directory, and Windows key store:

- host settings and permissions;
- paired-device identifiers and names;
- the public reconnect key registered by each paired browser;
- a persistent P-256 PC identity private key in the current user's Windows key
  store and its public fingerprint in paired-device records;
- device platform, browser, and display-mode descriptions;
- pairing and connection timestamps;
- per-device permission and pointer settings;
- the last reported CSS viewport size/orientation for each paired device, used
  only for the Custom screens editor preview;
- custom-screen names, responsive layouts, device assignments, button labels,
  literal text/shortcuts, and opaque references to approved application
  actions;
- local Microsoft Edge WebView2 runtime data used by the read-only Custom
  screens preview window; and
- presentation reports the user explicitly saves, including captured device
  name, presentation type, dates, durations, sessions, breaks, slide timing, and
  optional local presentation-file path or HTTP/HTTPS presentation link;
- the last valid left/right Files panel locations for each paired device; and
- a bounded local journal for active file operations containing the originating
  device, operation kind, job ID, and temporary destination paths needed for
  restart cleanup. The journal is cleared after cleanup and interrupted work is
  not resumed automatically.

Persistent pairing records, permissions, private reconnect keys, and saved
content remain on the user's devices. Before Relay end-to-end encryption is
established, the routing service forwards the pairing hello, challenge, and
proof frames. These can contain the device name, client identifier,
platform/browser description, public reconnect key, token identifier, nonces,
and pairing or reconnect proofs. Voltura's relay is designed to forward these
frames without parsing or storing their contents. The pairing token itself is
kept in the QR URL fragment and is not sent to the routing service.

Secure Direct signaling transiently exposes the opaque route, participant
network metadata, and offer/answer SDP containing private candidate metadata to
the signaling service. SDP and candidates are bounded, are not stored after the
one-off exchange, and are excluded from application logs. After answer
forwarding, controller content travels directly over the LAN in a
DTLS-protected WebRTC DataChannel; the signaling service does not forward or
retain established controller traffic.

## Remote-control content

In Standard Local mode, pointer, keyboard, text, and control commands travel
directly from the paired browser to the Windows host over the local HTTP/WebSocket
connection. In Enhanced Direct mode, they travel directly over the private LAN
in a DTLS-protected WebRTC DataChannel after the bounded hosted setup exchange.
In Relay mode, both sides connect outward and accepted-session commands are
end-to-end encrypted with direction-specific AES-256-GCM keys. The routing
service can observe the opaque route, connection timing, network delivery
metadata, and encrypted frame sizes, but not command contents. Text, pointer coordinates,
opened web addresses, pairing tokens, private reconnect keys, and reconnect
proofs are not included in Voltura Air application logs.

When the optional Screen tool is enabled and permitted, selected-display video
travels on a WebRTC DTLS-SRTP media track and cursor/status updates use a
DTLS-protected WebRTC data channel. Screen pixels, display contents, negotiated
session keys, SDP, cursor coordinates, and encoded video are neither logged nor
persisted. Captured GPU frames and encoded access units exist only in bounded
memory until sent or replaced by newer work.

Relay screen viewing uses Cloudflare TURN. Cloudflare processes participant IP
addresses, connection timing, credential requests, and byte counts while
forwarding DTLS-SRTP ciphertext, but cannot decrypt screen pixels or the
DTLS-protected data channel. Voltura Air queries aggregate current-month TURN
ingress and egress solely for the local usage estimate and quota cutoff. TURN
credentials expire after 15 minutes. Command relay remains available if screen
credentials are blocked by quota.

When Phone webcam is enabled and permitted, the selected phone camera video travels
on a WebRTC DTLS-SRTP media track to the paired Windows host. Camera frames, encoded
access units, SDP, credentials, proofs, and decoded virtual-camera frames are not
logged or persisted; bounded memory and the current-user Windows virtual camera hold
them only while needed. The feature requests video only and never requests phone
microphone audio. Enhanced Direct keeps established media on the selected private
LAN. Relay uses the same Cloudflare TURN processing, 15-minute credentials, aggregate
byte metering, and quota cutoff described for Screen viewing; it adds no account,
webcam-specific usage record, or billing data.

Assigned Custom screens sent to mobile contain visual definitions, opaque
screen/button IDs, and resolved availability only. Literal text, keyboard
shortcut payloads, executable details, and host action mappings remain on the
Windows PC. Application logs do not record those payloads or viewport history.
The saved-screen Preview is rendered in a WPF WebView2 window from the PC
loopback interface, contains the same visual-only definition, and cannot invoke
actions.
Application-log entries for editor operations contain only the operation,
outcome, and a bounded failure code.

In Standard Local mode, local-network observers or interfering devices on an
untrusted network may be able to observe HTTP and connection metadata or
observe/disrupt command traffic. Enhanced Direct protects established command
content with DTLS, but it does not hide LAN addresses, connection timing, or all
network metadata. Screen media is encrypted in transit by WebRTC in every mode.
These protections do not change the trusted-LAN model: use Direct only on
networks you trust and remove paired devices that should no longer control or
view the PC.

Relay and Secure Direct QR tokens are stored after `#` in the URL fragment.
The hosted PWA reads the token locally, but browsers do not include the fragment
in HTTP requests to the `voltura.se` short-link redirect or hosted-PWA server,
and the app does not send it to the relay/signaling service, analytics, or
ordinary access logs. The website and Cloudflare may still process normal
request metadata such as IP address, user agent, path, opaque route, and
timestamp for delivery and security.

When the user starts live pairing QR scanning on the HTTPS PWA, the browser
requests video-only camera access and Voltura Air decodes bounded camera frames
locally in a temporary browser worker. Camera frames and unrelated QR contents
are not transmitted, logged, or persisted. The camera stream, decoder worker,
and frames are released after a valid pairing code, cancellation, fallback,
page hiding, camera loss, or leaving the scanner. Photo QR decoding follows the
same local-only handling.

Typed or dictated text is delivered to Windows only when the user requests it.
Text may become part of the Windows clipboard or the selected destination
application as requested by the user. PC clipboard text is returned to a paired
browser only after an explicit request and when the host permission allows it.
The browser does not store returned clipboard text unless the user explicitly
saves it as a text snippet.

When Files is permitted, mobile receives bounded directory metadata such as
display locations, names, sizes, types, dates, attributes, progress display
names, and properties. File content stays on the PC or its mapped drives and is
not transferred to the mobile device. Client commands contain opaque references
rather than paths. By default, the host removes entries marked with both Windows
Hidden and System attributes before producing directory metadata; this setting
has a global value and a per-device override. File paths, names, clipboard file lists, conflict names,
temporary paths, and operation contents are excluded from application logs.

## Dictation and external services

Dictation uses the speech-recognition capability supplied by the mobile browser
or operating system. That provider may process microphone input under its own
privacy terms. Voltura Air does not receive microphone audio; it receives the
recognized text supplied by the browser.

The standard installer may contact Microsoft to download missing .NET runtimes.
The full installer includes those runtimes and does not require that download.
Opening a website, support link, or external application at the user's request
is governed by the privacy practices of that destination.

## Optional diagnostic logging

Application logging on the Windows host is off by default. When enabled, logs
contain timestamps, event and action types, outcomes, error details, and random
client identifiers. They do not contain typed text, clipboard contents, file
paths or names, file-operation conflict names, opened
web addresses, pointer coordinates, pairing tokens or IDs, private reconnect or
PC-identity keys, pairing/reconnect proofs, screen pixels, cursor coordinates,
screen SDP, encoded video, or negotiated screen-session keys.
Phone webcam camera frames, SDP, encoded or decoded video, device IDs, credentials,
and session proofs are also excluded.
Safe Relay entries may record the connection method, official/custom endpoint
type, state, selected quality, automatic Data Saver, quota warning/block, and a
bounded failure code. They never record the endpoint, route, credential, IP
address, SDP, command, text, coordinate, or screen content.
Log retention is configurable from 1 to 30 days and defaults to 2 days. Logs are
stored locally and can be viewed or deleted from Voltura Air Diagnostics.

## Product website

The Voltura Air product page has no account system, advertising, or analytics
scripts and does not set application cookies. As with ordinary web hosting, the
hosting infrastructure may process request information such as IP addresses,
browser identifiers, and timestamps for delivery, security, and operational
logging. GitHub and external support links are governed by their own privacy
policies.

## Removing local data

Users can remove paired-device access from the Windows host and forget saved PCs
from the mobile interface. Clearing the browser's site data removes all Voltura
Air data stored in that browser. Application logs can be deleted from
Diagnostics.

Saved presentation reports can be renamed, exported, emailed, or deleted from
the Windows **Presentations** page. Removing a paired device does not remove its
saved reports. Presentation report titles, timing contents, linked file paths,
and linked URLs are not written to application logs.

Custom screens can be edited, duplicated, assigned, reordered, or deleted from
the Windows **Custom screens** page. Removing a paired device removes its
assignments and last viewport metadata but does not delete reusable screens.
Screens can also be exported to local `.volturascreen` files and imported after
review. Imported screens do not retain device assignments. Catalog installation
downloads a package over HTTPS and opens the same local review before saving.
When the host encounters an unsupported Custom screens store version, it leaves
the file unchanged and reports how it can be recovered with a compatible
Voltura Air version. Invalid data in the current format is also left in place
and reported.

The optional custom-screen catalog at `voltura.se/air/screens` stores the email
address, display name, and password hash supplied when an account is created.
It also stores submitted packages, author notes, tags, moderation status and
feedback, ratings, download counts, and reports submitted with a reporter email
address. Each submitted report is also emailed to `air@voltura.se` for review.
Submitted packages are held for moderation; approved package contents,
author identity, notes, tags, ratings, and download counts are public.
Withdrawing a submission removes it from the public catalog but does not delete
its stored record. A catalog administrator can permanently delete an approved
submission and its package, ratings, and reports. Catalog accounts and data do
not grant access to a Windows host or its paired devices.

When Presentation control is enabled, the Windows host may read bounded
PowerPoint presentation names, canonical local file paths, and slideshow state
from the signed-in user's already-running PowerPoint process. It does not read
slide text or send presentation paths to mobile. Mobile may receive an opaque
saved-presentation ID, report title, and filename for still-existing PowerPoint
files; the host resolves and revalidates the local path only after an authorized
explicit launch request. The path remains host-only and may be stored in a
tracking draft so **Resume presentation** can reopen that exact file after a
break. When that tracked session is saved, its PowerPoint name and host-only
file path are retained in the local report. PowerPoint
tracking drafts and ordered slide-visit timing are stored locally with
presentation reports so an interrupted session can resume for the same exact
presentation or be saved/discarded after reconnect or host restart.

Uninstalling the Windows application removes program files and shortcuts but
retains settings and pairing data under `%APPDATA%\Voltura Air`. Delete that
directory and `%LOCALAPPDATA%\Voltura Air\Presentation statistics` after
uninstalling to remove all retained Windows-host data.

## Contact

Voltura AB maintains Voltura Air. Privacy questions may be submitted through the
[project's GitHub issue tracker](https://github.com/voltura/voltura-air/issues)
without including private text, pairing tokens, private reconnect keys, or other
sensitive data.
Report security vulnerabilities using the private process in the
[security policy](SECURITY.md).
