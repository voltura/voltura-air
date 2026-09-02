# Privacy policy

Voltura Air defaults to direct operation between a Windows PC and paired
devices on the same local network. An optional Cloud relay connection method is
available and does not require a Voltura account. Voltura AB does not provide
advertising. Voltura Air provides optional, consent-gated first-party usage
statistics as described below.
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
- separate installed and portable Usage statistics choices, plus a random
  identifier for the applicable profile only while that choice is allowed;
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

When AI Assistant is opened from the Windows host or by a My device user,
Voltura Air starts the locally installed Codex app-server with the user's
existing Codex account.
Questions and answers travel over the same authenticated Direct or end-to-end
encrypted Relay command connection as other controls. Voltura Air does not log,
add to telemetry, or separately persist prompts, answers, file contents, paths,
environment values, or Codex credentials. Codex stores the dedicated Assistant
conversation on the PC and processes its model requests under the user's Codex
account and applicable OpenAI terms and privacy choices.

AI Assistant is read-only and its host-owned tools run with the same Windows-user
access as Codex running locally. The current tools read bundled Voltura Air
documentation and can search likely document filenames under the local user
profile, returning paths, sizes, and modification times without reading those
documents' contents. Names and paths can still be private. Its environment
disables shell and command execution, web search, tool network access, configured
MCP integrations, apps, plugins, browser and computer control, hooks, and
multi-agent work. Use it only from a trusted personal device and avoid requesting
secrets.

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
and current Windows system-output audio travel on WebRTC DTLS-SRTP media tracks;
cursor/status/audio-availability updates use a DTLS-protected WebRTC data channel.
Screen pixels, display contents, system-audio samples, negotiated session keys,
SDP, cursor coordinates, and encoded media are not logged or persisted by the
Windows host or transport services. Captured GPU frames, loopback audio, and
encoded media exist there only in bounded memory until sent or replaced by newer
work. Capture uses the same **View PC screen** permission, starts with the
session, and does not capture a microphone or a selected application. Device
playback starts muted and local PC playback continues normally.

When Apps is opened and **Control open applications** is permitted, the PC
returns bounded titles, application display names, state flags, and random
connection-scoped references for eligible application windows in the current
Windows session and virtual desktop. It does not return native window handles,
process IDs, executable paths, command lines, arguments, or icons. Discovery
happens only on entry or an explicit refresh/action; it is not polled or stored.
The separate **View PC screen** permission controls static card previews. Only the
centered window and immediate neighbors are requested, and preview pixels can
contain private content visible in those application windows. JPEG previews
travel over a short-lived WebRTC DTLS-protected data channel and exist only in
bounded PC and browser memory. They are not written to files, logged, added to
telemetry, or retained after replacement, leaving Apps, disconnect, permission
loss, or failure. Relay previews use Cloudflare TURN with the same encrypted
content, network-metadata visibility, aggregate byte metering, and quota cutoff
described for Relay file transfer; Cloudflare cannot decrypt the preview pixels.

When Terminal is opened and **Terminal** permission is allowed, commands and
PowerShell output travel over a dedicated WebRTC DTLS-protected data channel.
Voltura Air keeps only bounded in-memory output needed for delivery and a
same-device reconnect window of up to 15 minutes; browser scrollback is also
in-memory and does not survive reload. Commands, output, working directories,
environment values, and terminal contents are not persisted by Voltura Air,
added to telemetry, or included in application logs. Commands still run with
the signed-in Windows user's normal access and can read or change anything that
account can access.

Relay screen viewing uses Cloudflare TURN. Cloudflare processes participant IP
addresses, connection timing, credential requests, and byte counts while
forwarding DTLS-SRTP ciphertext, but cannot decrypt screen pixels, PC audio, or the
DTLS-protected data channel. Voltura Air queries aggregate current-month TURN
ingress and egress solely for the local usage estimate and quota cutoff. TURN
credentials expire after 15 minutes. Command relay remains available if screen
credentials are blocked by quota.

Relay file transfer uses the same Cloudflare TURN processing, aggregate byte
metering, warning, and quota cutoff. Its purpose-bound credentials expire after
60 minutes; existing screen and webcam credentials remain 15 minutes. TURN can
observe network metadata and encrypted byte counts but cannot decrypt the
DTLS-protected file content.

