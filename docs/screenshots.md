# Screenshot and Installer Artwork Capture

This repo keeps public product screenshots in `docs/site/assets` and NSIS
installer artwork in `installer/assets`.

## Capture safety

- Capture runs on Windows with Chrome or Playwright.
- Temporary hosts use `--isolated-test-mode`, loopback-only networking,
  disposable pairing data and settings, and a no-op power controller.
- The launcher stops the normal host and waits for cursor recovery before
  starting a capture host.
- `npm run dev:ui` opens an interactive Chrome session. `npm run test:ui` runs
  the headless paired-connection smoke test.

Regenerate public static-site screenshots with:

```powershell
npm run screenshots:site
```

Regenerate icons and installer artwork from
`assets/branding/voltura-air-master.png` with:

```powershell
npm run icons:generate
```

On Windows, regenerate branding and then refresh every public screenshot in one
run with:

```powershell
npm run branding:generate
```

The capture writes public PNG files to `docs/site/assets`.

## Host Capture

Screenshot automation passes the requested theme to an isolated host.

The Debug host provides capture flags documented in the
[Windows host command-line reference](setup.md#windows-host-command-line-options):

```powershell
apps\windows-host\bin\Debug\net10.0-windows\VolturaAir.Host.exe `
  --site-screenshot-mode `
  --site-screenshot-theme Light `
  --isolated-test-mode `
  --pairing-store-root "$tempDir\appdata" `
  --pairing-url-file "$tempDir\pairing-url.txt"
```

`--site-screenshot-mode` displays `https://voltura.se/air/` in the window while
`--pairing-url-file` supplies the private loopback pairing URL to automation.
Keep that file temporary and never publish live tokens, LAN addresses, or
machine-specific QR codes. Pairing and settings remain in disposable storage.

The script brings the isolated Voltura Air window to the foreground and copies
its visible DWM bounds from the unlocked desktop. Keep the capture desktop
unobstructed while the host images are written.

The public capture set includes the Connect screen in both themes. The landing
page and README select the matching image with `prefers-color-scheme`. Capture
other host pages only for a specific support, release-note, or validation need.

## Mobile Client Capture

Use the pairing URL written by the host to open the real PWA in Chrome via
Playwright, click **Pair**, and wait for the connected state.

Append `screenshot=1` to the pairing URL before opening it. Screenshot mode
keeps the real app layout but displays the connected PC as `PC` instead of the
local machine name. It can also be enabled with local storage:

```js
localStorage.setItem("voltura-air.screenshotMode", "true");
```

Public capture viewport sizes:

```text
Phone portrait:    393 x 852
Tablet landscape: 1180 x 820
```

Capture light mode first, then set dark mode in local storage and reload:

```js
await page.evaluate(() => localStorage.setItem("voltura-air.themeMode", "dark"));
await page.reload({ waitUntil: "networkidle" });
```

Do not edit the status label in the image after capture. If the label contains a
machine name, recapture with `screenshot=1`.

## Manual UI Debug

For human checks while developing the mobile web UI, run:

```powershell
npm run dev:ui
```

The command reuses the real host pairing flow, but opens Chrome against the Vite
client with isolated temporary pairing and browser storage. It starts maximized,
opens DevTools, enables device mode, and selects
`Voltura 393x852 - iPhone Pro` by default. Set `VOLTURA_AIR_DEV_UI_DEVICE` to
one of the seeded `Voltura ...` device names to start with another preset.
These checks are useful before refreshing screenshots, but they do not replace
real device testing for touch behavior, installed-PWA behavior, or LAN
connectivity.

## Output Files

Static site assets:

```text
docs/site/assets/voltura-air-host.png
docs/site/assets/voltura-air-host-dark.png
docs/site/assets/voltura-air-iphone.png
docs/site/assets/voltura-air-iphone-dark.png
docs/site/assets/voltura-air-iphone-kodi-dark.png
docs/site/assets/voltura-air-split.png
```

The public set covers host pairing, phone trackpad, couch remote, and tablet
split mode. Add or replace an image only for a distinct core use case. README
and the marketing page reuse this set.

Installer artwork:

```text
installer/assets/installer-header.bmp
installer/assets/installer-welcome.bmp
```

Other generated outputs are written under `apps/mobile-web/public`,
`apps/windows-host/Assets`, `docs/site`, and `docs/site/assets`.

`npm run screenshots:site` builds the mobile client and host before capture.
After refreshing generated public images, inspect the results and run:

```powershell
npm run docs:check
npm run test:scripts
```

Run `npm run package:win` when installer artwork changes.

## Supplementary validation captures

When a change affects connection feedback or another state not represented by
the curated public set, capture the relevant states temporarily for review or
release notes. Do not check them into `docs/site/assets` or add them to the
landing page by default. Depending on the change, useful evidence can include:

- normal paired trackpad/keyboard state;
- Remote Power sheet with the HDMI/display-off warning visible;
- Remote Power sheet showing **Blackout display** and, on a PC with a configured
  saver, **Turn on screen saver**. Do not add the screen-saver row to captures
  from a PC where Windows reports it unavailable;
- Kodi remote mode on an iPhone portrait viewport with dark mode, navigation
  ring enabled, and the mode tab row collapsed;
- unavailable/retrying state;
- rejected pairing state with recovery actions;
- manual host entry and troubleshooting help on a small phone viewport;
- tablet split mode.
