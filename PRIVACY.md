# Privacy policy

Last updated: July 18, 2026

Voltura Air is designed to operate between a Windows PC and paired devices on
the same local network. Voltura AB does not provide an account system, cloud
relay, advertising service, analytics service, or remote telemetry service for
Voltura Air.

## Data handled by Voltura Air

The mobile browser stores the following data in that browser's local site
storage:

- a random client identifier and the device name chosen by the user;
- saved PC addresses and names;
- reconnect credentials for paired PCs;
- app, keyboard, remote, and trackpad settings; and
- text snippets the user explicitly saves.

The Windows host stores the following data under the current Windows user's
application-data directory:

- host settings and permissions;
- paired-device identifiers and names;
- a one-way hash of each reconnect credential;
- device platform, browser, and display-mode descriptions;
- pairing and connection timestamps; and
- per-device permission and pointer settings.

This information remains on the user's devices. Voltura AB does not receive it.

## Remote-control content

Pointer, keyboard, text, and control commands travel directly from the paired
browser to the Windows host over the local network. Text, pointer coordinates,
opened web addresses, and pairing credentials are not included in Voltura Air
application logs.

Typed or dictated text is delivered to Windows only when the user requests it.
Text may become part of the Windows clipboard or the selected destination
application as requested by the user. PC clipboard text is returned to a paired
browser only after an explicit request and when the host permission allows it.
The browser does not store returned clipboard text unless the user explicitly
saves it as a text snippet.

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
client identifiers. They do not contain typed text, clipboard contents, opened
web addresses, pointer coordinates, pairing tokens, or reconnect credentials.
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

Uninstalling the Windows application removes program files and shortcuts but
retains settings and pairing data under `%APPDATA%\Voltura Air`. Delete that
directory after uninstalling to remove the retained Windows-host data.

## Contact

Voltura AB maintains Voltura Air. Privacy questions may be submitted through the
[project's GitHub issue tracker](https://github.com/voltura/voltura-air/issues)
without including private text, pairing credentials, or other sensitive data.
Report security vulnerabilities using the private process in the
[security policy](SECURITY.md).