The user can explicitly capture the currently watched display as a cursor-free
PNG. The PC holds that PNG only in bounded memory until transfer completes or
fails and creates no screenshot file, history, timer, or background capture. The
receiving browser uses the same per-tab temporary storage and Save/Share cleanup
rules as a PC-file download. Official Relay admission conservatively estimates
three times the PNG size plus 1 MiB against aggregate monthly usage; those
analytics may lag and concurrent sessions are not reserved.

On supporting browsers, the user can also explicitly record up to five minutes
of the received screen video on that device. PC sound is included only when the
local Sound control is on at the start. The recording contains the clean PC
picture without the cursor and device controls, is written in bounded chunks to
one per-tab temporary browser-storage file, and is never uploaded or copied back
to the PC. Screen stop, stream loss, the five-minute limit, or foreground loss
finalizes the available partial for Save/Share or Discard. Successful Save/Share
or Discard removes it; navigation, page exit, reload, or a later page-start sweep
removes an abandoned partial. Voltura Air keeps no recording history or recovery
catalog.

When Phone webcam is enabled and permitted, selected phone camera video and optional,
explicitly enabled microphone audio travel on WebRTC DTLS-SRTP media tracks to the
paired Windows host. Camera frames, audio samples, encoded media, SDP, credentials,
proofs, and decoded output are not logged or persisted; bounded memory holds them
only while needed. Audio is sent only to the exact locally detected VB-CABLE endpoint.
If the user explicitly starts **Test audio** during an active microphone session, the
Windows host holds a duration-bounded `CABLE Output` buffer and plays it through the
default local speakers; leaving the page, ending the session, or stopping the test
releases that monitor. Monitored audio is not logged or persisted.
Enhanced Direct keeps established media on the selected private
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

When a paired device opens Diagnostics or explicitly refreshes it, the Windows
host may return a permission-gated, read-only snapshot containing connection
state, whether enhanced HTTPS capabilities are enabled, PC and adapter names,
selected local IP and port, Windows/system/processor details, logical-processor
count, primary display mode, memory, system-disk capacity, and uptime. It excludes
local paths, Windows usernames, other device names, raw host or WebSocket URLs,
credentials, tokens, query strings, and log contents. Voltura Air does not poll,
retain a history of, or proactively push this snapshot.

When Files is permitted, mobile receives bounded directory metadata such as
display locations, names, sizes, types, dates, attributes, progress display
names, and properties. Client commands contain opaque references rather than
paths. By default, the host removes entries marked with both Windows Hidden and
System attributes before producing directory metadata; this setting has a
global value and a per-device override. With the separate Transfer permission,
one explicitly selected file may travel directly between the paired devices on
a WebRTC DTLS-protected data channel. PC-to-device content is held in transient
browser storage until Save/Share and is removed on completion or Files/session
cleanup; device-to-PC content is held in a journaled partial until commit or
cleanup. File paths, names, clipboard file lists, conflict names, temporary
paths, file contents, and operation contents are excluded from application logs.

## Dictation and external services

Dictation uses the speech-recognition capability supplied by the mobile browser
or operating system. That provider may process microphone input under its own
privacy terms. Voltura Air does not receive microphone audio; it receives the
recognized text supplied by the browser.

The standard installer may contact Microsoft to download missing .NET runtimes.
The full installer includes those runtimes and does not require that download.
Installed builds with automatic downloads enabled contact GitHub Releases at most once daily to check and, when no controller is active, download a signed installer. No account, cookies, credentials, pairing data, or telemetry are sent for this check. Users choose when to install a staged update.
Opening a website, support link, or external application at the user's request
is governed by the privacy practices of that destination.

## Optional usage statistics

Usage statistics are off unless the current Windows user explicitly chooses
**Allow usage statistics**. An interactive installation or upgrade asks once
when the installed choice is unset; **Do not allow** has initial focus and
silent installation cannot grant consent. Portable copies have a separate
choice and start off without a first-run prompt. Both choices can be changed
under **Diagnostics → Usage statistics**. They do not affect pairing, local
control, Enhanced Direct, Relay, or any product feature.

After Allow, the Windows host creates one random UUID for that installed or
portable profile. The UUID is unrelated to paired-device, catalog, Relay, or
account identities. The host sends it over HTTPS to the fixed first-party
endpoint with the Voltura Air version and closed aggregate counters for:

- one telemetry-active start per consent-enabled local-identifier lifetime (normally once per Windows-host process; disabling discards that lifetime, and re-enabling starts a new unlinkable one);
- successful authenticated Standard Local, Enhanced Direct, and Relay
  controller sessions; and
