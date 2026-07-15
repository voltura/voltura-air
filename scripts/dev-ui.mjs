import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { devUiDevices, getDevUiDevice } from "./dev-ui-devices.mjs";
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
const browserProfileDir = path.join(tempDir, "chrome-profile");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const clientPort = readPreferredClientPort();
const smokeTest = process.argv.includes("--smoke-test");
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

  children.push(spawnCommand("dotnet", [
    "run",
    "--project",
    "apps/windows-host/VolturaAir.Host.csproj",
    "--",
    "--client-url",
    clientUrl,
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile,
    "--isolated-test-mode"
  ], {
    ...childEnv,
    APPDATA: tempAppDataDir
  }, { cwd: repoRoot }));

  const pairingUrl = await waitForTextFile(pairingUrlFile, 30000);
  const requireFromTemp = createRequire(path.join(tempNodeDir, "package.json"));
  const { chromium } = requireFromTemp("playwright");
  const page = await launchBrowser(chromium, pairingUrl);

  if (smokeTest) {
    await verifyResponsivePowerLayout(page);
    await verifyResponsiveTextTransferLayout(page);
    await verifyResponsiveUrlOpenLayout(page);
    console.log("Voltura Air UI smoke test connected and passed responsive Power sheet, text transfer, and URL opening checks.");
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

  if (existsSync(path.join(tempNodeDir, "node_modules", "playwright"))) {
    return;
  }

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", "playwright"], { cwd: tempNodeDir });
}

async function launchBrowser(chromium, pairingUrl) {
  const url = new URL(pairingUrl);
  url.searchParams.set("debug", "1");

  browserContext = await launchPersistentContext(chromium);
  const page = browserContext.pages()[0] ?? await browserContext.newPage();
  await applyDeviceEmulation(page, debugDevice);
  await page.goto(url.href, { waitUntil: "networkidle" });
  await clickPairIfPresent(page);
  if (smokeTest) {
    await waitForConnected(page);
  }
  return page;
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
    const buttons = Array.from(dialog.querySelectorAll("button"));
    if (!(standardInput instanceof HTMLInputElement) || !(input instanceof HTMLInputElement) || buttons.length !== 2) {
      return { error: "Themed snippet dialog controls were not visible." };
    }
    return {
      buttonsUseElements: buttons.every((button) => button instanceof HTMLButtonElement),
      fontMatchesApp: getComputedStyle(dialog).fontFamily === getComputedStyle(document.body).fontFamily,
      inputMatchesTheme: getComputedStyle(input).backgroundColor === getComputedStyle(standardInput).backgroundColor,
      minButtonHeight: Math.min(...buttons.map((button) => button.getBoundingClientRect().height)),
      opaqueBackground: getComputedStyle(dialog).backgroundColor !== "rgba(0, 0, 0, 0)"
    };
  });
  if ("error" in dialogMetrics || !dialogMetrics.buttonsUseElements || !dialogMetrics.fontMatchesApp ||
      !dialogMetrics.inputMatchesTheme || dialogMetrics.minButtonHeight < 48 || !dialogMetrics.opaqueBackground) {
    throw new Error(`Themed snippet dialog check failed: ${JSON.stringify(dialogMetrics)}`);
  }
  await renameDialog.getByRole("button", { name: "Cancel", exact: true }).click();
}

async function verifyResponsiveUrlOpenLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Remote mode", exact: true }).click();
  await page.getByRole("button", { name: "Fn", exact: true }).click();
  const input = page.getByRole("textbox", { name: "Open URL on PC", exact: true });

  if (await input.count() === 0) {
    const permissionMessage = page.getByText("Allow URL opening in the PC permissions first.", { exact: true });
    if (await permissionMessage.count() !== 1 || await page.getByRole("button", { name: "Open", exact: true }).count() !== 0) {
      throw new Error("URL controls were not hidden with the PC permission disabled.");
    }
    return;
  }

  await input.fill("javascript:alert(1)");
  if (!await page.getByRole("button", { name: "Open", exact: true }).isDisabled() ||
      await page.getByText("Use an HTTP or HTTPS web address.", { exact: true }).count() !== 1) {
    throw new Error("Invalid URL drafts did not disable Open with clear feedback.");
  }

  await page.getByRole("button", { name: "About Open URL on PC", exact: true }).click();
  const infoDialog = page.getByRole("dialog", { name: "Open URL on PC", exact: true });
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
    await page.locator(".remote-url-open").scrollIntoViewIfNeeded();
    await input.focus();
    const result = await page.evaluate(() => {
      const form = document.querySelector(".remote-url-open");
      const field = document.querySelector("#remote-url-draft");
      const button = document.querySelector(".remote-url-open-row button");
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
