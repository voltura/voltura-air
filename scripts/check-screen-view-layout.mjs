import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import { createServer } from "vite";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const mobileRoot = path.join(root, "apps", "mobile-web");
const server = await createServer({
  configFile: path.join(mobileRoot, "vite.config.ts"),
  logLevel: "error",
  root: mobileRoot,
  server: { host: "127.0.0.1", port: 0 }
});

let browser;
try {
  await server.listen();
  const address = server.httpServer?.address();
  if (!address || typeof address === "string") {
    throw new Error("Screen View layout check could not resolve the Vite port.");
  }

  try {
    browser = await chromium.launch({ headless: true });
  } catch {
    browser = await chromium.launch({ channel: "chrome", headless: true });
  }

  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  await page.goto(`http://127.0.0.1:${address.port}/?screenPreview=1&mouseState=active`, { waitUntil: "networkidle" });
  await page.locator(".screen-view-direct-pointer.active").waitFor();

  const result = await page.evaluate(() => {
    const video = document.querySelector("video.screen-view-video");
    const overlay = document.querySelector(".screen-view-direct-pointer.active");
    if (!(video instanceof HTMLVideoElement) || !(overlay instanceof HTMLElement)) {
      return { error: "The active Screen View video and direct pointer overlay were not rendered." };
    }

    const videoBounds = video.getBoundingClientRect();
    const overlayBounds = overlay.getBoundingClientRect();
    const centerX = videoBounds.left + videoBounds.width / 2;
    const centerY = videoBounds.top + videoBounds.height / 2;
    const hitTarget = document.elementFromPoint(centerX, centerY);
    return {
      video: { width: videoBounds.width, height: videoBounds.height },
      overlay: { width: overlayBounds.width, height: overlayBounds.height },
      sameBounds:
        Math.abs(videoBounds.left - overlayBounds.left) <= 1 &&
        Math.abs(videoBounds.top - overlayBounds.top) <= 1 &&
        Math.abs(videoBounds.right - overlayBounds.right) <= 1 &&
        Math.abs(videoBounds.bottom - overlayBounds.bottom) <= 1,
      overlayReceivesCenterHit: hitTarget === overlay
    };
  });

  if ("error" in result || result.video.width <= 0 || result.video.height <= 0 ||
      result.overlay.width <= 0 || result.overlay.height <= 0 ||
      !result.sameBounds || !result.overlayReceivesCenterHit) {
    throw new Error(`Screen View direct pointer hit-testing failed: ${JSON.stringify(result)}`);
  }

  process.stdout.write(`Screen View direct pointer covers the ${result.video.width.toFixed(2)} x ${result.video.height.toFixed(2)} video and receives its center hit.\n`);
} finally {
  await browser?.close();
  await server.close();
}
