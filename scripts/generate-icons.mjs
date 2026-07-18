import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import sharp from "sharp";
import {
  assertBmp24,
  assertIco,
  assertMaskableSafeZone,
  assertOpaque,
  assertPng,
  createBmp24,
  createDib,
  createEmbeddedSvg,
  createIco,
  getPngDimensions,
} from "./branding/image-tools.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const brandingRoot = path.join(repoRoot, "assets", "branding");
const startupRoot = path.join(repoRoot, "apps", "mobile-web", "public", "startup-images");
const colours = {
  darkBackground: "#101418",
  darkForeground: "#f7f2e9",
  lightBackground: "#f6f8fa",
  lightForeground: "#1c2227",
  connected: "#13a10e",
  disconnected: "#d13438",
};
const maskableBackground = [16, 20, 24];
const tightArtworkScale = 0.98;
const trayBadgeStyle = {
  centreX: 0.65,
  centreY: 0.68,
  outerRadius: 0.34375,
  rimWidth: 0.046875,
  glyphWidth: 0.075,
};
const masterPath = path.join(brandingRoot, "voltura-air-master.png");
const outputChecks = [];

const startupDevices = JSON.parse(
  await readFile(path.join(brandingRoot, "apple-startup-devices.json"), "utf8"),
);
validateStartupDevices(startupDevices);

const masterPng = await readFile(masterPath);
const masterLabel = path.relative(repoRoot, masterPath);
const masterDimensions = getPngDimensions(masterPng, masterLabel);
if (masterDimensions.width < 512 || masterDimensions.height < 512) {
  throw new Error(
    `${masterLabel} is ${masterDimensions.width}x${masterDimensions.height}; the branding master must be at least 512px in each dimension.`,
  );
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 512, height: 512 } });

try {
  await loadMaster(masterPng);
  await clearStartupOutputs();
  await generateWebAssets();
  await generateWindowsAssets();
  await generateInstallerArtwork();
  await generateAppleStartupImages();
  await validateWrittenOutputs();
} finally {
  await browser.close();
}

console.log(
  `Generated and validated ${outputChecks.length} Voltura Air branding files from assets/branding.`,
);

async function loadMaster(masterPng) {
  const masterDataUri = `data:image/png;base64,${masterPng.toString("base64")}`;
  await page.setContent(`<img id="master" src="${masterDataUri}" alt="">`, { waitUntil: "load" });

  const visibleBounds = await page.evaluate(() => {
    const bounds = {};
    for (const image of document.images) {
      const sourceCanvas = document.createElement("canvas");
      sourceCanvas.width = image.naturalWidth;
      sourceCanvas.height = image.naturalHeight;
      const context = sourceCanvas.getContext("2d", { alpha: true });
      context.drawImage(image, 0, 0);
      const pixels = context.getImageData(0, 0, image.naturalWidth, image.naturalHeight).data;

      let minX = image.naturalWidth;
      let minY = image.naturalHeight;
      let maxX = -1;
      let maxY = -1;
      for (let y = 0; y < image.naturalHeight; y += 1) {
        for (let x = 0; x < image.naturalWidth; x += 1) {
          if (pixels[(y * image.naturalWidth + x) * 4 + 3] <= 8) {
            continue;
          }
          minX = Math.min(minX, x);
          minY = Math.min(minY, y);
          maxX = Math.max(maxX, x);
          maxY = Math.max(maxY, y);
        }
      }

      if (maxX < minX || maxY < minY) {
        throw new Error(`${image.id} master does not contain visible pixels.`);
      }
      bounds.master = { minX, minY, maxX, maxY };
    }
    return bounds;
  });

  await page.evaluate((bounds) => {
    window.volturaAirMasterBounds = bounds;
  }, visibleBounds);
}

async function clearStartupOutputs() {
  const relative = path.relative(repoRoot, startupRoot);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error("Refusing to clear startup images outside the repository.");
  }
  await rm(startupRoot, { recursive: true, force: true });
}

