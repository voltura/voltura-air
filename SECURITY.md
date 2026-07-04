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

Voltura Air is designed for trusted devices on the same local network. It is not intended to expose PC control over the public internet.

When testing or deploying:

- Download only from the official product page or official GitHub releases.
- Pair only devices you trust.
- Remove stale paired devices from the Windows host Settings Devices page.
- Do not forward the Voltura Air host port from your router to the internet.
