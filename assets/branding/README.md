# Voltura Air branding sources

This directory is the source of truth for Voltura Air product branding.

- `voltura-air-master.png` is the single application artwork master.
- `apple-startup-devices.json` declares the iPhone and iPad launch-image matrix.

Replace `voltura-air-master.png` with transparent PNG artwork at least 512px in
each dimension, then run:

```powershell
npm run icons:generate
```

That command regenerates the mobile, Android, iOS, Windows host, NSIS,
marketing-site, and README-referenced branding assets. The connected and
disconnected tray variants are derived automatically by adding green and coral
status badges to this master. On Windows, `npm run branding:generate` also
refreshes the marketing-site screenshots.

Do not edit generated copies directly. `apps/mobile-web/public` is the mobile
web source; `apps/mobile-web/dist` and packaged host `wwwroot` directories are
build outputs.
