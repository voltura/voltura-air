import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { devUiDevices, getDevUiDevice } from "./dev-ui-devices.mjs";
import { verifyResponsivePresentationLayout } from "./dev-ui-presentation-check.mjs";
import {
  readPreferredClientPort,
  resolveCommand,
  stopChild,
  stopExistingHost,
  stopWindowsNodeListenersOnDevPorts
} from "./dev-shared.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tempDir = path.join(tmpdir(), "voltura-air-dev-ui");
const tempNodeDir = path.join(tempDir, "node");
const tempAppDataDir = path.join(tempDir, "appdata");
const tempArtifactsDir = path.join(tempDir, "artifacts");
const browserProfileDir = path.join(tempDir, "chrome-profile");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const clientPort = readPreferredClientPort();
const smokeTest = process.argv.includes("--smoke-test");
const hostStartupTimeoutMs = 120000;
const clientUrl = process.env.VOLTURA_AIR_CLIENT_URL ?? `http://127.0.0.1:${clientPort}`;
const clientHost = new URL(clientUrl).hostname;
const debugDevice = getDevUiDevice();
const childEnv = {
  ...process.env,
  VOLTURA_AIR_CLIENT_PORT: String(clientPort),
  VOLTURA_AIR_CLIENT_URL: clientUrl,
  VOLTURA_AIR_USE_VITE_CLIENT: "1"
};
const children = [];
let browserContext = null;
let shuttingDown = false;

main().catch((error) => {
  console.error(error);
  shutdown("SIGTERM", 1);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => shutdown(signal));
}

async function main() {
  if (process.platform !== "win32") {
    throw new Error("Voltura Air UI debug sessions must run on Windows because they launch the WPF host.");
  }

  await fs.mkdir(tempNodeDir, { recursive: true });
  await fs.mkdir(tempAppDataDir, { recursive: true });
  await fs.rm(path.join(tempAppDataDir, "Voltura Air"), { recursive: true, force: true });
  await fs.rm(tempArtifactsDir, { recursive: true, force: true });
  await fs.rm(browserProfileDir, { recursive: true, force: true });
  await fs.rm(pairingUrlFile, { force: true });
  await seedBrowserProfile(debugDevice);

  await ensureDebugDependencies();
  stopWindowsNodeListenersOnDevPorts(clientPort, 20);
  stopExistingHost();

  console.log("Starting Voltura Air UI debug session...");
  console.log(`Vite client: ${clientUrl}`);
  console.log(`Chrome device: ${debugDevice.title}`);
  console.log(`Debug storage: ${tempDir}`);

  children.push(spawnCommand(
    "node",
    ["../../node_modules/vite/bin/vite.js", "--host", clientHost, "--strictPort", "--port", String(clientPort)],
    childEnv,
    { cwd: path.join(repoRoot, "apps", "mobile-web") }));

  await waitForHttp(clientUrl, 30000);

  const hostArguments = [
    "run",
    "--artifacts-path",
    tempArtifactsDir,
    "--disable-build-servers",
    "--project",
    "apps/windows-host/VolturaAir.Host.csproj",
    "--",
    "--client-url",
    clientUrl,
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile,
    "--isolated-test-mode",
    "--enable-alpha-features"
  ];
  if (smokeTest) {
    hostArguments.push("--minimized");
  }

  children.push(spawnCommand("dotnet", hostArguments, {
    ...childEnv,
    APPDATA: tempAppDataDir
  }, { cwd: repoRoot }));

  const pairingUrl = await waitForTextFile(pairingUrlFile, hostStartupTimeoutMs);
  const requireFromTemp = createRequire(path.join(tempNodeDir, "package.json"));
  const { chromium } = requireFromTemp("playwright");
  const qrCode = requireFromTemp("qrcode");
  const page = await launchBrowser(chromium, qrCode, pairingUrl);

  if (smokeTest) {
    await verifySettingsDrawerLifecycle(page);
    await verifyTrackpadButtonLayout(page);
    await verifyKeyboardLayout(page);
    await verifyLandscapeSafeAreaLayouts(page);
    await verifyResponsivePowerLayout(page);
    await verifyResponsiveTextTransferLayout(page);
    await verifyResponsivePresentationLayout(page);
    await verifyResponsiveUrlOpenLayout(page);
    await verifyDisconnectedSavedPcReconnect(page);
    console.log("Voltura Air UI smoke test connected and passed settings drawer lifecycle, trackpad, keyboard and landscape safe-area layout, responsive Power sheet, text transfer, Presentation, URL opening, and saved-PC reconnect checks.");
    shutdown("SIGTERM", 0);
    return;
  }

  console.log("Chrome is open with the isolated local Voltura Air host.");
  console.log("Use Ctrl+Shift+M in DevTools to toggle the device toolbar.");
  console.log("Close Chrome or press Ctrl+C here to stop the debug session.");

  page.context().once("close", () => shutdown("SIGTERM", 0));
}

async function ensureDebugDependencies() {
  const packageJson = path.join(tempNodeDir, "package.json");
  if (!existsSync(packageJson)) {
    await fs.writeFile(packageJson, JSON.stringify({ private: true, type: "commonjs" }, null, 2));
  }

  if (existsSync(path.join(tempNodeDir, "node_modules", "playwright")) &&
      existsSync(path.join(tempNodeDir, "node_modules", "qrcode"))) {
    return;
  }

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", "playwright", "qrcode"], { cwd: tempNodeDir });
}

