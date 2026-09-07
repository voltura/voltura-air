import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const branding = path.join(root, "assets", "branding");
const output = path.join(branding, "youtube");
const screenshots = path.join(root, "apps", "public-site", "assets");
const background = path.join(branding, "youtube-background.png");
const master = path.join(branding, "voltura-air-master.png");

// At 2560x1440, keep essential banner content inside x=508..2052, y=509..931.
// The background is decorative; the logo and product UI always use real assets.
function svg(width, height, content) {
  return Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">
    <style>text { font-family: 'Segoe UI', Arial, sans-serif; fill: #f7f2e9; }
    .mint { fill: #66e5cf; } .muted { fill: #bad0d3; }</style>${content}</svg>`);
}

async function image(input, width, height, left, top, radius = 0) {
  let resized = sharp(input).resize(width, height, { fit: "contain", background: "#00000000" });
  if (radius) {
    resized = resized.composite([
      {
        input: svg(width, height, `<rect width="100%" height="100%" rx="${radius}" fill="white"/>`),
        blend: "dest-in",
      },
    ]);
  }
  return { input: await resized.png().toBuffer(), left, top };
}

async function save(name, width, height, layers, maxBytes) {
  const result = await sharp(background)
    .resize(width, height, { fit: "cover" })
    .composite(layers)
    .removeAlpha()
    .png({ compressionLevel: 9 })
    .toBuffer();
  const metadata = await sharp(result).metadata();
  if (metadata.width !== width || metadata.height !== height || result.length > maxBytes) {
    throw new Error(`${name} exceeds its YouTube dimension or file-size requirements.`);
  }
  await sharp(result).toFile(path.join(output, name));
  console.log(`${name}: ${width}x${height}, ${(result.length / 1024).toFixed(0)} KiB`);
}

async function banner() {
  await save(
    "voltura-air-youtube-banner.png",
    2560,
    1440,
    [
      {
        input: svg(
          2560,
          1440,
          `
      <text x="622" y="587" font-size="32" font-weight="650">Voltura Air</text>
      <text x="548" y="689" font-size="68" font-weight="750">Your phone.</text>
      <text x="548" y="773" font-size="68" font-weight="750">Your Windows remote.</text>
      <text x="550" y="835" font-size="26" class="muted">Remote control · Screen view · Webcam</text>
      <text x="550" y="889" font-size="23" class="mint">Free &amp; open source</text>
      <text x="1645" y="889" font-size="23">voltura.se/air</text>
      <rect x="1480" y="606" width="434" height="240" rx="14" fill="#050a0d" stroke="#516970" stroke-width="3"/>
      <path d="M1670 846v22h54v-22m-90 26h126" fill="none" stroke="#516970" stroke-width="9"/>
      <rect x="1874" y="551" width="146" height="307" rx="23" fill="#080f13" stroke="#779398" stroke-width="3"/>
    `,
        ),
        left: 0,
        top: 0,
      },
      await image(master, 58, 58, 546, 544),
      await image(
        path.join(screenshots, "voltura-air-host-custom-screens-dark.png"),
        414,
        219,
        1490,
        616,
        8,
      ),
      await image(path.join(screenshots, "voltura-air-iphone-dark.png"), 130, 283, 1882, 563, 15),
    ],
    6 * 1024 * 1024,
  );
}

async function thumbnail(name, lines, label) {
  await save(
    name,
    1280,
    720,
    [
      {
        input: svg(
          1280,
          720,
          `
      <rect width="660" height="720" fill="#061015" opacity=".35"/>
      <text x="119" y="91" font-size="32" font-weight="650">Voltura Air</text>
      ${lines.map((line, index) => `<text x="62" y="${237 + index * 94}" font-size="82" font-weight="750" ${index === lines.length - 1 ? 'class="mint"' : ""}>${line}</text>`).join("")}
      <rect x="64" y="574" width="280" height="48" rx="24" fill="#143b3d"/>
      <text x="87" y="606" font-size="24" font-weight="650">${label}</text>
      <rect x="679" y="243" width="524" height="309" rx="20" fill="#050a0d" stroke="#526a70" stroke-width="3"/>
      <path d="M903 552v38h75v-38m-128 45h180" fill="none" stroke="#526a70" stroke-width="13"/>
      <rect x="1030" y="115" width="186" height="411" rx="30" fill="#080f13" stroke="#92acb0" stroke-width="3"/>
    `,
        ),
        left: 0,
        top: 0,
      },
      await image(master, 56, 56, 57, 48),
      await image(
        path.join(screenshots, "voltura-air-host-custom-screens-dark.png"),
        502,
        279,
        690,
        254,
        10,
      ),
      await image(path.join(screenshots, "voltura-air-iphone-dark.png"), 168, 373, 1039, 134, 19),
    ],
    2 * 1024 * 1024,
  );
}

await mkdir(output, { recursive: true });
await banner();
await thumbnail(
  "voltura-air-promo-thumbnail.png",
  ["Control your PC", "from your", "phone."],
  "FREE · WINDOWS 11",
);
await thumbnail(
  "voltura-air-getting-started-thumbnail.png",
  ["Your first", "connection.", "Step by step."],
  "GETTING STARTED",
);
await save(
  "voltura-air-cloud-relay-thumbnail.png",
  1280,
  720,
  [
    {
      input: svg(
        1280,
        720,
        `
        <rect width="630" height="720" fill="#061015" opacity=".35"/>
        <text x="119" y="91" font-size="32" font-weight="650">Voltura Air</text>
        <text x="62" y="237" font-size="82" font-weight="750">Your PC.</text>
        <text x="62" y="331" font-size="82" font-weight="750">Over the</text>
        <text x="62" y="425" font-size="82" font-weight="750" class="mint">internet.</text>
        <rect x="64" y="574" width="310" height="48" rx="24" fill="#143b3d"/>
        <text x="86" y="606" font-size="24" font-weight="650">CLOUD RELAY GUIDE</text>
        <text x="699" y="237" font-size="32" font-weight="650" class="mint">View PC screen</text>
        <text x="699" y="280" font-size="26">Away from home</text>
      `,
      ),
      left: 0,
      top: 0,
    },
    await image(master, 56, 56, 57, 48),
    await image(
      await sharp(path.join(screenshots, "voltura-air-screen-view.png"))
        .extract({ left: 60, top: 940, width: 1480, height: 672 })
        .png()
        .toBuffer(),
      610,
      300,
      640,
      320,
    ),
  ],
  2 * 1024 * 1024,
);
