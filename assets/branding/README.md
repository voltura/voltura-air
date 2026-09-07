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

## YouTube channel artwork

The official channel is <https://www.youtube.com/@voltura-air>.
Run `npm run branding:youtube` to regenerate `youtube/voltura-air-youtube-banner.png`,
`youtube/voltura-air-promo-thumbnail.png`, and
the `youtube/voltura-air-getting-started-thumbnail.png` and
`youtube/voltura-air-cloud-relay-thumbnail.png` guide thumbnails. This standalone command uses
Sharp, the production master, and the existing privacy-safe screenshots; it does
not start a host or upload artwork. Run it after `branding:generate` when refreshing
channel artwork with new screenshots.

`scripts/generate-youtube-branding.mjs` owns composition and typography. The banner
is 2560×1440 with essential content inside the centered 1544×423 safe area.
Thumbnails are 1280×720. The renderer validates dimensions and the 6 MiB banner /
2 MiB thumbnail limits. Review the mobile crop and thumbnails before uploading in
YouTube Studio. Custom thumbnail uploads require YouTube phone verification.

`youtube-background.png` is a decorative background generated with the built-in
image-generation tool. It contains no product UI, logo, or text. Its generation
brief was: a premium 16:9 near-black charcoal and blue-green background, restrained
teal/mint arcs at the right edge, a hint of cyan and violet, and generous dark
negative space on the left and center; no words, logos, devices, people, icons,
watermarks, particles, or busy wires. Regeneration of channel artwork is local and
deterministic from this retained background. Product screenshots and logo are
composited from their existing sources without AI reconstruction.

## Getting-started walkthrough

The published guide is <https://youtu.be/za9gWg4R9xE>. It includes uploaded English
captions and belongs to the channel's Getting started with Voltura Air playlist.

`youtube/getting-started.json` owns the eight-scene editorial script, based on the
installation/connection instructions in the root README and trackpad behavior in
`docs/features.md`. Screens are the existing privacy-safe product captures, not
live phone footage. The sample QR is explicitly labeled; it is not a pairing
credential. Review those authorities whenever updating the walkthrough.

The English narration uses the synthetic `en-US-AriaNeural` voice at -3% rate.
The retained `youtube/narration/*.mp3` and aligned `.srt` segments allow offline
rendering without a voice-service dependency. To regenerate narration, install
`edge-tts==7.2.8` into a separate Python environment and run
`python scripts/branding/generate-youtube-narration.py`. This optional operation
sends the public narration text to Microsoft Edge's online speech service.

With FFmpeg on PATH (or `FFMPEG_PATH` set to its executable), run:

```powershell
node scripts/render-youtube-guide.mjs
```

The renderer writes `youtube/voltura-air-getting-started.mp4` (1080p/30, H.264/AAC)
and its English `.srt`, then fully decodes the video to check it. Intermediate
frames/clips and chapter timestamps are in ignored `artifacts/youtube-guide`.
Inspect representative frames and listen to narration before publishing. Upload
captions as English with timing; retain the existing promo as the channel trailer.
The generated background and synthetic narration are disclosed in the video
description. Uploads and publication remain separate from all local generators.

## Cloud relay walkthrough

The published guide is <https://youtu.be/TqpQlfPhX6g>, with English captions and
chapters, in the channel's Getting started with Voltura Air playlist.

`youtube/cloud-relay.json` owns the Cloud relay guide. Its authorities are
`docs/features.md`, `docs/network-and-host-selection.md`, `docs/pairing-feedback.md`,
and `docs/troubleshooting.md`, with the root README for initial setup. It explains
remote internet access, Keep awake, Connection selection and restart, fresh QR
pairing and device-name confirmation, device permissions, saved-PC reconnect,
and View PC screen away from home. It does not imply remote wake or unlimited
screen relay availability.

The user-supplied Connection and Keep awake screenshots are retained unchanged in
`youtube/cloud-relay-sources`. Scene crops are specified in the editorial JSON.
The QR is cropped from the public-safe Connect capture and is labeled as a
demonstration: scan the fresh code on the actual PC after switching to Relay.
The connected-controller image illustrates the result of pairing, not the
device-name form. Screen viewing uses the real UI with the existing fictional
desktop. The guide is a narrated walkthrough using screenshots, not a recording
of an actual remote session.

The same generators support the additional guide without replacing the original:

```powershell
python scripts/branding/generate-youtube-narration.py cloud-relay
node scripts/render-youtube-guide.mjs cloud-relay
npm run branding:youtube
```

Narration sources live in `youtube/cloud-relay-narration`; the final MP4 and English
SRT are `youtube/voltura-air-cloud-relay.mp4` and `.srt`. Intermediate review frames
and chapters are in ignored `artifacts/youtube-cloud-relay`. Apply the same
privacy, caption, visual, and full-decode checks as the getting-started guide.
