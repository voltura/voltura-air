# Screenshot and installer-art capture

Public screenshots live in `apps/public-site/assets`; installer artwork in
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

WPF host images are rendered directly from the laid-out visual tree with
`RenderTargetBitmap`; no host or startup window is shown and desktop contents
cannot cover the result. Browser images use Playwright. Do not edit a machine
name or other private data out afterward; recapture safely.

## Public set

```text
apps/public-site/assets/voltura-air-host.png
apps/public-site/assets/voltura-air-host-dark.png
apps/public-site/assets/voltura-air-host-custom-screens.png
apps/public-site/assets/voltura-air-host-custom-screens-dark.png
apps/public-site/assets/voltura-air-iphone.png
apps/public-site/assets/voltura-air-iphone-dark.png
apps/public-site/assets/voltura-air-iphone-kodi-dark.png
apps/public-site/assets/voltura-air-iphone-kodi-dark-forum.png
apps/public-site/assets/voltura-air-split.png
apps/public-site/assets/voltura-air-files.png
apps/public-site/assets/voltura-air-files-dark.png
```

The set covers host pairing, the fixed-size responsive Custom screens editor,
phone trackpad, couch remote, tablet split mode, and a two-panel Files on PC
view rendered from deterministic example folders and files. The 350-pixel-wide
`-forum` image is derived from the Kodi screenshot for forum posts. README and
the website reuse the full-size images. Add an image only for a distinct core
use case.

Mobile public captures use the real isolated pairing flow at 393×852 phone
portrait and 1180×820 tablet landscape. Capture light and dark themes from the
app; status must already contain `PC`. The Files capture uses only the
screenshot harness's fixed example data; never use a developer's real drives,
paths, filenames, or account name.

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
