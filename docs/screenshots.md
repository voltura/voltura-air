# Screenshot and Installer Artwork Capture

This repo keeps public product screenshots in `docs/site/assets` and NSIS
installer artwork in `installer/assets`.

## Tools Used

- Windows host debug/release build from `apps/windows-host`.
- Chrome for deterministic browser captures.
- Playwright installed in a temporary capture folder, not as a repo dependency.

Regenerate public static-site screenshots with:

```powershell
npm run screenshots:site
```

The script uses a temporary capture workspace under `%TEMP%`, installs
capture-only dependencies there, launches the Windows host, pairs a browser
client, and writes the public PNG files to `docs/site/assets`.

## Host Capture

The Windows host supports light, dark, and system modes through the in-app
settings. For screenshot automation, set the registry value before launching:

```powershell
New-Item -Path HKCU:\Software\VolturaAir -Force | Out-Null
New-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value Light -PropertyType String -Force | Out-Null
New-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value Dark -PropertyType String -Force | Out-Null
New-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value System -PropertyType String -Force | Out-Null
```

The host has developer automation flags for rendering public-safe screenshots
and writing the real pairing URL to a local file:

```powershell
apps\windows-host\bin\Debug\net10.0-windows\VolturaAir.Host.exe `
  --site-screenshot-mode `
  --pairing-store-root "$tempDir\appdata" `
  --pairing-url-file "$tempDir\pairing-url.txt"
```

In `--site-screenshot-mode`, the host window renders the visible QR code and
visible pairing link as `https://voltura.se/air/`. The `--pairing-url-file`
output remains the real local pairing URL so automation can pair the mobile
browser. `--pairing-store-root` keeps capture-only pairing data out of the
user's real device list. Do not publish a live pairing token, LAN address, or
machine-specific QR.

The host screenshot is captured from the visible Windows window bounds. The
capture script brings the host window forward, takes a screen capture, and crops
to the actual app window. It temporarily disables connection notifications and
restores the previous notification settings when it exits.

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

## Output Files

Static site assets:

```text
docs/site/assets/voltura-air-host.png
docs/site/assets/voltura-air-host-dark.png
docs/site/assets/voltura-air-iphone.png
docs/site/assets/voltura-air-iphone-dark.png
docs/site/assets/voltura-air-ipad.png
docs/site/assets/voltura-air-ipad-dark.png
docs/site/assets/voltura-air-split.png
```

Installer artwork:

```text
installer/assets/installer-header.bmp
installer/assets/installer-welcome.bmp
```

After refreshing screenshots or installer artwork, run:

```powershell
npm run build
npm test
npm run package:win
```