async function launchBrowser(chromium, qrCode, pairingUrl) {
  const url = new URL(pairingUrl);
  url.searchParams.set("debug", "1");

  browserContext = await launchPersistentContext(chromium);
  const page = browserContext.pages()[0] ?? await browserContext.newPage();
  await applyDeviceEmulation(page, debugDevice);
  await page.goto(url.href, { waitUntil: "networkidle" });
  if (smokeTest) {
    await page.getByRole("button", { name: "Pair", exact: true }).waitFor({ state: "visible", timeout: 10000 });
    const qrImageFile = path.join(tempDir, "pairing-qr.png");
    await qrCode.toFile(qrImageFile, pairingUrl, { errorCorrectionLevel: "H", margin: 4, width: 1024 });
    const ordinaryUrl = new URL(clientUrl);
    ordinaryUrl.searchParams.set("debug", "1");
    await page.goto(ordinaryUrl.href, { waitUntil: "networkidle" });
    await page.locator('input[type="file"][accept="image/*"]').first().setInputFiles(qrImageFile);
    await page.getByRole("button", { name: "Pair", exact: true }).waitFor({ state: "visible", timeout: 10000 });
    await clickPairIfPresent(page);
    await waitForConnected(page);
  } else {
    await clickPairIfPresent(page);
  }
  return page;
}