async function generateWebAssets() {
  const full = await renderSquare(512, { scale: tightArtworkScale });
  await writeSvg(createEmbeddedSvg(full.png), [
    "apps/mobile-web/public/icon.svg",
    "docs/site/assets/voltura-air-icon.svg",
  ]);

  const cropped = await renderSquare(512, { scale: tightArtworkScale });
  await writeSvg(createEmbeddedSvg(cropped.png), ["apps/mobile-web/public/favicon.svg"]);

  for (const size of [16, 32]) {
    const artwork = await renderSquare(size, { scale: tightArtworkScale });
    await writePng(artwork, size, size, [
      `apps/mobile-web/public/favicon-${size}.png`,
      `docs/site/favicon-${size}.png`,
    ]);
  }

  const appleTouch = await renderSquare(180, {
    background: colours.darkBackground,
    scale: 0.7,
  });
  assertOpaque(appleTouch.rgba, "Apple touch icon");
  await writePng(appleTouch, 180, 180, [
    "apps/mobile-web/public/apple-touch-icon.png",
    "docs/site/apple-touch-icon.png",
  ]);

  for (const size of [192, 512]) {
    const anyIcon = await renderSquare(size, { scale: tightArtworkScale });
    await writePng(anyIcon, size, size, [`apps/mobile-web/public/icons/icon-${size}.png`]);

    const maskableIcon = await renderSquare(size, {
      background: colours.darkBackground,
      scale: 0.54,
    });
    assertOpaque(maskableIcon.rgba, `Maskable ${size}px icon`);
    assertMaskableSafeZone(
      maskableIcon.rgba,
      size,
      maskableBackground,
      `Maskable ${size}px icon`,
    );
    await writePng(maskableIcon, size, size, [
      `apps/mobile-web/public/icons/icon-maskable-${size}.png`,
    ]);
  }

  const faviconSizes = [16, 32, 48];
  const favicon = await renderIco(faviconSizes, tightArtworkScale);
  await writeIco(favicon, faviconSizes, [
    "apps/mobile-web/public/favicon.ico",
    "docs/site/favicon.ico",
  ]);
}

async function generateWindowsAssets() {
  const hostIcon = await renderSquare(256, { scale: tightArtworkScale });
  await writePng(hostIcon, 256, 256, ["apps/windows-host/Assets/VolturaAir-256.png"]);

  const applicationSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];
  await writeIco(
    await renderIco(applicationSizes, tightArtworkScale),
    applicationSizes,
    ["apps/windows-host/Assets/VolturaAir.ico"],
  );

  const traySizes = [16, 20, 24, 32, 40, 48, 64];
  const trayTargets = {
    default: "apps/windows-host/Assets/VolturaAirTray.ico",
    connected: "apps/windows-host/Assets/VolturaAirTrayConnected.ico",
    disconnected: "apps/windows-host/Assets/VolturaAirTrayDisconnected.ico",
  };
  const trayBuffers = {};
  for (const [status, target] of Object.entries(trayTargets)) {
    const badge = status === "default" ? null : status;
    const buffer = await renderIco(traySizes, tightArtworkScale, badge);
    trayBuffers[status] = buffer;
    await writeIco(buffer, traySizes, [target]);
  }
  if (new Set(Object.values(trayBuffers).map((buffer) => buffer.toString("base64"))).size !== 3) {
    throw new Error("Generated tray states must have distinct artwork.");
  }
}

async function generateInstallerArtwork() {
  const header = await renderComposition({
    width: 150,
    height: 57,
    background: colours.darkBackground,
    foreground: colours.darkForeground,
    kind: "installer-header",
  });
  assertOpaque(header.rgba, "NSIS header artwork");
  await writeBmp(createBmp24(header.rgba, 150, 57), 150, 57, [
    "installer/assets/installer-header.bmp",
  ]);

  const welcome = await renderComposition({
    width: 164,
    height: 314,
    background: colours.darkBackground,
    foreground: colours.darkForeground,
    kind: "installer-welcome",
  });
  assertOpaque(welcome.rgba, "NSIS welcome artwork");
  await writeBmp(createBmp24(welcome.rgba, 164, 314), 164, 314, [
    "installer/assets/installer-welcome.bmp",
  ]);
}

