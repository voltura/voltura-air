import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import { officialScreens } from "./custom-screens/catalog.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const assets = path.join(root, "apps", "public-site", "screens", "assets");
const screens = new Map(officialScreens.map(definition => [definition.screen.id, definition.screen]));
const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? "/", "http://127.0.0.1");
    if (url.pathname === "/preview") {
      const screen = screens.get(url.searchParams.get("id"));
      if (!screen) {
        response.writeHead(404).end();
        return;
      }
      const packageJson = JSON.stringify({ packageVersion: 1, format: "voltura-air.custom-screen", screen })
        .replaceAll("<", "\\u003c");
      response.setHeader("content-type", "text/html; charset=utf-8");
      response.end(`<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/catalog-preview.css"></head><body><div id="root"></div><script id="catalog-screen-package" type="application/json">${packageJson}</script><script src="/catalog-preview.js"></script></body></html>`);
      return;
    }
    if (url.pathname === "/catalog-preview.css" || url.pathname === "/catalog-preview.js") {
      const filename = url.pathname.slice(1);
      response.setHeader("content-type", filename.endsWith(".css") ? "text/css" : "text/javascript");
      response.end(await readFile(path.join(assets, filename)));
      return;
    }
    response.writeHead(404).end();
  } catch (error) {
    response.writeHead(500).end(String(error));
  }
});

await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const address = server.address();
const baseUrl = `http://127.0.0.1:${address.port}`;
let browser;
try {
  browser = await chromium.launch({ headless: true });
} catch {
  browser = await chromium.launch({ channel: "chrome", headless: true });
}
try {
  const page = await browser.newPage();
  for (const definition of officialScreens) {
    const expectedButtons = definition.screen.sections.reduce((count, section) => count + section.buttons.length, 0);
    for (const viewport of [{ width: 360, height: 640 }, { width: 640, height: 360 }]) {
      await page.setViewportSize(viewport);
      await page.goto(`${baseUrl}/preview?id=${encodeURIComponent(definition.screen.id)}`);
      await page.locator(".custom-screen-preview-notice").waitFor();
      const result = await page.evaluate(expected => {
        const buttons = [...document.querySelectorAll(".custom-screen-button")];
        const clippedButtons = buttons.filter(button => {
          const rect = button.getBoundingClientRect();
          const section = button.closest(".custom-screen-section")?.getBoundingClientRect();
          const style = getComputedStyle(button);
          return style.display === "none" || style.visibility === "hidden" || rect.width <= 0 || rect.height <= 0 ||
            rect.left < -1 || rect.right > innerWidth + 1 ||
            (section && (rect.left < section.left - 1 || rect.right > section.right + 1 || rect.bottom > section.bottom + 1));
        }).map(button => button.getAttribute("aria-label"));
        const clippedLabels = buttons.flatMap(button => [...button.querySelectorAll(":scope > span:not(.custom-screen-pending)")]
          .filter(label => label.scrollWidth > label.clientWidth + 1 || label.scrollHeight > label.clientHeight + 1)
          .map(label => ({
            button: button.getAttribute("aria-label"),
            text: label.textContent,
            clientWidth: label.clientWidth,
            scrollWidth: label.scrollWidth,
            clientHeight: label.clientHeight,
            scrollHeight: label.scrollHeight
          })));
        return {
          buttonCount: buttons.length,
          clippedButtons,
          clippedLabels,
          horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
          expected
        };
      }, expectedButtons);
      if (result.buttonCount !== expectedButtons || result.clippedButtons.length > 0 ||
          result.clippedLabels.length > 0 || result.horizontalOverflow) {
        throw new Error(`${definition.screen.id} failed ${viewport.width}x${viewport.height}: ${JSON.stringify(result)}`);
      }
    }
  }
  process.stdout.write(`Checked ${officialScreens.length} Custom Screens in portrait and landscape.\n`);
} finally {
  await browser.close();
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
}