async function verifyTrackpadButtonLayout(page) {
  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "phone landscape", width: 852, height: 393 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(() => {
      const mode = document.querySelector(".trackpad-mode");
      const row = document.querySelector(".trackpad-mode > .mouse-buttons");
      const buttons = row ? Array.from(row.querySelectorAll("button")) : [];
      if (!(mode instanceof HTMLElement) || !(row instanceof HTMLElement) || buttons.length !== 2) {
        return { error: "Trackpad click buttons were not visible." };
      }

      const modeBounds = mode.getBoundingClientRect();
      const rowBounds = row.getBoundingClientRect();
      const buttonBounds = buttons.map((button) => button.getBoundingClientRect());
      return {
        equalButtonWidths: Math.abs(buttonBounds[0].width - buttonBounds[1].width) <= 1,
        fillsModeWidth: Math.abs(rowBounds.width - modeBounds.width) <= 1,
        fillsRowWidth: Math.abs(buttonBounds[0].left - rowBounds.left) <= 1 && Math.abs(buttonBounds[1].right - rowBounds.right) <= 1,
        rowDisplay: getComputedStyle(row).display
      };
    });

    if ("error" in result || !result.equalButtonWidths || !result.fillsModeWidth || !result.fillsRowWidth || result.rowDisplay !== "grid") {
      throw new Error(`Trackpad click button layout failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }
}

async function verifyKeyboardLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Keyboard mode", exact: true }).click();
  await page.getByRole("tab", { name: "Show numeric keyboard", exact: true }).click();
  await page.getByRole("tab", { name: "Show regular keyboard", exact: true }).click();

  const portrait = await page.evaluate(() => {
    const mode = document.querySelector(".keyboard-mode");
    const primaryKeys = document.querySelector(".keyboard-primary-keys");
    const input = document.querySelector(".keyboard-mode textarea");
    const liveTyping = document.querySelector(".live-typing-switch");
    const selector = document.querySelector(".keyboard-input-mode-buttons");
    const selectorButtons = selector ? Array.from(selector.querySelectorAll("button")) : [];
    const primaryButtons = primaryKeys ? Array.from(primaryKeys.querySelectorAll("button")) : [];
    if (!(mode instanceof HTMLElement) || !(primaryKeys instanceof HTMLElement) ||
        !(input instanceof HTMLTextAreaElement) || !(liveTyping instanceof HTMLElement) ||
        !(selector instanceof HTMLElement) || selectorButtons.length !== 2 || primaryButtons.length < 7) {
      return { error: "Keyboard controls were not visible." };
    }

    const inputBounds = input.getBoundingClientRect();
    const liveTypingBounds = liveTyping.getBoundingClientRect();
    const selectorButtonBounds = selectorButtons.map((button) => button.getBoundingClientRect());
    const primaryButtonBounds = primaryButtons.map((button) => button.getBoundingClientRect());
    const rowTops = new Set(primaryButtonBounds.map((bounds) => Math.round(bounds.top)));
    const inputStyle = getComputedStyle(input);
    return {
      inputFocused: document.activeElement === input,
      inputOutlineWidth: inputStyle.outlineWidth,
      inputOutlineStyle: inputStyle.outlineStyle,
      inputTopAligned: Math.abs(inputBounds.top - liveTypingBounds.top) <= 1,
      inputBottomAligned: Math.abs(inputBounds.bottom - liveTypingBounds.bottom) <= 1,
      primaryDisplay: getComputedStyle(primaryKeys).display,
      primaryRowCount: rowTops.size,
      selectorDisplay: getComputedStyle(selector).display,
      selectorGap: selectorButtonBounds[1].left - selectorButtonBounds[0].right
    };
  });

  if ("error" in portrait || !portrait.inputFocused ||
      (portrait.inputOutlineWidth !== "0px" && portrait.inputOutlineStyle !== "none") ||
      !portrait.inputTopAligned || !portrait.inputBottomAligned || portrait.primaryDisplay !== "grid" ||
      portrait.primaryRowCount < 2 || portrait.selectorDisplay !== "grid" || Math.abs(portrait.selectorGap) > 1) {
    throw new Error(`Keyboard portrait layout failed: ${JSON.stringify(portrait)}`);
  }

  const sleepButton = page.getByRole("button", { name: "Sleep", exact: true });
  if (await sleepButton.count() === 1) {
    await sleepButton.click();
    const sleepDialog = page.getByRole("dialog", { name: "Put PC to sleep?", exact: true });
    const confirmation = await sleepDialog.evaluate((dialog) => {
      const cancel = dialog.querySelector(".confirmation-dialog-cancel");
      const confirm = dialog.querySelector(".confirmation-dialog-confirm");
      if (!(cancel instanceof HTMLButtonElement) || !(confirm instanceof HTMLButtonElement)) {
        return { error: "Sleep confirmation buttons were not visible." };
      }

      return {
        cancelFocused: document.activeElement === cancel,
        cancelBorder: getComputedStyle(cancel).borderTopColor,
        confirmBorder: getComputedStyle(confirm).borderTopColor,
        confirmOutlineWidth: getComputedStyle(confirm).outlineWidth
      };
    });

    if ("error" in confirmation || !confirmation.cancelFocused || confirmation.cancelBorder === confirmation.confirmBorder || confirmation.confirmOutlineWidth !== "0px") {
      throw new Error(`Sleep confirmation default failed: ${JSON.stringify(confirmation)}`);
    }
    await sleepDialog.getByRole("button", { name: "Cancel", exact: true }).click();
  }

  await page.setViewportSize({ width: 1024, height: 768 });
  const landscape = await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    const mode = document.querySelector(".keyboard-mode");
    if (!(shell instanceof HTMLElement) || !(mode instanceof HTMLElement)) {
      return { error: "Keyboard landscape controls were not visible." };
    }

    const shellStyle = getComputedStyle(shell);
    const availableWidth = shell.clientWidth - Number.parseFloat(shellStyle.paddingLeft) - Number.parseFloat(shellStyle.paddingRight);
    return {
      availableWidth,
      modeWidth: mode.getBoundingClientRect().width
    };
  });

  if ("error" in landscape || Math.abs(landscape.modeWidth - landscape.availableWidth) > 1) {
    throw new Error(`Keyboard landscape width failed: ${JSON.stringify(landscape)}`);
  }
}

async function verifyLandscapeSafeAreaLayouts(page) {
  await page.setViewportSize({ width: 852, height: 393 });
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  const splitSection = page.locator('[data-settings-section="split"]');
  await splitSection.locator("> summary").click();
  const splitCheckbox = splitSection.getByRole("checkbox", { name: "Enable split mode", exact: true });
  if (!await splitCheckbox.isChecked()) {
    await splitCheckbox.click();
  }
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  await page.waitForSelector(".app-shell.split-mode-active");

  const splitLayout = await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    const keyboardPane = document.querySelector(".split-keyboard-pane");
    const keyboard = keyboardPane?.querySelector(".keyboard-mode");
    const finalButtons = keyboardPane ? Array.from(keyboardPane.querySelectorAll(".app-switch-row button")) : [];
    const trackpadSurface = document.querySelector(".split-trackpad-pane .trackpad-surface");
    const expandButton = trackpadSurface?.querySelector(".trackpad-expand-button");
    if (!(shell instanceof HTMLElement) || !(keyboardPane instanceof HTMLElement) ||
        !(keyboard instanceof HTMLElement) || finalButtons.length !== 2 ||
        !(trackpadSurface instanceof HTMLElement) || !(expandButton instanceof HTMLElement)) {
      return { error: "Split keyboard or trackpad controls were not visible." };
    }

    shell.style.setProperty("--mode-bottom-safe-area", "32px");
    shell.style.setProperty("--mode-inline-safe-end", "120px");
    keyboardPane.scrollTop = keyboardPane.scrollHeight;
    keyboard.scrollTop = keyboard.scrollHeight;

    const paneBounds = keyboardPane.getBoundingClientRect();
    const finalButtonBounds = finalButtons.map((button) => button.getBoundingClientRect());
    const surfaceBounds = trackpadSurface.getBoundingClientRect();
    const expandBounds = expandButton.getBoundingClientRect();
    return {
      expandRightGap: surfaceBounds.right - expandBounds.right,
      finalButtonBottomGap: paneBounds.bottom - Math.max(...finalButtonBounds.map((bounds) => bounds.bottom)),
      finalButtonMinHeight: Number.parseFloat(getComputedStyle(finalButtons[0]).minHeight),
      keyboardPaddingBottom: Number.parseFloat(getComputedStyle(keyboard).paddingBottom),
      panePaddingBottom: Number.parseFloat(getComputedStyle(keyboardPane).paddingBottom)
    };
  });

  if ("error" in splitLayout || Math.abs(splitLayout.expandRightGap - 10) > 1 ||
      Math.abs(splitLayout.finalButtonBottomGap) > 1 || splitLayout.finalButtonMinHeight < 80 ||
      splitLayout.keyboardPaddingBottom !== 0 || splitLayout.panePaddingBottom !== 0) {
    throw new Error(`Split landscape safe-area layout failed: ${JSON.stringify(splitLayout)}`);
  }

  await page.getByRole("button", { name: "Expand trackpad", exact: true }).click();
  const expandedLayout = await page.evaluate(() => {
    const button = document.querySelector(".split-trackpad-pane .trackpad-mode.expanded .trackpad-expand-button");
    if (!(button instanceof HTMLElement)) {
      return { error: "Expanded split trackpad toggle was not visible." };
    }

    return { rightGap: window.innerWidth - button.getBoundingClientRect().right };
  });
  if ("error" in expandedLayout || Math.abs(expandedLayout.rightGap - 10) > 1) {
    throw new Error(`Expanded split trackpad safe-area layout failed: ${JSON.stringify(expandedLayout)}`);
  }
  await page.getByRole("button", { name: "Restore trackpad", exact: true }).click();

  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Remote mode", exact: true }).click();
  await page.setViewportSize({ width: 852, height: 393 });
  const remoteVolumeLayout = await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    const section = document.querySelector(".remote-volume-section");
    const grid = section?.querySelector(".remote-volume-grid");
    const buttons = grid ? Array.from(grid.querySelectorAll("button")) : [];
    if (!(shell instanceof HTMLElement) || !(section instanceof HTMLElement) ||
        !(grid instanceof HTMLElement) || buttons.length !== 3) {
      return { error: "Remote landscape volume controls were not visible." };
    }

    shell.style.setProperty("--mode-inline-safe-end", "120px");
    const sectionStyle = getComputedStyle(section);
    const gridBounds = grid.getBoundingClientRect();
    const buttonBounds = buttons.map((button) => button.getBoundingClientRect());
    return {
      equalButtonWidths: Math.max(...buttonBounds.map((bounds) => bounds.width)) - Math.min(...buttonBounds.map((bounds) => bounds.width)) <= 1,
      gridRightGap: section.getBoundingClientRect().right - Number.parseFloat(sectionStyle.borderRightWidth) - Number.parseFloat(sectionStyle.paddingRight) - gridBounds.right,
      sectionPaddingRight: Number.parseFloat(sectionStyle.paddingRight)
    };
  });
  if ("error" in remoteVolumeLayout || !remoteVolumeLayout.equalButtonWidths ||
      Math.abs(remoteVolumeLayout.gridRightGap) > 1 || Math.abs(remoteVolumeLayout.sectionPaddingRight - 9) > 1) {
    throw new Error(`Remote landscape volume safe-area layout failed: ${JSON.stringify(remoteVolumeLayout)}`);
  }

  await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    if (shell instanceof HTMLElement) {
      shell.style.removeProperty("--mode-bottom-safe-area");
      shell.style.removeProperty("--mode-inline-safe-end");
    }
  });
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Trackpad mode", exact: true }).click();
}

async function verifySettingsDrawerLifecycle(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  const drawer = page.locator(".settings-drawer");
  const readState = () => drawer.evaluate((dialog) => ({
    display: getComputedStyle(dialog).display,
    open: dialog instanceof HTMLDialogElement && dialog.open,
    width: dialog.getBoundingClientRect().width
  }));

  const initiallyClosed = await readState();
  if (initiallyClosed.open || initiallyClosed.display !== "none" || initiallyClosed.width !== 0) {
    throw new Error(`Settings drawer should start closed and hidden: ${JSON.stringify(initiallyClosed)}`);
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  const opened = await readState();
  if (!opened.open || opened.display === "none" || opened.width < 300) {
    throw new Error(`Settings drawer did not open visibly: ${JSON.stringify(opened)}`);
  }

  const initialFocus = await page.evaluate(() => {
    const dialog = document.querySelector(".settings-drawer");
    const backdrop = document.querySelector(".settings-drawer-light-dismiss");
    return {
      backdropTabIndex: backdrop instanceof HTMLElement ? backdrop.tabIndex : null,
      drawerFocused: document.activeElement === dialog
    };
  });
  if (!initialFocus.drawerFocused || initialFocus.backdropTabIndex !== -1) {
    throw new Error(`Settings drawer initial focus is incorrect: ${JSON.stringify(initialFocus)}`);
  }

  await page.keyboard.press("Tab");
  const firstTabTarget = await page.evaluate(() => document.activeElement?.getAttribute("aria-label"));
  if (firstTabTarget !== "Close menu") {
    throw new Error(`Settings drawer first Tab target should be the close button, received: ${JSON.stringify(firstTabTarget)}`);
  }

  const connectionSection = page.locator('[data-settings-section="connection"]');
  const trackpadSection = page.locator('[data-settings-section="trackpad"]');
  const trackpadSummary = trackpadSection.locator("> summary");
  await connectionSection.locator("> summary").click();
  await trackpadSummary.evaluate((summary) => summary.scrollIntoView({ block: "end" }));
  await trackpadSummary.click();
  await page.waitForFunction(() => {
    const scrollRegion = document.querySelector(".settings-drawer-scroll-region");
    const connection = document.querySelector('[data-settings-section="connection"]');
    const trackpad = document.querySelector('[data-settings-section="trackpad"]');
    const summary = trackpad?.querySelector("summary");
    const firstControl = trackpad?.querySelector(".settings-section-body button, .settings-section-body input, .settings-section-body select, .settings-section-body textarea, .settings-section-body a[href], .settings-section-body [tabindex]");
    if (!(scrollRegion instanceof HTMLElement)
      || !(connection instanceof HTMLDetailsElement)
      || !(trackpad instanceof HTMLDetailsElement)
      || !(summary instanceof HTMLElement)
      || !(firstControl instanceof HTMLElement)
      || connection.open
      || !trackpad.open) {
      return false;
    }

    const regionBounds = scrollRegion.getBoundingClientRect();
    const summaryBounds = summary.getBoundingClientRect();
    const controlBounds = firstControl.getBoundingClientRect();
    return summaryBounds.top >= regionBounds.top - 1
      && summaryBounds.bottom <= regionBounds.bottom + 1
      && controlBounds.top >= regionBounds.top - 1
      && controlBounds.bottom <= regionBounds.bottom + 1;
  });

  const accordionState = await page.evaluate(() => {
    const scrollRegion = document.querySelector(".settings-drawer-scroll-region");
    const connection = document.querySelector('[data-settings-section="connection"]');
    const trackpad = document.querySelector('[data-settings-section="trackpad"]');
    const summary = trackpad?.querySelector("summary");
    const firstControl = trackpad?.querySelector(".settings-section-body button, .settings-section-body input, .settings-section-body select, .settings-section-body textarea, .settings-section-body a[href], .settings-section-body [tabindex]");
    if (!(scrollRegion instanceof HTMLElement)
      || !(connection instanceof HTMLDetailsElement)
      || !(trackpad instanceof HTMLDetailsElement)
      || !(summary instanceof HTMLElement)
      || !(firstControl instanceof HTMLElement)) {
      return { error: "Settings accordion controls were not visible." };
    }

    const regionBounds = scrollRegion.getBoundingClientRect();
    const summaryBounds = summary.getBoundingClientRect();
    const controlBounds = firstControl.getBoundingClientRect();
    return {
      connectionOpen: connection.open,
      firstControlVisible: controlBounds.top >= regionBounds.top - 1 && controlBounds.bottom <= regionBounds.bottom + 1,
      summaryFocused: document.activeElement === summary,
      summaryVisible: summaryBounds.top >= regionBounds.top - 1 && summaryBounds.bottom <= regionBounds.bottom + 1,
      trackpadOpen: trackpad.open
    };
  });
  if ("error" in accordionState
    || accordionState.connectionOpen
    || !accordionState.trackpadOpen
    || !accordionState.summaryVisible
    || !accordionState.firstControlVisible
    || !accordionState.summaryFocused) {
    throw new Error(`Settings accordion assisted reveal failed: ${JSON.stringify(accordionState)}`);
  }

  await page.mouse.click(380, 426);
  const closedByBackdrop = await readState();
  if (closedByBackdrop.open || closedByBackdrop.display !== "none" || closedByBackdrop.width !== 0) {
    throw new Error(`Settings drawer did not close from its backdrop: ${JSON.stringify(closedByBackdrop)}`);
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  const closedByButton = await readState();
  if (closedByButton.open || closedByButton.display !== "none" || closedByButton.width !== 0) {
    throw new Error(`Settings drawer did not close from its close button: ${JSON.stringify(closedByButton)}`);
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.keyboard.press("Escape");
  const closedByEscape = await readState();
  if (closedByEscape.open || closedByEscape.display !== "none" || closedByEscape.width !== 0) {
    throw new Error(`Settings drawer did not close with Escape: ${JSON.stringify(closedByEscape)}`);
  }
}

async function verifyResponsivePowerLayout(page) {
  await page.getByRole("button", { name: "Remote mode", exact: true }).click();
  await page.getByRole("button", { name: "Power", exact: true }).click();

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "compact phone portrait", width: 375, height: 667 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet portrait", width: 768, height: 1024 },
    { name: "tablet landscape", width: 1024, height: 768 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(() => {
      const sheet = document.querySelector(".power-sheet");
      const content = document.querySelector(".power-sheet-content");
      const rows = Array.from(document.querySelectorAll(".power-action-row"));
      if (!(sheet instanceof HTMLElement) || !(content instanceof HTMLElement)) {
        return { error: "Power sheet was not visible." };
      }

      const bounds = sheet.getBoundingClientRect();
      return {
        actionCount: rows.length,
        contentScrolls: content.scrollHeight > content.clientHeight + 1,
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minActionHeight: Math.min(...rows.map((row) => row.getBoundingClientRect().height)),
        outsideViewport: bounds.left < -1 || bounds.top < -1 || bounds.right > window.innerWidth + 1 || bounds.bottom > window.innerHeight + 1
      };
    });

    if ("error" in result || result.actionCount !== 8 || result.horizontalOverflow || result.minActionHeight < 44 || result.outsideViewport) {
      throw new Error(`Responsive Power sheet check failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }
}

async function verifyResponsiveTextTransferLayout(page) {
  await page.locator(".power-sheet-close").click();
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.locator("summary").filter({ hasText: /^App$/ }).click();
  const fourthModeControl = page.locator(".fourth-mode-select");
  const fourthModeMetrics = await fourthModeControl.evaluate((control) => ({
    fontSize: Number.parseFloat(getComputedStyle(control).fontSize),
    height: control.getBoundingClientRect().height,
    width: control.getBoundingClientRect().width
  }));
  if (fourthModeMetrics.fontSize < 16 || fourthModeMetrics.height < 48 || fourthModeMetrics.width < 240) {
    throw new Error(`Fourth mode button selector is too small: ${JSON.stringify(fourthModeMetrics)}`);
  }
  await page.getByRole("button", { name: "Send text to PC", exact: true }).click();
  await page.getByRole("button", { name: "Use device keyboard", exact: true }).click();
  await page.getByLabel("Text to send").fill("Responsive text transfer check");
  const savedSnippets = page.locator(".saved-snippets");
  const snippetsStartFolded = await savedSnippets.evaluate((details) => details instanceof HTMLDetailsElement && !details.open);
  await page.locator(".saved-snippets > summary").click();
  await page.getByLabel("Snippet name").fill("Smoke snippet");
  await page.getByRole("button", { name: "Save current text", exact: true }).click();

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet landscape", width: 1024, height: 768 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(() => {
      const editor = document.querySelector(".text-transfer-editor textarea");
      const editorSurface = document.querySelector(".text-transfer-editor-surface");
      const editorField = document.querySelector(".text-transfer-editor");
      const editorLabel = document.querySelector(".text-transfer-editor > label");
      const actions = document.querySelector(".text-transfer-actions");
      const sendButtons = Array.from(document.querySelectorAll(".text-transfer-actions button"));
      const snippetInput = document.querySelector(".snippet-save-row input");
      const saveButton = document.querySelector(".snippet-save-row button");
      const snippetActions = Array.from(document.querySelectorAll(".saved-snippets li button:not(.snippet-load)"));
      if (!(editor instanceof HTMLTextAreaElement) || !(editorSurface instanceof HTMLElement) || !(editorField instanceof HTMLElement) ||
          !(editorLabel instanceof HTMLElement) || !(actions instanceof HTMLElement) || sendButtons.length !== 2 ||
          !(snippetInput instanceof HTMLInputElement) || !(saveButton instanceof HTMLButtonElement)) {
        return { error: "Text transfer controls were not visible." };
      }

      const editorSurfaceBounds = editorSurface.getBoundingClientRect();
      const editorFieldBounds = editorField.getBoundingClientRect();
      const editorLabelBounds = editorLabel.getBoundingClientRect();
      const actionBounds = actions.getBoundingClientRect();
      const sendButtonBounds = sendButtons.map((button) => button.getBoundingClientRect());
      return {
        backButtonPresent: document.querySelector(".text-transfer-mode .tool-back-button") !== null,
        editorLabelGap: editorSurfaceBounds.top - editorLabelBounds.bottom,
        editorMisaligned: Math.abs(editorSurfaceBounds.left - editorFieldBounds.left) > 1 || Math.abs(editorSurfaceBounds.width - editorFieldBounds.width) > 2,
        editorOverlapsActions: editorSurfaceBounds.bottom > actionBounds.top + 1,
        editorUsesTrackpadGrid: getComputedStyle(editorSurface).backgroundImage !== "none",
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        maxSnippetActionHeight: Math.max(...snippetActions.map((button) => button.getBoundingClientRect().height)),
        sendButtonsShareRow: Math.abs(sendButtonBounds[0].top - sendButtonBounds[1].top) <= 1,
        snippetInputOpaque: getComputedStyle(snippetInput).backgroundColor !== "rgba(0, 0, 0, 0)",
        snippetInputWidth: snippetInput.getBoundingClientRect().width
      };
    });

    if (!snippetsStartFolded || "error" in result || result.backButtonPresent || (viewport.name === "phone portrait" && result.editorLabelGap > 5) || result.editorMisaligned || result.editorOverlapsActions ||
        result.editorUsesTrackpadGrid || result.horizontalOverflow || result.maxSnippetActionHeight > 45 || !result.sendButtonsShareRow ||
        !result.snippetInputOpaque || result.snippetInputWidth < 160) {
      throw new Error(`Responsive text transfer check failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }

  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Rename", exact: true }).click();
  const renameDialog = page.getByRole("dialog", { name: "Rename snippet", exact: true });
  const dialogMetrics = await renameDialog.evaluate((dialog) => {
    const standardInput = document.querySelector(".snippet-save-row input");
    const input = dialog.querySelector("input");
    const buttons = Array.from(dialog.querySelectorAll(".modal-dialog-actions button"));
    const closeButton = dialog.querySelector(".modal-dialog-close");
    if (!(standardInput instanceof HTMLInputElement) || !(input instanceof HTMLInputElement) ||
        !(closeButton instanceof HTMLButtonElement) || buttons.length !== 2) {
      return { error: "Themed snippet dialog controls were not visible." };
    }
    return {
      buttonsUseElements: buttons.every((button) => button instanceof HTMLButtonElement),
      closeButtonAccessible: closeButton.getAttribute("aria-label") === "Close Rename snippet",
      fontMatchesApp: getComputedStyle(dialog).fontFamily === getComputedStyle(document.body).fontFamily,
      inputMatchesTheme: getComputedStyle(input).backgroundColor === getComputedStyle(standardInput).backgroundColor,
      minButtonHeight: Math.min(...buttons.map((button) => button.getBoundingClientRect().height)),
      opaqueBackground: getComputedStyle(dialog).backgroundColor !== "rgba(0, 0, 0, 0)"
    };
  });
  if ("error" in dialogMetrics || !dialogMetrics.buttonsUseElements || !dialogMetrics.closeButtonAccessible || !dialogMetrics.fontMatchesApp ||
      !dialogMetrics.inputMatchesTheme || dialogMetrics.minButtonHeight < 48 || !dialogMetrics.opaqueBackground) {
    throw new Error(`Themed snippet dialog check failed: ${JSON.stringify(dialogMetrics)}`);
  }
  await renameDialog.getByRole("button", { name: "Cancel", exact: true }).click();
}

async function verifyResponsiveUrlOpenLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Remote mode", exact: true }).click();
  await page.getByRole("button", { name: "Fn", exact: true }).click();
  await page.getByRole("button", { name: "Open URL", exact: true }).click();
  const urlDialog = page.getByRole("dialog", { name: "Open URL on PC", exact: true });
  const input = urlDialog.getByRole("textbox", { name: "Web address", exact: true });

  if (await input.count() === 0) {
    const permissionMessage = urlDialog.getByText("Allow URL opening in the PC permissions first.", { exact: true });
    if (await permissionMessage.count() !== 1 || await urlDialog.getByRole("button", { name: "Open", exact: true }).count() !== 0) {
      throw new Error("URL controls were not hidden with the PC permission disabled.");
    }
    return;
  }

  await input.fill("javascript:alert(1)");
  if (!await urlDialog.getByRole("button", { name: "Open", exact: true }).isDisabled() ||
      await urlDialog.getByText("Use an HTTP or HTTPS web address.", { exact: true }).count() !== 1) {
    throw new Error("Invalid URL drafts did not disable Open with clear feedback.");
  }

  await urlDialog.getByRole("button", { name: "About Opening URLs on PC", exact: true }).click();
  const infoDialog = page.getByRole("dialog", { name: "Opening URLs on PC", exact: true });
  const infoMetrics = await infoDialog.evaluate((dialog) => {
    const ok = dialog.querySelector(".info-dialog-actions button");
    if (!(ok instanceof HTMLButtonElement)) {
      return { error: "Missing OK button." };
    }

    const dialogBounds = dialog.getBoundingClientRect();
    return {
      height: dialogBounds.height,
      okOffsetLeft: ok.getBoundingClientRect().left - dialogBounds.left,
      text: dialog.textContent ?? ""
    };
  });
  if ("error" in infoMetrics || infoMetrics.height < 250 || infoMetrics.okOffsetLeft > 40 ||
      !infoMetrics.text.includes("Addresses without a scheme use HTTPS")) {
    throw new Error(`URL information dialog check failed: ${JSON.stringify(infoMetrics)}`);
  }
  await infoDialog.getByRole("button", { name: "OK", exact: true }).click();
  await input.fill("example.com/page?q=responsive-test");

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "compact phone portrait", width: 375, height: 667 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet landscape", width: 1024, height: 768 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await input.focus();
    const result = await page.evaluate(() => {
      const form = document.querySelector(".remote-url-dialog form");
      const field = document.querySelector("#remote-url-draft");
      const button = document.querySelector(".remote-url-dialog-primary");
      if (!(form instanceof HTMLElement) || !(field instanceof HTMLInputElement) || !(button instanceof HTMLButtonElement)) {
        return { error: "URL opening controls were not visible." };
      }

      const bounds = form.getBoundingClientRect();
      const fieldStyle = getComputedStyle(field);
      return {
        buttonHeight: button.getBoundingClientRect().height,
        draft: field.value,
        fieldWidth: field.getBoundingClientRect().width,
        fieldBackground: fieldStyle.backgroundColor,
        fieldBorderColor: fieldStyle.borderColor,
        fieldOutlineColor: fieldStyle.outlineColor,
        fieldOutlineOffset: fieldStyle.outlineOffset,
        formBackground: getComputedStyle(form).backgroundColor,
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        outsideViewport: bounds.left < -1 || bounds.right > window.innerWidth + 1
      };
    });

    if ("error" in result || result.buttonHeight < 44 || result.fieldWidth < 160 || result.fieldBackground === result.formBackground ||
        result.fieldBorderColor !== result.fieldOutlineColor || result.fieldOutlineOffset !== "0px" || result.horizontalOverflow || result.outsideViewport ||
        result.draft !== "example.com/page?q=responsive-test") {
      throw new Error(`Responsive URL opening check failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }
}

