import path from "node:path";
import { mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import { createServer } from "vite";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const mobileRoot = path.join(root, "apps", "mobile-web");
const server = await createServer({
  configFile: path.join(mobileRoot, "vite.config.ts"),
  logLevel: "error",
  root: mobileRoot,
  server: { host: "127.0.0.1", port: 0 },
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
  await page.goto(`http://127.0.0.1:${address.port}/?screenPreview=1&mouseState=active`, {
    waitUntil: "networkidle",
  });
  await page.locator(".screen-view-direct-pointer.active").waitFor();

  const result = await page.evaluate(() => {
    const video = document.querySelector("video.screen-view-video");
    const overlay = document.querySelector(".screen-view-direct-pointer.active");
    if (!(video instanceof HTMLVideoElement) || !(overlay instanceof HTMLElement)) {
      return {
        error: "The active Screen View video and direct pointer overlay were not rendered.",
      };
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
      overlayReceivesCenterHit: hitTarget === overlay,
    };
  });

  if (
    "error" in result ||
    result.video.width <= 0 ||
    result.video.height <= 0 ||
    result.overlay.width <= 0 ||
    result.overlay.height <= 0 ||
    !result.sameBounds ||
    !result.overlayReceivesCenterHit
  ) {
    throw new Error(`Screen View direct pointer hit-testing failed: ${JSON.stringify(result)}`);
  }

  process.stdout.write(
    `Screen View direct pointer covers the ${result.video.width.toFixed(2)} x ${result.video.height.toFixed(2)} video and receives its center hit.\n`,
  );
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  // Preview has no incoming media; allow the real local Sound handler to run.
  await page.evaluate(() => {
    HTMLMediaElement.prototype.play = async () => {};
  });
  await page.getByRole("button", { name: "Play PC sound", exact: true }).click();
  const output = process.env.SCREEN_VIEW_SCREENSHOT_DIR;
  if (output) await mkdir(output, { recursive: true });
  for (const viewport of [
    { width: 320, height: 740 },
    { width: 390, height: 844 },
    { width: 844, height: 390 },
  ]) {
    await page.setViewportSize(viewport);
    for (const fullscreen of [false, true]) {
      if (fullscreen)
        await page.getByRole("button", { name: "View PC screen full screen", exact: true }).click();
      const layout = await page.evaluate(() => {
        const stage = document.querySelector(".screen-view-stage").getBoundingClientRect();
        const buttons = [...document.querySelectorAll(".screen-view-top-actions > button")]
          .filter((button) => !button.hidden)
          .map((button) => {
            const bounds = button.getBoundingClientRect();
            return {
              name: button.getAttribute("aria-label"),
              x: bounds.x,
              y: bounds.y,
              right: bounds.right,
              width: bounds.width,
              height: bounds.height,
            };
          });
        const back = document.querySelector(".screen-view-icon-button").getBoundingClientRect();
        return {
          stage: { x: stage.x, right: stage.right, bottom: stage.bottom },
          buttons,
          backY: back.y,
          immersive: document
            .querySelector(".screen-view-workspace")
            .classList.contains("is-immersive"),
        };
      });
      const buttons = layout.buttons;
      if (
        buttons.length !== 7 ||
        buttons.some(
          (b, i) =>
            Math.abs(b.y - buttons[0].y) > 1 ||
            Math.abs(b.height - 40) > 1 ||
            b.x < layout.stage.x ||
            b.right > layout.stage.right ||
            (i > 0 && b.x < buttons[i - 1].right - 1),
        ) ||
        (!fullscreen && viewport.width > viewport.height && layout.backY < layout.stage.bottom - 1)
      ) {
        throw new Error(
          `Toolbar layout failed at ${JSON.stringify(viewport)}, fullscreen=${fullscreen}: ${JSON.stringify(layout)}`,
        );
      }
      if (output && viewport.width === 390 && !fullscreen)
        await page.screenshot({ path: path.join(output, "controls-shown.png") });
      const geometry = () =>
        page.evaluate(() => ({
          transform: document.querySelector(".screen-view-content").style.transform,
          bounds: [".screen-view-stage", ".screen-view-video", ".screen-view-overlay-actions"].map(
            (selector) => document.querySelector(selector).getBoundingClientRect().toJSON(),
          ),
          fullscreen: document.fullscreenElement !== null,
          scroll: [window.scrollX, window.scrollY],
        }));
      const scrollMode = page.getByRole("button", {
        name: "Two-finger mode: Scroll. Switch to Zoom",
      });
      if (await scrollMode.count()) await scrollMode.click();
      await page.evaluate(
        () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
      );
      await page.locator(".screen-view-stage").evaluate((stage) => {
        const bounds = stage.getBoundingClientRect();
        const touch = (id, x, y) =>
          new Touch({
            identifier: id,
            target: stage,
            clientX: bounds.x + x,
            clientY: bounds.y + y,
          });
        const dispatch = (type, touches) =>
          stage.dispatchEvent(
            new TouchEvent(type, {
              bubbles: true,
              cancelable: true,
              touches,
              targetTouches: touches,
            }),
          );
        dispatch("touchstart", [touch(1, 100, 100), touch(2, 160, 100)]);
        dispatch("touchmove", [touch(1, 60, 110), touch(2, 200, 110)]);
        dispatch("touchend", []);
      });
      await page.locator(".screen-view-content.zoomed").waitFor({ timeout: 3000 });
      const beforeKeyboard = await geometry();
      for (const label of ["Show device keyboard", "Show device numeric keyboard"]) {
        await page.getByRole("button", { name: label, exact: true }).click();
        const input = await page.locator(".screen-view-keyboard-input").evaluate((element) => ({
          focused: document.activeElement === element,
          mode: element.inputMode,
          opacity: getComputedStyle(element).opacity,
        }));
        if (
          !input.focused ||
          input.opacity !== "0" ||
          input.mode !== (label.includes("numeric") ? "numeric" : "text")
        )
          throw new Error("Device keyboard focus or hidden-input presentation failed");
        const afterKeyboard = await geometry();
        if (JSON.stringify(afterKeyboard) !== JSON.stringify(beforeKeyboard))
          throw new Error(
            `Device keyboard focus changed mirror geometry or fullscreen: ${JSON.stringify({ viewport, fullscreen, beforeKeyboard, afterKeyboard })}`,
          );
      }
      await page.locator(".screen-view-keyboard-input").evaluate((element) => element.blur());
      if (JSON.stringify(await geometry()) !== JSON.stringify(beforeKeyboard))
        throw new Error("Device keyboard dismissal changed mirror geometry or fullscreen");
      await page.getByRole("button", { name: "Hide controls", exact: true }).click();
      for (const label of [
        "Increase PC volume",
        "Decrease PC volume",
        "Mute PC sound",
        "Capture PC screenshot",
        "Start screen recording",
        "Show device keyboard",
        "Show device numeric keyboard",
      ]) {
        if (await page.getByRole("button", { name: label, exact: true }).count())
          throw new Error(`Hidden control is still accessible: ${label}`);
      }
      if (await page.getByRole("button", { name: /Two-finger mode:/ }).count())
        throw new Error("Scroll/Zoom remains accessible");
      if (output && viewport.width === 390 && !fullscreen)
        await page.screenshot({ path: path.join(output, "controls-hidden.png") });
      if (fullscreen)
        await page.getByRole("button", { name: "Exit full screen", exact: true }).click();
      await page.getByRole("button", { name: "Show controls", exact: true }).click();
    }
  }
  await page.getByRole("button", { name: "Mute PC sound", exact: true }).click();
  if (await page.getByRole("button", { name: "Increase PC volume", exact: true }).count())
    throw new Error("Muted volume control is visible");
  if (errors.length) throw new Error(`Browser errors: ${errors.join("; ")}`);
  process.stdout.write(
    "Screen View controls pass narrow portrait, portrait, landscape, fullscreen, hidden, and muted checks.\n",
  );
} finally {
  await browser?.close();
  await server.close();
}