async function generateAppleStartupImages() {
  for (const device of startupDevices) {
    for (const theme of ["dark", "light"]) {
      const background = theme === "dark" ? colours.darkBackground : colours.lightBackground;
      const foreground = theme === "dark" ? colours.darkForeground : colours.lightForeground;
      for (const orientation of ["portrait", "landscape"]) {
        const portrait = orientation === "portrait";
        const width = (portrait ? device.width : device.height) * device.dpr;
        const height = (portrait ? device.height : device.width) * device.dpr;
        const artwork = await renderComposition({
          width,
          height,
          background,
          foreground,
          kind: "apple-startup",
        });
        if (!artwork.opaque) {
          throw new Error(`${device.name} ${theme} ${orientation} startup image is transparent.`);
        }
        await writePng(artwork, width, height, [
          `apps/mobile-web/public/startup-images/${startupFileName(device, theme, orientation)}`,
        ]);
      }
    }
  }
}

async function renderIco(sizes, scale, badge = null) {
  const images = [];
  for (const size of sizes) {
    const artwork = await renderSquare(size, { badge, scale });
    images.push({
      size,
      data: size === 256 ? artwork.png : createDib(artwork.rgba, size),
    });
  }
  return createIco(images);
}

async function renderSquare(size, { background = null, badge = null, scale }) {
  return renderComposition({
    width: size,
    height: size,
    background,
    foreground: colours.darkForeground,
    kind: "square",
    badge,
    badgeColour: badge ? colours[badge] : null,
    badgeGlyphColour: badge ? colours.darkForeground : null,
    badgeRimColour: badge ? colours.darkBackground : null,
    badgeStyle: badge ? trayBadgeStyle : null,
    scale,
  });
}

