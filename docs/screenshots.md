# Screenshot and installer-art capture

Public screenshots live in `docs/site/assets`; installer artwork in
`installer/assets`.

## Commands

```powershell
npm run screenshots:site
npm run icons:generate
npm run branding:generate
```

`screenshots:site` captures public images. `icons:generate` derives icons and
installer artwork from `assets/branding/voltura-air-master.png`.
`branding:generate` runs both.

## Isolation and privacy

Capture uses a temporary host with `--isolated-test-mode`, loopback, disposable
settings/pairing data, and no-op power actions. The launcher stops the normal
host and waits for cursor restoration. Pairing URL files are temporary secrets:
never publish live tokens, LAN addresses, machine names, or machine-specific QR
codes.

`--site-screenshot-mode` shows the public product URL and replaces the connected
PC name with `PC`. The Debug capture options are listed in
[setup](setup.md#host-options).

Keep the Windows desktop unobstructed while host images are captured. Do not
edit a machine name out afterward; recapture safely.

## Public set

```text
docs/site/assets/voltura-air-host.png
docs/site/assets/voltura-air-host-dark.png
docs/site/assets/voltura-air-iphone.png
docs/site/assets/voltura-air-iphone-dark.png
docs/site/assets/voltura-air-iphone-kodi-dark.png
docs/site/assets/voltura-air-split.png
```

The set covers host pairing, phone trackpad, couch remote, and tablet split
mode. README and the website reuse it. Add an image only for a distinct core use
case.

Mobile public captures use the real isolated pairing flow at 393×852 phone
portrait and 1180×820 tablet landscape. Capture light and dark themes from the
app; status must already contain `PC`.

Installer outputs:

```text
installer/assets/installer-header.bmp
installer/assets/installer-welcome.bmp
```

## Interactive UI inspection

```powershell
npm run dev:ui
```

This opens an isolated Vite/Chrome device-mode session with real pairing and
temporary browser/host state. It does not replace real-device checks for touch,
installed-PWA, or LAN behavior.

Temporary review captures may cover changed unavailable/rejected states,
recovery dialogs, Power warnings, remote layouts, or split mode. Do not add
them to the public asset set by default.

## Verification

Inspect generated images, then run `npm run docs:check`. If capture automation
changed, run its focused script test; use full `npm run test:scripts` only for
shared orchestration. Installer-art changes also run `npm run package:win`.
