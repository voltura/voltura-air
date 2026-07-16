# Voltura Air branding sources

This directory is the source of truth for Voltura Air product branding.

- `voltura-air-neutral-master.png` is the default application mark.
- `voltura-air-connected-master.png` is the connected tray-state mark.
- `voltura-air-disconnected-master.png` is the disconnected tray-state mark.
- `apple-startup-devices.json` declares the iPhone and iPad launch-image matrix.

Replace the three master PNG files with artwork using the same transparent PNG
format, then run:

```powershell
npm run icons:generate
```

That command regenerates the mobile, Android, iOS, Windows host, NSIS,
marketing-site, and README-referenced branding assets. On Windows,
`npm run branding:generate` also refreshes the marketing-site screenshots.

Do not edit generated copies directly. `apps/mobile-web/public` is the mobile
web source; `apps/mobile-web/dist` and packaged host `wwwroot` directories are
build outputs.