- feature-using sessions for Trackpad, Keyboard, Dictation, Media controls,
  Presentation, Custom screens, Files, Screen viewing, Phone webcam, and Gyro
  mouse. Each feature is counted at most once per authenticated controller
  session in each consent-enabled identifier lifetime, not once per click or as
  proof that downstream work succeeded.

The mobile PWA never makes a telemetry HTTP request, stores no telemetry UUID,
and has no telemetry consent state. It adds a closed functional input-context
value to existing authenticated controller commands only when the connected
host advertises support, allowing the host to distinguish coarse feature use.
Older hosts receive the unchanged command shape. The context contains no input
content.

Usage statistics never contain typed or dictated text, clipboard or file
contents or names, URLs, device or screen names, keys, pointer coordinates,
credentials, proofs, pairing or reconnect identifiers, screen or camera
content, audio, crash reports, performance timings, OS, hardware, browser or
device data, session duration, or individual button presses. The service does
not store request JSON, raw UUIDs, raw IP addresses, or User-Agent values.

The receiving PHP service immediately derives a domain-separated HMAC-SHA-256
pseudonym from the UUID using a server-side secret. Daily rows contain that
binary pseudonym, the received host version, UTC receipt date, and approved
counters. Separate operational rows contain the random batch ID paired with the
installation pseudonym for deduplication; domain-separated installation/source
rate-bucket HMACs with their window and bounded request count; delivery-health
totals; and one non-identifying cleanup-lease timestamp. The raw UUID exists in
the HTTPS request because it is needed to derive the pseudonym but is never
logged. A source IP is processed only into its short-lived HMAC rate bucket for
abuse prevention; that bucket does not appear in the dashboard. Shared database
credentials are contained by fixed prepared statements and telemetry-only tables
and cleanup operations.

Daily aggregate and server-recorded delivery-health rows are retained for 180
days. Batch-deduplication and installation/source rate-bucket rows are retained
for 24 hours. Ingest and administrator access acquire a lease and delete only
bounded chunks, so physical cleanup resumes on the next access after complete
service inactivity; dashboard queries exclude data beyond the aggregate
retention window even while deletion catches up. An authenticated existing
Custom Screens administrator can also preview and run bounded retention,
date-cutoff, or delete-all operations against telemetry tables only.

Turning Usage statistics off changes the host's cached state first, cancels
telemetry workers and network work, discards every unsent in-memory count and
batch, saves **Do not allow**, and removes the local UUID. A cleanup failure is
shown rather than hidden and is retried on a later action or startup. Re-enabling
creates an unlinkable replacement UUID and never reuses a stale one. Previously
accepted daily aggregates cannot be identified and deleted from a local UUID;
they remain until normal retention or administrator cleanup.

The public ingest endpoint is necessarily spoofable because Voltura Air is open
source and embeds no trusted client secret. Rate limits, strict schemas,
deduplication, service-wide caps, and directional dashboard wording limit that
risk. Its results are product signals about active opted-in installations, not
anonymous data, total installation counts, billing records, security evidence,
or exact user truth.

## Optional diagnostic logging

Application logging on the Windows host is off by default. When enabled, logs
contain timestamps, event and action types, outcomes, error details, and random
client identifiers. They do not contain typed text, clipboard contents, file
paths or names, file-operation conflict names, opened
web addresses, pointer coordinates, pairing tokens or IDs, private reconnect or
PC-identity keys, pairing/reconnect proofs, screen pixels, cursor coordinates,
screen SDP, encoded video, or negotiated screen-session keys.
Terminal commands, output, working directories, environment values, signaling,
and session keys are also excluded.
Phone webcam camera frames, SDP, encoded or decoded video, device IDs, credentials,
and session proofs are also excluded.
Safe Relay entries may record the connection method, official/custom endpoint
type, state, selected quality, automatic Data Saver, quota warning/block, and a
bounded failure code. They never record the endpoint, route, credential, IP
address, SDP, command, text, coordinate, or screen content.
When Usage statistics are allowed, safe lifecycle and delivery entries may add
only the approved metric names and bounded aggregate counts, destination
category, result category, and bounded HTTP status code/class. They never add
the UUID or pseudonym, endpoint URL, request or response body, IP address,
User-Agent, or any prohibited content. Telemetry producers never write logs;
only the independent telemetry worker does, through this existing bounded log.
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
Diagnostics. Disabling **Usage statistics** there removes the applicable local
telemetry UUID and discards unsent data; accepted server aggregates follow the
separate retention and administrator-cleanup rules above.

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
