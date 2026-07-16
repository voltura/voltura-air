# Screenshot and Installer Artwork Capture

This repo keeps public product screenshots in `docs/site/assets` and NSIS
installer artwork in `installer/assets`.

## Tools Used

- Windows host debug/release build from `apps/windows-host`.
- Chrome for deterministic browser captures.
- Playwright installed in a temporary capture folder, not as a repo dependency.
- The temporary host runs with `--isolated-test-mode`, so its empty pairing store
  and host settings are separate from the normal host. A browser can pair with
  it over loopback, but neither that pairing nor any paired-client setting can
  change the user's device list or host preferences.
- The isolated registry starts with Custom pointer disabled, so screenshot
  capture does not start a cursor watchdog. Before capture, the launcher stops
  any existing host without killing its recovery monitor and waits for that
  monitor to restore the cursor scheme and exit.
- A Windows Firewall prompt is unnecessary for this loopback-only preview and
  can be denied. Automated protocol tests use an in-memory server and should not
  display a firewall prompt at all.
- `npm run dev:ui` is an interactive Chrome session and makes no UI assertions.
  `npm run test:ui` is the separate headless paired-connection smoke test and
  checks the Power sheet at seeded phone/tablet portrait and landscape sizes.
- Isolated UI sessions use a no-op power controller, so exercising Power sheet
  controls cannot lock, turn off the display, sign out, restart, or shut down
  the development PC.

Regenerate public static-site screenshots with:

```powershell
npm run screenshots:site
```

The sticker-outlined artwork source is
`assets/branding/voltura-air-master.png`; the borderless safekeeping copy is not
consumed by the scripts. The generator uses the production master for all static
outputs and the large green-check and muted-red-cross tray badges. Regenerate
all static mobile, iOS, Android, Windows host, NSIS, and marketing-site branding
assets with:

```powershell
npm run icons:generate
```

On Windows, regenerate branding and then refresh every public screenshot in one
run with:

```powershell
npm run branding:generate
```

The script uses a temporary capture workspace under `%TEMP%`, installs
capture-only dependencies there, launches the Windows host, pairs a browser
client, and writes the public PNG files to `docs/site/assets`.

## Host Capture

The Windows host supports light, dark, and system modes through the in-app
settings. Screenshot automation passes the required theme to an isolated host;
it never exports, edits, deletes, or restores `HKCU\Software\VolturaAir`.

The host has developer automation flags for rendering public-safe screenshots
and writing the real pairing URL to a local file:

```powershell
apps\windows-host\bin\Debug\net10.0-windows\VolturaAir.Host.exe `
  --site-screenshot-mode `
  --site-screenshot-theme Light `
  --isolated-test-mode `
  --pairing-store-root "$tempDir\appdata" `
  --pairing-url-file "$tempDir\pairing-url.txt"
```

In `--site-screenshot-mode`, the host window renders the visible QR code and
visible pairing link as `https://voltura.se/air/`. The `--pairing-url-file`
output remains the real local pairing URL so automation can pair the mobile
browser. `--pairing-store-root` keeps capture-only pairing data out of the
user's real device list. The host also redirects its settings to a disposable
isolated registry key, so settings sent by the paired browser cannot alter the
normal host configuration. Do not publish a live pairing token, LAN address,
or machine-specific QR.

The host screenshot is rendered directly from the Voltura Air window handle so
foreground or topmost applications cannot replace the host image. Screen-copy
capture is used only if direct window rendering is unavailable. The script sets
its public-safe permissions, notifications, and theme only inside the isolated
host. A normal exit removes that temporary settings key; if a capture process
is interrupted, any stranded key is separate from the real settings key and
cannot alter network mode, manual host or adapter choice, manual port, or
automatic address and port history. Screenshot capture must never alter a real
phone's saved connection endpoint.

The host refreshes its in-memory hot-path setting caches whenever it enters or
leaves the isolated registry scope. A setting cached before screenshot startup
therefore cannot leak its production value into capture, and leaving capture
reloads the cached production value without rewriting it.

The capture script includes Connect plus **Preferences > Global permissions** in
both themes. It launches a capture-only host with the requested accordion open;
do not automate mouse clicks against the live WPF window. When Diagnostics
changes, capture **Diagnostics > Application log** separately with the filter
controls, themed activity rows, and the pinned bottom action row. Do not enable
Windows locking or invoke a power action just to create a screenshot.

## Mobile Client Capture

Use the pairing URL written by the host to open the real PWA in Chrome via
Playwright, click **Pair**, and wait for the connected state.

Append `screenshot=1` to the pairing URL before opening it. Screenshot mode
keeps the real app layout but displays the connected PC as `PC` instead of the
local machine name. It can also be enabled with local storage:

```js
localStorage.setItem("voltura-air.screenshotMode", "true");
```

Recommended viewport sizes:

```text
iPhone: 393 x 852
iPad:   820 x 1180
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
docs/site/assets/voltura-air-host-preferences.png
docs/site/assets/voltura-air-host-preferences-dark.png
docs/site/assets/voltura-air-iphone.png
docs/site/assets/voltura-air-iphone-dark.png
docs/site/assets/voltura-air-iphone-menu.png
docs/site/assets/voltura-air-iphone-remote-fn-dark.png
docs/site/assets/voltura-air-iphone-remote-fn-landscape-dark.png
docs/site/assets/voltura-air-iphone-url-dark.png
docs/site/assets/voltura-air-iphone-kodi-dark.png
docs/site/assets/voltura-air-ipad.png
docs/site/assets/voltura-air-ipad-dark.png
docs/site/assets/voltura-air-split.png
```

Installer artwork:

```text
installer/assets/installer-header.bmp
installer/assets/installer-welcome.bmp
```

Other generated branding outputs include the web icons and Apple launch images
under `apps/mobile-web/public`, the host PNG/ICO files under
`apps/windows-host/Assets`, and the marketing-site icon/favicons under
`docs/site` and `docs/site/assets`. The mobile build output and packaged host
`wwwroot` are generated consumers, not branding sources.

After refreshing screenshots or installer artwork, run:

```powershell
npm run build
npm test
npm run package:win
```


## Connection-state screenshots

Keep screenshots current for the public site and release notes when UI changes
affect connection feedback. Capture at least:

- normal paired trackpad/keyboard state;
- Remote Power sheet with the HDMI/display-off warning visible;
- Remote Power sheet showing **Blackout display** and, on a PC with a configured
  saver, **Turn on screen saver**. Do not add the screen-saver row to captures
  from a PC where Windows reports it unavailable;
- Kodi remote mode on an iPhone portrait viewport with dark mode, navigation
  ring enabled, and the mode tab row collapsed;
- mobile menu, Remote Fn in portrait and landscape, and the Open URL dialog;
- unavailable/retrying state;
- rejected pairing state with recovery actions;
- manual host entry and troubleshooting help on a small phone viewport;
- tablet split mode.
