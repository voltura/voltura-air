# Screenshot and Installer Artwork Capture

This repo keeps public product screenshots in `docs/site/assets` and NSIS
installer artwork in `installer/assets`.

## Tools Used

- Windows host debug/release build from `apps/windows-host`.
- Chrome for deterministic browser captures.
- Playwright installed in a temporary capture folder, not as a repo dependency.
- Python with Pillow from the Codex bundled runtime for image post-processing.
- The temporary Node `qrcode` package for replacing the host pairing QR with a
  public QR for `https://voltura.se/air`.

The temporary capture workspace used during the rebrand was:

```powershell
$tempDir = Join-Path $env:TEMP "voltura-air-capture"
```

## Host Capture

The Windows host supports light, dark, and system modes through the in-app
settings. For screenshot automation, set the registry value before launching:

```powershell
New-Item -Path HKCU:\Software\VolturaAir -Force | Out-Null
Set-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value Light -Type String
Set-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value Dark -Type String
Set-ItemProperty -Path HKCU:\Software\VolturaAir -Name ThemeMode -Value System -Type String
```

The host has a developer automation flag for writing the current pairing URL to
a local file:

```powershell
apps\windows-host\bin\Debug\net10.0-windows\VolturaAir.Host.exe `
  --pairing-url-file "$tempDir\pairing-url.txt"
```

For public host screenshots, capture the real window and replace only the QR
bitmap with a QR that points to `https://voltura.se/air`. Do not publish a live
pairing token, LAN address, or machine-specific QR.

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
