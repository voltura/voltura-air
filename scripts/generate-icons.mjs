import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const masterPaths = {
  neutral: path.join(repoRoot, "scripts", "assets", "voltura-air-neutral-master.png"),
  connected: path.join(repoRoot, "scripts", "assets", "voltura-air-connected-master.png"),
  disconnected: path.join(repoRoot, "scripts", "assets", "voltura-air-disconnected-master.png"),
};

const masterDataUris = {};
for (const [state, masterPath] of Object.entries(masterPaths)) {
  const png = await readFile(masterPath);
  masterDataUris[state] = `data:image/png;base64,${png.toString("base64")}`;
}

function createIco(images) {
  const headerSize = 6;
  const entrySize = 16;
  const directory = Buffer.alloc(headerSize + images.length * entrySize);
  directory.writeUInt16LE(0, 0);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(images.length, 4);

  let imageOffset = directory.length;
  images.forEach(({ size, data }, index) => {
    const entryOffset = headerSize + index * entrySize;
    directory.writeUInt8(size === 256 ? 0 : size, entryOffset);
    directory.writeUInt8(size === 256 ? 0 : size, entryOffset + 1);
    directory.writeUInt8(0, entryOffset + 2);
    directory.writeUInt8(0, entryOffset + 3);
    directory.writeUInt16LE(1, entryOffset + 4);
    directory.writeUInt16LE(32, entryOffset + 6);
    directory.writeUInt32LE(data.length, entryOffset + 8);
    directory.writeUInt32LE(imageOffset, entryOffset + 12);
    imageOffset += data.length;
  });

  return Buffer.concat([directory, ...images.map(({ data }) => data)]);
}