async function verifyDisconnectedSavedPcReconnect(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  const urlDialog = page.getByRole("dialog", { name: "Open URL on PC", exact: true });
  if (await urlDialog.isVisible().catch(() => false)) {
    await urlDialog.getByRole("button", { name: "Close", exact: true }).click();
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.locator('[data-settings-section="connection"] > summary').click();
  const disconnectButton = page.getByRole("button", { name: "Disconnect this PC", exact: true });
  await disconnectButton.scrollIntoViewIfNeeded();
  await disconnectButton.click();
  await page.getByRole("button", { name: "Close menu", exact: true }).click();

  const reconnectPanel = page.locator(".pairing-required");
  await reconnectPanel.getByRole("heading", { name: "PC disconnected", exact: true }).waitFor({ state: "visible" });
  const blockingState = await page.evaluate(() => {
    const panel = document.querySelector(".pairing-required");
    const backdrop = document.querySelector(".pairing-backdrop");
    const menuButton = document.querySelector('[aria-label="Open menu"]');
    if (!(panel instanceof HTMLElement) || !(backdrop instanceof HTMLElement) || !(menuButton instanceof HTMLElement)) {
      return { error: "Blocking reconnect panel was incomplete." };
    }

    const menuBounds = menuButton.getBoundingClientRect();
    const hitTarget = document.elementFromPoint(menuBounds.left + menuBounds.width / 2, menuBounds.top + menuBounds.height / 2);
    const backdropBounds = backdrop.getBoundingClientRect();
    return {
      appChromeBlocked: hitTarget === backdrop,
      backdropCoversViewport: backdropBounds.left <= 0 && backdropBounds.top <= 0 && backdropBounds.right >= window.innerWidth && backdropBounds.bottom >= window.innerHeight,
      modal: panel.getAttribute("aria-modal") === "true" && panel.getAttribute("role") === "dialog"
    };
  });
  if ("error" in blockingState || !blockingState.appChromeBlocked || !blockingState.backdropCoversViewport || !blockingState.modal) {
    throw new Error(`Disconnected saved-PC reconnect blocking state failed: ${JSON.stringify(blockingState)}`);
  }
  const reconnectButton = reconnectPanel.getByRole("button", { name: "Try reconnect", exact: true });
  const qrButton = reconnectPanel.getByRole("button", { name: "Take photo of QR code", exact: true });

  const viewports = [
    { name: "regular portrait", width: 393, height: 852 },
    { name: "short landscape", width: 640, height: 360 }
  ];
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await reconnectButton.scrollIntoViewIfNeeded();
    await qrButton.scrollIntoViewIfNeeded();
    const result = await reconnectPanel.evaluate((panel) => {
      const actions = Array.from(panel.querySelectorAll(".pairing-actions button"));
      const bounds = panel.getBoundingClientRect();
      return {
        actionCount: actions.length,
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minActionHeight: Math.min(...actions.map((button) => button.getBoundingClientRect().height)),
        outsideViewport: bounds.left < -1 || bounds.right > window.innerWidth + 1,
        scrollableWhenNeeded: panel.scrollHeight <= panel.clientHeight + 1 || getComputedStyle(panel).overflowY === "auto"
      };
    });

    if (result.actionCount !== 2 || result.horizontalOverflow || result.minActionHeight < 48 || result.outsideViewport || !result.scrollableWhenNeeded) {
      throw new Error(`Disconnected saved-PC reconnect layout failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }

  await page.setViewportSize({ width: 393, height: 852 });
  await reconnectButton.scrollIntoViewIfNeeded();
  await reconnectButton.click();
  await waitForConnected(page);
}

async function launchPersistentContext(chromium) {
  const options = {
    channel: "chrome",
    headless: smokeTest,
    devtools: !smokeTest,
    viewport: smokeTest ? { width: debugDevice.screen.vertical.width, height: debugDevice.screen.vertical.height } : null,
    args: smokeTest ? ["--test-type"] : ["--start-maximized", "--auto-open-devtools-for-tabs", "--test-type"]
  };

  try {
    return await chromium.launchPersistentContext(browserProfileDir, options);
  } catch {
    console.log("Chrome channel was not available; installing Playwright Chromium.");
    await run("npx", ["playwright", "install", "chromium"], { cwd: tempNodeDir });
    const { channel, ...fallbackOptions } = options;
    return chromium.launchPersistentContext(browserProfileDir, fallbackOptions);
  }
}

async function seedBrowserProfile(device) {
  const preferences = {
    browser: {
      window_placement: {
        maximized: true
      }
    },
    devtools: {
      preferences: {
        currentDockState: JSON.stringify("right"),
        "custom-emulated-device-list": JSON.stringify(devUiDevices),
        customEmulatedDeviceList: JSON.stringify(devUiDevices),
        "emulation.device-mode-value": JSON.stringify({
          device: device.title,
          orientation: "vertical",
          mode: ""
        }),
        "emulation.device-scale": "0.86",
        "emulation.show-device-mode": "true"
      }
    }
  };

  await writeJson(path.join(browserProfileDir, "Preferences"), preferences);
  await writeJson(path.join(browserProfileDir, "Default", "Preferences"), preferences);
}

async function applyDeviceEmulation(page, device) {
  const vertical = device.screen.vertical;
  const client = await page.context().newCDPSession(page);
  await client.send("Emulation.setDeviceMetricsOverride", {
    width: vertical.width,
    height: vertical.height,
    deviceScaleFactor: device.screen["device-pixel-ratio"],
    mobile: device.capabilities.includes("mobile"),
    screenWidth: vertical.width,
    screenHeight: vertical.height
  });
  await client.send("Emulation.setTouchEmulationEnabled", {
    enabled: device.capabilities.includes("touch"),
    maxTouchPoints: device.capabilities.includes("touch") ? 1 : 0
  });
}

async function clickPairIfPresent(page) {
  const pair = page.getByRole("button", { name: "Pair" });
  if (await pair.isVisible({ timeout: 10000 }).catch(() => false)) {
    await pair.evaluate((button) => button.click());
  }
}

async function waitForConnected(page) {
  await page.locator(".status.paired").waitFor({ state: "visible", timeout: 15000 });
}

async function waitForTextFile(filePath, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (existsSync(filePath)) {
      const value = (await fs.readFile(filePath, "utf8")).trim();
      if (value) {
        return value;
      }
    }
    await delay(200);
  }

  throw new Error(`Timed out waiting for ${filePath}`);
}

async function waitForHttp(url, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
    }
    await delay(250);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

function spawnCommand(command, args, env, options = {}) {
  const commandLine = [command, ...args].join(" ");
  const child = process.platform === "win32"
    ? spawn("cmd.exe", ["/d", "/s", "/c", commandLine], { stdio: "inherit", env, windowsHide: false, ...options })
    : spawn(command, args, { stdio: "inherit", env, ...options });

  child.commandLine = commandLine;
  child.on("error", (error) => {
    console.error(`Failed to start ${child.commandLine}:`, error);
    shutdown("SIGTERM", 1);
  });

  child.on("exit", (code, signal) => {
    if (shuttingDown) {
      return;
    }

    if (signal) {
      shutdown("SIGTERM", 1);
      return;
    }

    shutdown("SIGTERM", code ?? 0);
  });

  return child;
}

async function run(command, args, options = {}) {
  const executable = resolveCommand(command);
  console.log(`> ${command} ${args.join(" ")}`);
  await new Promise((resolve, reject) => {
    const [spawnFile, spawnArgs] = process.platform === "win32"
      ? ["cmd.exe", ["/d", "/s", "/c", [executable, ...args].join(" ")]]
      : [executable, args];
    const child = spawn(spawnFile, spawnArgs, {
      cwd: options.cwd ?? repoRoot,
      stdio: "inherit",
      windowsHide: true
    });
    child.once("exit", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${command} exited with code ${code}`));
      }
    });
    child.once("error", reject);
  });
}

function shutdown(signal, exitCode = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  Promise.resolve()
    .then(async () => {
      if (browserContext) {
        await browserContext.close().catch(() => {});
      }

      try {
        stopExistingHost();
      } catch (error) {
        console.error(error);
        exitCode = 1;
      }

      for (const child of children) {
        stopChild(child, signal);
      }
    })
    .finally(() => setTimeout(() => process.exit(exitCode), 500));
}

async function writeJson(filePath, value) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, JSON.stringify(value));
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