async function renderComposition(options) {
  const rendered = await page.evaluate((input) => {
    const image = document.getElementById("master");
    const bounds = window.volturaAirMasterBounds.master;
    const canvas = document.createElement("canvas");
    canvas.width = input.width;
    canvas.height = input.height;
    const context = canvas.getContext("2d", { alpha: true });
    context.imageSmoothingEnabled = true;
    context.imageSmoothingQuality = "high";

    if (input.background) {
      context.fillStyle = input.background;
      context.fillRect(0, 0, input.width, input.height);
    }

    const drawMaster = (centreX, centreY, maxWidth, maxHeight) => {
      const sourceWidth = bounds.maxX - bounds.minX + 1;
      const sourceHeight = bounds.maxY - bounds.minY + 1;
      const ratio = Math.min(maxWidth / sourceWidth, maxHeight / sourceHeight);
      const width = sourceWidth * ratio;
      const height = sourceHeight * ratio;
      context.drawImage(
        image,
        bounds.minX,
        bounds.minY,
        sourceWidth,
        sourceHeight,
        centreX - width / 2,
        centreY - height / 2,
        width,
        height,
      );
    };

    if (input.kind === "square") {
      drawMaster(input.width / 2, input.height / 2, input.width * input.scale, input.height * input.scale);
      if (input.badge) {
        const badgeStyle = input.badgeStyle;
        const centreX = Math.round(input.width * badgeStyle.centreX * 2) / 2;
        const centreY = Math.round(input.height * badgeStyle.centreY * 2) / 2;
        const outerRadius = input.width * badgeStyle.outerRadius;
        const rimWidth = Math.max(1, input.width * badgeStyle.rimWidth);
        const innerRadius = outerRadius - rimWidth;

        context.save();
        context.fillStyle = input.badgeRimColour;
        context.beginPath();
        context.arc(centreX, centreY, outerRadius, 0, Math.PI * 2);
        context.fill();

        context.fillStyle = input.badgeColour;
        context.beginPath();
        context.arc(centreX, centreY, innerRadius, 0, Math.PI * 2);
        context.fill();

        context.strokeStyle = input.badgeGlyphColour;
        context.lineWidth = Math.max(1.25, input.width * badgeStyle.glyphWidth);
        context.lineCap = "round";
        context.lineJoin = "round";
        context.beginPath();
        if (input.badge === "connected") {
          context.moveTo(centreX - innerRadius * 0.36, centreY + innerRadius * 0.02);
          context.lineTo(centreX - innerRadius * 0.10, centreY + innerRadius * 0.27);
          context.lineTo(centreX + innerRadius * 0.40, centreY - innerRadius * 0.30);
        } else {
          context.moveTo(centreX - innerRadius * 0.32, centreY - innerRadius * 0.32);
          context.lineTo(centreX + innerRadius * 0.32, centreY + innerRadius * 0.32);
          context.moveTo(centreX + innerRadius * 0.32, centreY - innerRadius * 0.32);
          context.lineTo(centreX - innerRadius * 0.32, centreY + innerRadius * 0.32);
        }
        context.stroke();
        context.restore();
      }
    } else if (input.kind === "installer-header") {
      drawMaster(input.width / 2, input.height / 2, 48, 48);
    } else if (input.kind === "installer-welcome") {
      drawMaster(input.width / 2, 125, 118, 118);
      context.fillStyle = input.foreground;
      context.font = '600 19px Arial, sans-serif';
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText("Voltura Air", input.width / 2, 211);
    } else if (input.kind === "apple-startup") {
      const portrait = input.height >= input.width;
      const iconSize = Math.min(
        input.width * (portrait ? 0.38 : 0.24),
        input.height * (portrait ? 0.24 : 0.42),
      );
      const fontSize = Math.max(32, Math.round(Math.min(input.width, input.height) * 0.045));
      const gap = Math.round(fontSize * 0.8);
      const groupHeight = iconSize + gap + fontSize;
      const iconCentreY = (input.height - groupHeight) / 2 + iconSize / 2;
      drawMaster(input.width / 2, iconCentreY, iconSize, iconSize);
      context.fillStyle = input.foreground;
      context.font = `600 ${fontSize}px Arial, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText("Voltura Air", input.width / 2, iconCentreY + iconSize / 2 + gap + fontSize / 2);
    } else {
      throw new Error(`Unknown composition kind: ${input.kind}`);
    }

    const pixels = context.getImageData(0, 0, input.width, input.height).data;
    let opaque = true;
    for (let offset = 3; offset < pixels.length; offset += 4) {
      if (pixels[offset] !== 255) {
        opaque = false;
        break;
      }
    }

    let rgba = "";
    if (input.kind !== "apple-startup") {
      for (let offset = 0; offset < pixels.length; offset += 32768) {
        rgba += String.fromCharCode(...pixels.subarray(offset, offset + 32768));
      }
    }

    return {
      png: canvas.toDataURL("image/png").split(",")[1],
      rgba: rgba ? btoa(rgba) : null,
      opaque,
    };
  }, options);

  return {
    png: Buffer.from(rendered.png, "base64"),
    rgba: rendered.rgba ? Buffer.from(rendered.rgba, "base64") : null,
    opaque: rendered.opaque,
  };
}

async function writeSvg(buffer, targets) {
  await writeOutputs(buffer, targets, { kind: "svg" });
}

async function writePng(artwork, width, height, targets) {
  assertPng(artwork.png, width, height, targets[0]);
  const optimized = await optimizePng(artwork.png, width, height, artwork.opaque, targets[0]);
  await writeOutputs(optimized, targets, {
    kind: "png",
    width,
    height,
    opaque: artwork.opaque,
  });
}

async function optimizePng(buffer, width, height, opaque, label) {
  let pipeline = sharp(buffer);
  if (opaque) {
    pipeline = pipeline.removeAlpha();
  }

  const optimized = await pipeline
    .png({ adaptiveFiltering: true, compressionLevel: 9, palette: false })
    .toBuffer();
  assertPng(optimized, width, height, label);
  await assertPngEncoding(optimized, opaque, label);

  if (opaque) {
    await assertOpaquePngPixelEquivalent(buffer, optimized, width, height, label);
  }

  return optimized;
}

async function assertPngEncoding(buffer, opaque, label) {
  const metadata = await sharp(buffer).metadata();
  if (opaque && (metadata.hasAlpha || metadata.channels !== 3)) {
    throw new Error(`${label} must be an opaque RGB PNG without an alpha channel.`);
  }
  if (!opaque && !metadata.hasAlpha) {
    throw new Error(`${label} must preserve its alpha channel.`);
  }
}

async function assertOpaquePngPixelEquivalent(source, optimized, width, height, label) {
  const [sourcePixels, optimizedPixels] = await Promise.all([
    sharp(source).removeAlpha().raw().toBuffer({ resolveWithObject: true }),
    sharp(optimized).raw().toBuffer({ resolveWithObject: true }),
  ]);
  for (const image of [sourcePixels, optimizedPixels]) {
    if (image.info.width !== width || image.info.height !== height || image.info.channels !== 3) {
      throw new Error(`${label} pixel-equivalence check did not produce ${width}x${height} RGB data.`);
    }
  }
  if (!sourcePixels.data.equals(optimizedPixels.data)) {
    throw new Error(`${label} changed visible pixels during lossless PNG optimization.`);
  }
}

async function writeIco(buffer, sizes, targets) {
  assertIco(buffer, sizes, targets[0]);
  await writeOutputs(buffer, targets, { kind: "ico", sizes });
}

async function writeBmp(buffer, width, height, targets) {
  assertBmp24(buffer, width, height, targets[0]);
  await writeOutputs(buffer, targets, { kind: "bmp", width, height });
}

async function writeOutputs(buffer, targets, check) {
  for (const relativePath of targets) {
    const outputPath = path.join(repoRoot, relativePath);
    await mkdir(path.dirname(outputPath), { recursive: true });
    await writeFile(outputPath, buffer);
    outputChecks.push({ ...check, relativePath });
  }
}

async function validateWrittenOutputs() {
  for (const output of outputChecks) {
    const buffer = await readFile(path.join(repoRoot, output.relativePath));
    if (output.kind === "png") {
      assertPng(buffer, output.width, output.height, output.relativePath);
      await assertPngEncoding(buffer, output.opaque, output.relativePath);
    } else if (output.kind === "ico") {
      assertIco(buffer, output.sizes, output.relativePath);
    } else if (output.kind === "bmp") {
      assertBmp24(buffer, output.width, output.height, output.relativePath);
    } else if (!buffer.toString("utf8").startsWith("<svg")) {
      throw new Error(`${output.relativePath} is not an SVG file.`);
    }
  }
}

function startupFileName(device, theme, orientation) {
  return `${device.name}-${device.width}x${device.height}-${device.dpr}x-${theme}-${orientation}.png`;
}

function validateStartupDevices(devices) {
  if (!Array.isArray(devices) || devices.length === 0) {
    throw new Error("apple-startup-devices.json must contain at least one device.");
  }

  const names = new Set();
  for (const device of devices) {
    if (
      typeof device.name !== "string" ||
      !Number.isInteger(device.width) ||
      !Number.isInteger(device.height) ||
      !Number.isInteger(device.dpr) ||
      device.width <= 0 ||
      device.height <= device.width ||
      device.dpr <= 0 ||
      names.has(device.name)
    ) {
      throw new Error(`Invalid Apple startup device: ${JSON.stringify(device)}`);
    }
    names.add(device.name);
  }
}
