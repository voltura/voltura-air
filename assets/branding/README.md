# Voltura Air branding sources

This directory is the source of truth for Voltura Air product branding.

- `voltura-air-master.png` is the sticker-outlined production master consumed
  by every downstream branding output.
- `voltura-air-borderless-for-safekeeping.png` is retained only as an artwork
  backup and is not read or modified by the generator.
- `apple-startup-devices.json` declares the iPhone and iPad launch-image matrix.

Replace `voltura-air-master.png` with transparent PNG artwork at least 512px in
each dimension, then run:

```powershell
npm run icons:generate
```

That command regenerates the mobile, Android, iOS, Windows host, NSIS,
marketing-site, and README-referenced branding assets. It derives the connected
and disconnected tray variants by adding large green-check and muted-red-cross
badges. Ordinary app and task-area icons use a tight fit, while platform-safe
maskable artwork retains its required inset. On Windows,
`npm run branding:generate` also refreshes the marketing-site screenshots.

Do not edit generated copies outside this directory directly.
`apps/mobile-web/public` is the mobile web source; `apps/mobile-web/dist` and
packaged host `wwwroot` directories are build outputs.