function createEmbeddedSvg(png) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="Voltura Air"><image width="512" height="512" href="data:image/png;base64,${png.toString("base64")}"/></svg>\n`;
}

function createDib(rgba, size) {
  const bitmapHeader = Buffer.alloc(40);
  bitmapHeader.writeUInt32LE(40, 0);
  bitmapHeader.writeInt32LE(size, 4);
  bitmapHeader.writeInt32LE(size * 2, 8);
  bitmapHeader.writeUInt16LE(1, 12);
  bitmapHeader.writeUInt16LE(32, 14);
  bitmapHeader.writeUInt32LE(0, 16);
  bitmapHeader.writeUInt32LE(size * size * 4, 20);

  const xorBitmap = Buffer.alloc(size * size * 4);
  for (let sourceY = 0; sourceY < size; sourceY += 1) {
    const targetY = size - sourceY - 1;
    for (let x = 0; x < size; x += 1) {
      const sourceOffset = (sourceY * size + x) * 4;
      const targetOffset = (targetY * size + x) * 4;
      xorBitmap[targetOffset] = rgba[sourceOffset + 2];
      xorBitmap[targetOffset + 1] = rgba[sourceOffset + 1];
      xorBitmap[targetOffset + 2] = rgba[sourceOffset];
      xorBitmap[targetOffset + 3] = rgba[sourceOffset + 3];
    }
  }

  const andRowSize = Math.ceil(size / 32) * 4;
  const andMask = Buffer.alloc(andRowSize * size);
  return Buffer.concat([bitmapHeader, xorBitmap, andMask]);
}

async function writeToTargets(buffer, targets) {
  for (const relativePath of targets) {
    const outputPath = path.join(repoRoot, relativePath);
    await mkdir(path.dirname(outputPath), { recursive: true });
    await writeFile(outputPath, buffer);
  }
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 512, height: 512 } });
const renderCache = new Map();

async function renderArtwork(state, size, presentation) {
  const dataUri = masterDataUris[state];
  if (!dataUri) {
    throw new Error(`Unknown icon state: ${state}`);
  }
  if (presentation !== "full" && presentation !== "cropped") {
    throw new Error(`Unknown icon presentation: ${presentation}`);
  }

  const cacheKey = `${state}:${presentation}:${size}`;
  const cached = renderCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  await page.setContent(`<img src="${dataUri}">`, { waitUntil: "load" });
  const rendered = await page.evaluate(({ outputSize, iconPresentation }) => {
    const image = document.querySelector("img");
    const canvas = document.createElement("canvas");
    canvas.width = outputSize;
    canvas.height = outputSize;
    const context = canvas.getContext("2d", { alpha: true });

    const sourceCanvas = document.createElement("canvas");
    sourceCanvas.width = image.naturalWidth;
    sourceCanvas.height = image.naturalHeight;
    const sourceContext = sourceCanvas.getContext("2d", { alpha: true });
    sourceContext.drawImage(image, 0, 0);
    const sourcePixels = sourceContext.getImageData(0, 0, image.naturalWidth, image.naturalHeight).data;

    let minX = image.naturalWidth;
    let minY = image.naturalHeight;
    let maxX = -1;
    let maxY = -1;
    for (let y = 0; y < image.naturalHeight; y += 1) {
      for (let x = 0; x < image.naturalWidth; x += 1) {
        const alpha = sourcePixels[(y * image.naturalWidth + x) * 4 + 3];
        if (alpha > 8) {
          minX = Math.min(minX, x);
          minY = Math.min(minY, y);
          maxX = Math.max(maxX, x);
          maxY = Math.max(maxY, y);
        }
      }
    }

    if (maxX < minX || maxY < minY) {
      throw new Error("The icon master does not contain visible pixels.");
    }

    const sourceWidth = maxX - minX + 1;
    const sourceHeight = maxY - minY + 1;
    let destinationWidth = outputSize;
    let destinationHeight = destinationWidth * (sourceHeight / sourceWidth);
    if (destinationHeight > outputSize) {
      destinationHeight = outputSize;
      destinationWidth = destinationHeight * (sourceWidth / sourceHeight);
    }
    const destinationX = (outputSize - destinationWidth) / 2;
    const destinationY = (outputSize - destinationHeight) / 2;
    context.drawImage(
      image,
      minX,
      minY,
      sourceWidth,
      sourceHeight,
      destinationX,
      destinationY,
      destinationWidth,
      destinationHeight,
    );

    const rgba = context.getImageData(0, 0, outputSize, outputSize).data;
    let rgbaBinary = "";
    for (let offset = 0; offset < rgba.length; offset += 32768) {
      rgbaBinary += String.fromCharCode(...rgba.subarray(offset, offset + 32768));
    }
    return {
      png: canvas.toDataURL("image/png").split(",")[1],
      rgba: btoa(rgbaBinary),
    };
  }, { outputSize: size, iconPresentation: presentation });

  const artwork = {
    png: Buffer.from(rendered.png, "base64"),
    rgba: Buffer.from(rendered.rgba, "base64"),
  };
  renderCache.set(cacheKey, artwork);
  return artwork;
}

async function renderPng(state, size, presentation) {
  return (await renderArtwork(state, size, presentation)).png;
}

async function renderDib(state, size, presentation) {
  const artwork = await renderArtwork(state, size, presentation);
  return createDib(artwork.rgba, size);
}

try {
  const fullSvgPng = await renderPng("neutral", 512, "full");
  await writeToTargets(Buffer.from(createEmbeddedSvg(fullSvgPng)), [
    "apps/mobile-web/public/icon.svg",
    "apps/windows-host/wwwroot/icon.svg",
    "docs/site/assets/voltura-air-icon.svg",
  ]);

  const croppedSvgPng = await renderPng("neutral", 512, "cropped");
  await writeToTargets(Buffer.from(createEmbeddedSvg(croppedSvgPng)), [
    "apps/mobile-web/public/favicon.svg",
    "apps/windows-host/wwwroot/favicon.svg",
  ]);

  const pngGroups = [
    { size: 16, presentation: "cropped", targets: ["apps/mobile-web/public/favicon-16.png", "apps/windows-host/wwwroot/favicon-16.png"] },
    { size: 32, presentation: "cropped", targets: ["apps/mobile-web/public/favicon-32.png", "apps/windows-host/wwwroot/favicon-32.png"] },
    { size: 180, presentation: "full", targets: ["apps/mobile-web/public/apple-touch-icon.png", "apps/windows-host/wwwroot/apple-touch-icon.png"] },
    { size: 192, presentation: "full", targets: ["apps/mobile-web/public/icons/icon-192.png", "apps/windows-host/wwwroot/icons/icon-192.png"] },
    { size: 256, presentation: "full", targets: ["apps/windows-host/Assets/VolturaAir-256.png"] },
    { size: 512, presentation: "full", targets: ["apps/mobile-web/public/icons/icon-512.png", "apps/windows-host/wwwroot/icons/icon-512.png"] },
  ];

  for (const group of pngGroups) {
    await writeToTargets(await renderPng("neutral", group.size, group.presentation), group.targets);
  }

  const faviconImages = [];
  for (const size of [16, 32, 48]) {
    faviconImages.push({ size, data: await renderDib("neutral", size, "cropped") });
  }
  await writeToTargets(createIco(faviconImages), [
    "apps/mobile-web/public/favicon.ico",
    "apps/windows-host/wwwroot/favicon.ico",
  ]);

  const applicationImages = [];
  for (const size of [16, 20, 24, 32, 40, 48, 64, 128, 256]) {
    applicationImages.push({ size, data: await renderDib("neutral", size, "cropped") });
  }
  await writeToTargets(createIco(applicationImages), ["apps/windows-host/Assets/VolturaAir.ico"]);

  const traySizes = [16, 20, 24, 32, 40, 48, 64];
  const trayTargets = {
    neutral: "apps/windows-host/Assets/VolturaAirTray.ico",
    connected: "apps/windows-host/Assets/VolturaAirTrayConnected.ico",
    disconnected: "apps/windows-host/Assets/VolturaAirTrayDisconnected.ico",
  };

  for (const [state, target] of Object.entries(trayTargets)) {
    const images = [];
    for (const size of traySizes) {
      images.push({ size, data: await renderDib(state, size, "cropped") });
    }
    await writeToTargets(createIco(images), [target]);
  }
} finally {
  await browser.close();
}

console.log("Voltura Air icon assets generated from the approved artwork masters.");
