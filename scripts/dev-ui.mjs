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
  stopWindowsNodeListenersOnDevPorts,
} from "./dev-shared.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tempDir = path.join(tmpdir(), "voltura-air-dev-ui");
const tempNodeDir = path.join(tempDir, "node");
const tempAppDataDir = path.join(tempDir, "appdata");
const tempArtifactsDir = path.join(tempDir, "artifacts");
const browserProfileDir = path.join(tempDir, "chrome-profile");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const clientPort = readPreferredClientPort();
const accentSmokeTest = process.argv.includes("--accent-smoke-test");
const smokeTest = accentSmokeTest || process.argv.includes("--smoke-test");
const hostStartupTimeoutMs = 120000;
const clientUrl = process.env.VOLTURA_AIR_CLIENT_URL ?? `http://127.0.0.1:${clientPort}`;
const clientHost = new URL(clientUrl).hostname;
const debugDevice = getDevUiDevice();
const childEnv = {
  ...process.env,
  VOLTURA_AIR_CLIENT_PORT: String(clientPort),
  VOLTURA_AIR_CLIENT_URL: clientUrl,
  VOLTURA_AIR_USE_VITE_CLIENT: "1",
};
const children = [];
const deviceEmulationClients = new WeakMap();
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
    throw new Error(
      "Voltura Air UI debug sessions must run on Windows because they launch the WPF host.",
    );
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

  children.push(
    spawnCommand(
      "node",
      [
        "../../node_modules/vite/bin/vite.js",
        "--host",
        clientHost,
        "--strictPort",
        "--port",
        String(clientPort),
      ],
      childEnv,
      { cwd: path.join(repoRoot, "apps", "mobile-web") },
    ),
  );

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
  ];
  if (smokeTest) {
    hostArguments.push("--minimized");
  }

  children.push(
    spawnCommand(
      "dotnet",
      hostArguments,
      {
        ...childEnv,
        APPDATA: tempAppDataDir,
      },
      { cwd: repoRoot },
    ),
  );

  const pairingUrl = await waitForTextFile(pairingUrlFile, hostStartupTimeoutMs);
  const requireFromTemp = createRequire(path.join(tempNodeDir, "package.json"));
  const { chromium } = requireFromTemp("playwright");
  const qrCode = requireFromTemp("qrcode");
  const page = await launchBrowser(chromium, qrCode, pairingUrl);

  if (accentSmokeTest) {
    await verifyAccentColorPicker(page);
    console.log(
      "Voltura Air accent UI smoke test passed portrait and landscape layout, extreme-color contrast, Apply, Cancel, and PC-default inheritance.",
    );
    shutdown("SIGTERM", 0);
    return;
  }

  if (smokeTest) {
    await verifySettingsDrawerLifecycle(page);
    await verifyRemoteModeSelectorLayout(page);
    await verifyKodiRemoteLayout(page);
    await verifyTrackpadButtonLayout(page);
    await verifyKeyboardLayout(page);
    await verifyLandscapeSafeAreaLayouts(page);
    await verifyResponsivePowerLayout(page);
    await verifyResponsiveTextTransferLayout(page);
    await verifyResponsivePresentationLayout(page);
    await verifyResponsiveUrlOpenLayout(page);
    await verifyDisconnectedSavedPcReconnect(page);
    console.log(
      "Voltura Air UI smoke test connected and passed settings drawer lifecycle, Kodi Remote, trackpad, keyboard and landscape safe-area layout, responsive Power sheet, text transfer, Presentation, URL opening, and saved-PC reconnect checks.",
    );
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

  if (
    existsSync(path.join(tempNodeDir, "node_modules", "playwright")) &&
    existsSync(path.join(tempNodeDir, "node_modules", "qrcode"))
  ) {
    return;
  }

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", "playwright", "qrcode"], {
    cwd: tempNodeDir,
  });
}

async function launchBrowser(chromium, qrCode, pairingUrl) {
  const url = new URL(pairingUrl);

  browserContext = await launchPersistentContext(chromium);
  const page = browserContext.pages()[0] ?? (await browserContext.newPage());
  await applyDeviceEmulation(page, debugDevice);
  await page.goto(url.href, { waitUntil: "networkidle" });
  if (smokeTest) {
    await page
      .getByRole("button", { name: "Pair", exact: true })
      .waitFor({ state: "visible", timeout: 10000 });
    const qrImageFile = path.join(tempDir, "pairing-qr.png");
    await qrCode.toFile(qrImageFile, pairingUrl, {
      errorCorrectionLevel: "H",
      margin: 4,
      width: 1024,
    });
    const ordinaryUrl = new URL(clientUrl);
    ordinaryUrl.searchParams.set("debug", "1");
    await page.goto(ordinaryUrl.href, { waitUntil: "networkidle" });
    await page.locator('input[type="file"][accept="image/*"]').first().setInputFiles(qrImageFile);
    await page
      .getByRole("button", { name: "Pair", exact: true })
      .waitFor({ state: "visible", timeout: 10000 });
    await clickPairIfPresent(page);
    await waitForConnected(page);
  } else {
    await clickPairIfPresent(page);
  }
  return page;
}

async function verifyRemoteModeSelectorLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  const remoteButton = page.locator('button[aria-label="Remote"]:visible').last();
  await remoteButton.click();
  await page.locator('button[aria-label="Change mode"]:visible').click();

  const viewports = [
    { name: "compact phone portrait", width: 360, height: 780, standardButtons: true },
    { name: "phone portrait", width: 393, height: 852, standardButtons: true },
    { name: "short phone landscape", width: 568, height: 320, standardButtons: false },
    { name: "phone landscape", width: 852, height: 393, standardButtons: false },
    { name: "tablet portrait", width: 768, height: 1024, standardButtons: true },
    { name: "tablet landscape", width: 1024, height: 768, standardButtons: true },
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(() => {
      const menu = document.querySelector(".mode-selector-popover");
      const compactSelector = document.querySelector(".top-bar .compact-mode-button");
      const remote = menu?.querySelector('button[aria-label="Remote"]');
      const remoteModes = menu?.querySelector(".mode-selector-remote-modes");
      const standardNavigations = Array.from(
        document.querySelectorAll(".top-mode-tabs, .bottom-mode-tabs"),
      );
      const modeButtons = remoteModes ? Array.from(remoteModes.querySelectorAll("button")) : [];
      if (
        !(menu instanceof HTMLElement) ||
        !(compactSelector instanceof HTMLButtonElement) ||
        !(remote instanceof HTMLButtonElement) ||
        !(remoteModes instanceof HTMLElement) ||
        modeButtons.length !== 3
      ) {
        return { error: "Remote quick-mode choices were not visible." };
      }

      const menuBounds = menu.getBoundingClientRect();
      const remoteBounds = remote.getBoundingClientRect();
      const remoteModesBounds = remoteModes.getBoundingClientRect();
      const activeButtons = modeButtons.filter((button) => button.classList.contains("active"));
      const minimumButtonHeight = Math.min(
        ...modeButtons.map((button) => button.getBoundingClientRect().height),
      );
      const isVisible = (element) => {
        const bounds = element.getBoundingClientRect();
        return (
          getComputedStyle(element).display !== "none" && bounds.width > 0 && bounds.height > 0
        );
      };
      return {
        activeCount: activeButtons.length,
        compactSelectorVisible: isVisible(compactSelector),
        containedHorizontally:
          menuBounds.left >= 0 &&
          menuBounds.right <= window.innerWidth + 1 &&
          remoteModesBounds.left >= menuBounds.left &&
          remoteModesBounds.right <= menuBounds.right + 1 &&
          menu.scrollWidth <= menu.clientWidth + 1,
        menuInViewport:
          menuBounds.top >= 0 &&
          menuBounds.bottom <= window.innerHeight + 1 &&
          menu.clientHeight <= menu.scrollHeight,
        minimumButtonHeight,
        remoteChoicesTogether:
          remoteBounds.top >= menuBounds.top && remoteModesBounds.bottom <= menuBounds.bottom + 1,
        standardNavigationVisible: standardNavigations.some(isVisible),
      };
    });

    if (
      "error" in result ||
      result.activeCount !== 1 ||
      !result.compactSelectorVisible ||
      !result.containedHorizontally ||
      !result.menuInViewport ||
      result.minimumButtonHeight < 44 ||
      !result.remoteChoicesTogether ||
      result.standardNavigationVisible !== viewport.standardButtons
    ) {
      throw new Error(
        `Remote quick-mode layout failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
    }
  }

  await page.getByRole("menuitemradio", { name: "Trackpad", exact: true }).click();
}

async function verifyKodiRemoteLayout(page) {
  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "compact phone portrait", width: 360, height: 780 },
    { name: "short phone portrait", width: 375, height: 667 },
    { name: "short phone landscape", width: 568, height: 320 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet landscape", width: 1024, height: 768 },
  ];
  const navigationVariants = [
    { name: "D-pad", selector: ".remote-dpad", enabled: false },
    { name: "ring", selector: ".remote-navigation-ring", enabled: true },
  ];
  const actionNames = [
    "Up one level",
    "Menu or player controls",
    "Info",
    "Toggle subtitles",
    "Audio track",
    "Toggle fullscreen or windowed",
  ];

  for (const navigation of navigationVariants) {
    for (const viewport of viewports) {
      await setEmulatedViewport(page, viewport);
      await page.evaluate(
        ({ navigationRing }) => {
          const clientId = localStorage.getItem("voltura-air.clientId");
          const pcId = localStorage.getItem("voltura-air.activePcId");
          const update = (key) => {
            let current = {};
            try {
              current = JSON.parse(localStorage.getItem(key) ?? "{}");
            } catch {
              current = {};
            }
            localStorage.setItem(
              key,
              JSON.stringify({ ...current, navigationRing, mode: "kodi", startKodi: false }),
            );
          };
          if (clientId) {
            update(`voltura-air.remoteSettings.${clientId}`);
          }
          if (clientId && pcId) {
            update(`voltura-air.remoteSettings.${clientId}.${pcId}`);
          }
        },
        { navigationRing: navigation.enabled },
      );
      await page.reload({ waitUntil: "networkidle" });
      await setEmulatedViewport(page, viewport);
      await waitForConnected(page);
      if (!(await page.locator(".remote-mode").isVisible())) {
        const remoteButton = page.locator('button[aria-label="Remote"]:visible').last();
        if (await remoteButton.isVisible()) {
          await remoteButton.click();
        } else {
          await page.locator('button[aria-label="Change mode"]:visible').click();
          await page.getByRole("menuitemradio", { name: "Remote", exact: true }).click();
        }
      }
      await page.locator(navigation.selector).waitFor({ state: "visible", timeout: 5000 });

      const result = await page.evaluate(
        ({ actionNames, navigationSelector }) => {
          const mode = document.querySelector(".remote-mode");
          const navigation = document.querySelector(".remote-navigation-section");
          const navigationControl = document.querySelector(navigationSelector);
          const functions = document.querySelector(".remote-floating-fn");
          const power = document.querySelector(".remote-power-button");
          const title = navigation?.querySelector(".remote-section-title");
          const actions = actionNames.map((name) =>
            navigation?.querySelector(`button[aria-label="${name}"]`),
          );
          if (
            !(mode instanceof HTMLElement) ||
            !(navigation instanceof HTMLElement) ||
            !(navigationControl instanceof HTMLElement) ||
            !(functions instanceof HTMLButtonElement) ||
            actions.some((action) => !(action instanceof HTMLButtonElement))
          ) {
            return {
              error: "Kodi Remote controls were not visible.",
              actionCount: actions.filter((action) => action instanceof HTMLButtonElement).length,
              functions: functions instanceof HTMLButtonElement,
              mode: mode instanceof HTMLElement,
              navigation: navigation instanceof HTMLElement,
              navigationControl: navigationControl instanceof HTMLElement,
            };
          }

          const intersects = (first, second) =>
            first.left < second.right - 1 &&
            first.right > second.left + 1 &&
            first.top < second.bottom - 1 &&
            first.bottom > second.top + 1;
          const actionBounds = actions.map((action) => action.getBoundingClientRect());
          const navigationBounds = navigation.getBoundingClientRect();
          const navigationControlBounds = navigationControl.getBoundingClientRect();
          const functionsBounds = functions.getBoundingClientRect();
          const powerBounds = power instanceof HTMLElement ? power.getBoundingClientRect() : null;
          const fullscreen = actions[actionNames.indexOf("Toggle fullscreen or windowed")];
          const fullscreenBounds = fullscreen?.getBoundingClientRect() ?? null;
          const functionsLabel = functions.querySelector(".remote-corner-action-label");
          const powerLabel = power?.querySelector("span") ?? null;
          const navigationObstacleBounds = navigationControl.matches(".remote-dpad")
            ? Array.from(navigationControl.querySelectorAll("button")).map((button) =>
                button.getBoundingClientRect(),
              )
            : [navigationControlBounds];
          const cornerObstacleBounds = [functions, power]
            .filter(
              (element) =>
                element instanceof HTMLElement && getComputedStyle(element).display !== "none",
            )
            .map((element) => element.getBoundingClientRect())
            .filter((bounds) => bounds.width > 0 && bounds.height > 0);
          const obstacleBounds = [
            ...cornerObstacleBounds,
            ...(title instanceof HTMLElement && getComputedStyle(title).display !== "none"
              ? [title.getBoundingClientRect()]
              : []),
          ];
          const pairOverlaps = actionBounds.some((bounds, index) =>
            actionBounds.slice(index + 1).some((other) => intersects(bounds, other)),
          );
          const controlsOverlap = actionBounds.some(
            (bounds) =>
              navigationObstacleBounds.some((other) => intersects(bounds, other)) ||
              obstacleBounds.some((other) => intersects(bounds, other)),
          );
          const overlapDetails = actionBounds.flatMap((bounds, index) => {
            const overlaps = [];
            if (navigationObstacleBounds.some((other) => intersects(bounds, other))) {
              overlaps.push(`${actionNames[index]}:navigation`);
            }
            obstacleBounds.forEach((other, obstacleIndex) => {
              if (intersects(bounds, other)) {
                overlaps.push(`${actionNames[index]}:obstacle-${obstacleIndex}`);
              }
            });
            return overlaps;
          });
          const hitTargetsWork = actions.every((action, index) => {
            const bounds = actionBounds[index];
            const hit = document.elementFromPoint(
              bounds.left + bounds.width / 2,
              bounds.top + bounds.height / 2,
            );
            return hit === action || action.contains(hit);
          });
          const modeBounds = mode.getBoundingClientRect();
          const sectionBounds = Array.from(mode.querySelectorAll(":scope > .remote-section"))
            .filter((section) => getComputedStyle(section).display !== "none")
            .map((section) => section.getBoundingClientRect());
          return {
            actionCount: actions.length,
            actionsContained: actionBounds.every(
              (bounds) =>
                bounds.left >= navigationBounds.left - 1 &&
                bounds.right <= navigationBounds.right + 1 &&
                bounds.top >= navigationBounds.top - 1 &&
                bounds.bottom <= navigationBounds.bottom + 1,
            ),
            controlsOverlap,
            hitTargetsWork,
            horizontalOverflow:
              document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
            geometry: {
              actions: actionBounds.map((bounds, index) => ({
                name: actionNames[index],
                left: bounds.left,
                right: bounds.right,
                top: bounds.top,
                bottom: bounds.bottom,
              })),
              navigationObstacles: navigationObstacleBounds.map((bounds) => ({
                left: bounds.left,
                right: bounds.right,
                top: bounds.top,
                bottom: bounds.bottom,
              })),
              navigation: {
                top: navigationBounds.top,
                bottom: navigationBounds.bottom,
              },
              obstacles: obstacleBounds.map((bounds) => ({
                top: bounds.top,
                bottom: bounds.bottom,
              })),
            },
            minActionHeight: Math.min(...actionBounds.map((bounds) => bounds.height)),
            minActionWidth: Math.min(...actionBounds.map((bounds) => bounds.width)),
            navigationCentered:
              Math.abs(
                (navigationControlBounds.left + navigationControlBounds.right) / 2 -
                  (navigationBounds.left + navigationBounds.right) / 2,
              ) <= 8 &&
              Math.abs(
                (navigationControlBounds.top + navigationControlBounds.bottom) / 2 -
                  (navigationBounds.top + navigationBounds.bottom) / 2,
              ) <= 8,
            navigationOverlapsCornerControls: navigationObstacleBounds.some((bounds) =>
              cornerObstacleBounds.some((other) => intersects(bounds, other)),
            ),
            navigationControlWidth: navigationControlBounds.width,
            shortLandscapePlacement:
              window.matchMedia("(aspect-ratio >= 1/1) and (height <= 520px)").matches &&
              !window.matchMedia("(height <= 375px)").matches
                ? {
                    fullscreenBesidePower:
                      powerBounds !== null &&
                      fullscreenBounds !== null &&
                      fullscreenBounds.left >= powerBounds.right + 1 &&
                      fullscreenBounds.right <= functionsBounds.left - 1,
                    fullscreenOnBottom:
                      fullscreenBounds !== null &&
                      Math.abs(fullscreenBounds.bottom - navigationBounds.bottom) <= 1,
                    functionsIconOnly:
                      functionsLabel instanceof HTMLElement &&
                      getComputedStyle(functionsLabel).display === "none",
                    powerIconOnly:
                      powerLabel instanceof HTMLElement &&
                      getComputedStyle(powerLabel).display === "none",
                    functionsOnBottomRight:
                      Math.abs(functionsBounds.right - navigationBounds.right) <= 1 &&
                      Math.abs(functionsBounds.bottom - navigationBounds.bottom) <= 1,
                  }
                : null,
            modeClipped:
              mode.scrollHeight > mode.clientHeight + 1 ||
              mode.scrollWidth > mode.clientWidth + 1 ||
              sectionBounds.some(
                (bounds) =>
                  bounds.left < modeBounds.left - 1 ||
                  bounds.right > modeBounds.right + 1 ||
                  bounds.top < modeBounds.top - 1 ||
                  bounds.bottom > modeBounds.bottom + 1,
              ),
            overlapDetails,
            pairOverlaps,
          };
        },
        { actionNames, navigationSelector: navigation.selector },
      );

      if (
        "error" in result ||
        result.actionCount !== 6 ||
        !result.actionsContained ||
        result.controlsOverlap ||
        !result.hitTargetsWork ||
        result.horizontalOverflow ||
        result.minActionHeight < 44 ||
        result.minActionWidth < 44 ||
        (result.shortLandscapePlacement !== null &&
          (!result.shortLandscapePlacement.fullscreenBesidePower ||
            !result.shortLandscapePlacement.fullscreenOnBottom ||
            !result.shortLandscapePlacement.functionsIconOnly ||
            !result.shortLandscapePlacement.powerIconOnly ||
            !result.shortLandscapePlacement.functionsOnBottomRight)) ||
        (navigation.enabled &&
          viewport.width === 568 &&
          viewport.height === 320 &&
          result.navigationControlWidth < 140) ||
        (navigation.enabled &&
          viewport.width === 852 &&
          viewport.height === 393 &&
          result.navigationControlWidth < 170) ||
        (navigation.enabled &&
          viewport.width > viewport.height &&
          viewport.height <= 520 &&
          !result.navigationCentered) ||
        result.navigationOverlapsCornerControls ||
        result.modeClipped ||
        result.pairOverlaps
      ) {
        throw new Error(
          `Kodi Remote ${navigation.name} layout failed for ${viewport.name}: ${JSON.stringify(result)}`,
        );
      }

      if (navigation.enabled && viewport.width === 393 && viewport.height === 852) {
        await page.screenshot({ path: path.join(tempArtifactsDir, "kodi-remote-393x852.png") });
      }
      if (navigation.enabled && viewport.width === 852 && viewport.height === 393) {
        await page.screenshot({ path: path.join(tempArtifactsDir, "kodi-remote-852x393.png") });
      }

      await page.getByRole("button", { name: "Fn", exact: true }).click();
      await page.getByRole("button", { name: "Aspect ratio", exact: true }).waitFor({
        state: "visible",
        timeout: 5000,
      });
      const utilityResult = await page.locator(".remote-utility-section").evaluate(
        (utility, { navigationSelector }) => {
          utility.scrollTop = 0;
          const kodiGrid = utility.querySelector(".remote-kodi-utility-grid");
          const titles = Array.from(utility.querySelectorAll(".remote-section-title > span"));
          const buttons = kodiGrid ? Array.from(kodiGrid.querySelectorAll("button")) : [];
          const navigation = document.querySelector(".remote-navigation-section");
          const navigationControl = document.querySelector(navigationSelector);
          if (!(kodiGrid instanceof HTMLElement) || buttons.length !== 7) {
            return {
              error: "Kodi Functions controls were not visible.",
              buttonCount: buttons.length,
            };
          }
          const utilityBounds = utility.getBoundingClientRect();
          const gridBounds = kodiGrid.getBoundingClientRect();
          const buttonBounds = buttons.map((button) => button.getBoundingClientRect());
          const labelElements = buttons.map((button) => button.querySelector("span"));
          const navigationVisible =
            navigation instanceof HTMLElement && getComputedStyle(navigation).display !== "none";
          let navigationContained = null;
          let navigationCentered = null;
          let navigationControlWidth = null;
          let navigationOverlapsCornerControls = null;
          if (
            navigationVisible &&
            navigation instanceof HTMLElement &&
            navigationControl instanceof HTMLElement
          ) {
            const navigationBounds = navigation.getBoundingClientRect();
            const controlBounds = navigationControl.getBoundingClientRect();
            const controlObstacleBounds = navigationControl.matches(".remote-dpad")
              ? Array.from(navigationControl.querySelectorAll("button")).map((button) =>
                  button.getBoundingClientRect(),
                )
              : [controlBounds];
            const cornerControls = [
              navigation.querySelector(".remote-power-button"),
              navigation.querySelector(".remote-floating-fn"),
            ].filter((element) => element instanceof HTMLElement);
            const intersects = (first, second) =>
              first.left < second.right - 1 &&
              first.right > second.left + 1 &&
              first.top < second.bottom - 1 &&
              first.bottom > second.top + 1;
            navigationContained =
              controlBounds.left >= navigationBounds.left - 1 &&
              controlBounds.right <= navigationBounds.right + 1 &&
              controlBounds.top >= navigationBounds.top - 1 &&
              controlBounds.bottom <= Math.min(navigationBounds.bottom, window.innerHeight) + 1;
            navigationCentered =
              Math.abs(
                (controlBounds.left + controlBounds.right) / 2 -
                  (navigationBounds.left + navigationBounds.right) / 2,
              ) <= 8 &&
              Math.abs(
                (controlBounds.top + controlBounds.bottom) / 2 -
                  (navigationBounds.top + navigationBounds.bottom) / 2,
              ) <= 8;
            navigationControlWidth = controlBounds.width;
            navigationOverlapsCornerControls = controlObstacleBounds.some((bounds) =>
              cornerControls.some((control) => intersects(bounds, control.getBoundingClientRect())),
            );
          }
          return {
            applicationSectionPresent: utility.querySelector(".remote-app-launch-grid") !== null,
            buttonCount: buttons.length,
            firstSection: titles[0]?.textContent?.trim() ?? "",
            horizontalOverflow: utility.scrollWidth > utility.clientWidth + 1,
            labelsFit: labelElements.every(
              (label) => label instanceof HTMLElement && label.scrollWidth <= label.clientWidth + 1,
            ),
            minButtonHeight: Math.min(...buttonBounds.map((bounds) => bounds.height)),
            navigationContained,
            navigationCentered,
            navigationControlWidth,
            navigationOverlapsCornerControls,
            visibleLabels: labelElements.map((label) => label?.textContent?.trim() ?? ""),
            viewport: {
              height: window.innerHeight,
              portrait: window.matchMedia("(orientation: portrait)").matches,
              width: window.innerWidth,
            },
            visibleBeforeScroll:
              utility.scrollTop === 0 &&
              gridBounds.top >= utilityBounds.top - 1 &&
              buttonBounds.every((bounds) => bounds.bottom <= utilityBounds.bottom + 1),
          };
        },
        { navigationSelector: navigation.selector },
      );

      if (
        "error" in utilityResult ||
        utilityResult.buttonCount !== 7 ||
        utilityResult.applicationSectionPresent ||
        utilityResult.firstSection !== "Kodi" ||
        utilityResult.horizontalOverflow ||
        !utilityResult.labelsFit ||
        utilityResult.minButtonHeight < 44 ||
        utilityResult.visibleLabels.join("|") !==
          "Rewind|Forward|Subtitle|Previous|Next|Details|Aspect" ||
        (viewport.width <= 560 &&
          viewport.height >= 760 &&
          utilityResult.navigationContained !== true) ||
        utilityResult.navigationOverlapsCornerControls === true ||
        (viewport.width > viewport.height &&
          viewport.height <= 520 &&
          utilityResult.navigationCentered !== true) ||
        (navigation.enabled &&
          viewport.width === 568 &&
          viewport.height === 320 &&
          (utilityResult.navigationControlWidth ?? 0) < 150) ||
        !utilityResult.visibleBeforeScroll
      ) {
        throw new Error(
          `Kodi Functions ${navigation.name} layout failed for ${viewport.name}: ${JSON.stringify(utilityResult)}`,
        );
      }

      if (navigation.enabled && viewport.width === 393 && viewport.height === 852) {
        await page.screenshot({ path: path.join(tempArtifactsDir, "kodi-functions-393x852.png") });
      }
      if (navigation.enabled && viewport.width === 568 && viewport.height === 320) {
        await page.screenshot({ path: path.join(tempArtifactsDir, "kodi-functions-568x320.png") });
      }
      if (navigation.enabled && viewport.width === 852 && viewport.height === 393) {
        await page.screenshot({ path: path.join(tempArtifactsDir, "kodi-functions-852x393.png") });
      }
    }
  }

  await setEmulatedViewport(page, { width: 393, height: 852 });
  await page.locator('button[aria-label="Change mode"]:visible').click();
  await page.getByRole("menuitemradio", { name: "Trackpad", exact: true }).click();
}

async function verifyTrackpadButtonLayout(page) {
  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "phone landscape", width: 852, height: 393 },
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
        fillsRowWidth:
          Math.abs(buttonBounds[0].left - rowBounds.left) <= 1 &&
          Math.abs(buttonBounds[1].right - rowBounds.right) <= 1,
        rowDisplay: getComputedStyle(row).display,
      };
    });

    if (
      "error" in result ||
      !result.equalButtonWidths ||
      !result.fillsModeWidth ||
      !result.fillsRowWidth ||
      result.rowDisplay !== "grid"
    ) {
      throw new Error(
        `Trackpad click button layout failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
    }
  }
}

async function verifyKeyboardLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Keyboard", exact: true }).click();
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
    if (
      !(mode instanceof HTMLElement) ||
      !(primaryKeys instanceof HTMLElement) ||
      !(input instanceof HTMLTextAreaElement) ||
      !(liveTyping instanceof HTMLElement) ||
      !(selector instanceof HTMLElement) ||
      selectorButtons.length !== 2 ||
      primaryButtons.length < 7
    ) {
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
      selectorGap: selectorButtonBounds[1].left - selectorButtonBounds[0].right,
    };
  });

  if (
    "error" in portrait ||
    !portrait.inputFocused ||
    (portrait.inputOutlineWidth !== "0px" && portrait.inputOutlineStyle !== "none") ||
    !portrait.inputTopAligned ||
    !portrait.inputBottomAligned ||
    portrait.primaryDisplay !== "grid" ||
    portrait.primaryRowCount < 2 ||
    portrait.selectorDisplay !== "grid" ||
    Math.abs(portrait.selectorGap) > 1
  ) {
    throw new Error(`Keyboard portrait layout failed: ${JSON.stringify(portrait)}`);
  }

  const sleepButton = page.getByRole("button", { name: "Sleep", exact: true });
  if ((await sleepButton.count()) === 1) {
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
      };
    });

    if (
      "error" in confirmation ||
      !confirmation.cancelFocused ||
      confirmation.cancelBorder === confirmation.confirmBorder
    ) {
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
    const availableWidth =
      shell.clientWidth -
      Number.parseFloat(shellStyle.paddingLeft) -
      Number.parseFloat(shellStyle.paddingRight);
    return {
      availableWidth,
      modeWidth: mode.getBoundingClientRect().width,
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
  const splitCheckbox = splitSection.getByRole("checkbox", {
    name: "Enable split mode",
    exact: true,
  });
  if (!(await splitCheckbox.isChecked())) {
    await splitCheckbox.click();
  }
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  await page.waitForSelector(".app-shell.split-mode-active");

  const splitLayout = await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    const keyboardPane = document.querySelector(".split-keyboard-pane");
    const keyboard = keyboardPane?.querySelector(".keyboard-mode");
    const finalButtons = keyboardPane
      ? Array.from(keyboardPane.querySelectorAll(".app-switch-row button"))
      : [];
    const trackpadSurface = document.querySelector(".split-trackpad-pane .trackpad-surface");
    const expandButton = trackpadSurface?.querySelector(".trackpad-expand-button");
    if (
      !(shell instanceof HTMLElement) ||
      !(keyboardPane instanceof HTMLElement) ||
      !(keyboard instanceof HTMLElement) ||
      finalButtons.length !== 2 ||
      !(trackpadSurface instanceof HTMLElement) ||
      !(expandButton instanceof HTMLElement)
    ) {
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
      finalButtonBottomGap:
        paneBounds.bottom - Math.max(...finalButtonBounds.map((bounds) => bounds.bottom)),
      finalButtonMinHeight: Number.parseFloat(getComputedStyle(finalButtons[0]).minHeight),
      keyboardPaddingBottom: Number.parseFloat(getComputedStyle(keyboard).paddingBottom),
      panePaddingBottom: Number.parseFloat(getComputedStyle(keyboardPane).paddingBottom),
    };
  });

  if (
    "error" in splitLayout ||
    Math.abs(splitLayout.expandRightGap - 10) > 1 ||
    Math.abs(splitLayout.finalButtonBottomGap) > 1 ||
    splitLayout.finalButtonMinHeight < 80 ||
    splitLayout.keyboardPaddingBottom !== 0 ||
    splitLayout.panePaddingBottom !== 0
  ) {
    throw new Error(`Split landscape safe-area layout failed: ${JSON.stringify(splitLayout)}`);
  }

  await page.getByRole("button", { name: "Expand trackpad", exact: true }).click();
  const expandedLayout = await page.evaluate(() => {
    const button = document.querySelector(
      ".split-trackpad-pane .trackpad-mode.expanded .trackpad-expand-button",
    );
    if (!(button instanceof HTMLElement)) {
      return { error: "Expanded split trackpad toggle was not visible." };
    }

    return { rightGap: window.innerWidth - button.getBoundingClientRect().right };
  });
  if ("error" in expandedLayout || Math.abs(expandedLayout.rightGap - 10) > 1) {
    throw new Error(
      `Expanded split trackpad safe-area layout failed: ${JSON.stringify(expandedLayout)}`,
    );
  }
  await page.getByRole("button", { name: "Restore trackpad", exact: true }).click();

  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Remote", exact: true }).click();
  await page.setViewportSize({ width: 852, height: 393 });
  const remoteVolumeLayout = await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    const section = document.querySelector(".remote-volume-section");
    const grid = section?.querySelector(".remote-volume-grid");
    const buttons = grid ? Array.from(grid.querySelectorAll("button")) : [];
    if (
      !(shell instanceof HTMLElement) ||
      !(section instanceof HTMLElement) ||
      !(grid instanceof HTMLElement) ||
      buttons.length !== 3
    ) {
      return { error: "Remote landscape volume controls were not visible." };
    }

    shell.style.setProperty("--mode-inline-safe-end", "120px");
    const sectionStyle = getComputedStyle(section);
    const gridBounds = grid.getBoundingClientRect();
    const buttonBounds = buttons.map((button) => button.getBoundingClientRect());
    return {
      equalButtonWidths:
        Math.max(...buttonBounds.map((bounds) => bounds.width)) -
          Math.min(...buttonBounds.map((bounds) => bounds.width)) <=
        1,
      gridRightGap:
        section.getBoundingClientRect().right -
        Number.parseFloat(sectionStyle.borderRightWidth) -
        Number.parseFloat(sectionStyle.paddingRight) -
        gridBounds.right,
      sectionPaddingRight: Number.parseFloat(sectionStyle.paddingRight),
    };
  });
  if (
    "error" in remoteVolumeLayout ||
    !remoteVolumeLayout.equalButtonWidths ||
    Math.abs(remoteVolumeLayout.gridRightGap) > 1 ||
    Math.abs(remoteVolumeLayout.sectionPaddingRight - 9) > 1
  ) {
    throw new Error(
      `Remote landscape volume safe-area layout failed: ${JSON.stringify(remoteVolumeLayout)}`,
    );
  }

  await page.evaluate(() => {
    const shell = document.querySelector(".app-shell");
    if (shell instanceof HTMLElement) {
      shell.style.removeProperty("--mode-bottom-safe-area");
      shell.style.removeProperty("--mode-inline-safe-end");
    }
  });
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Trackpad", exact: true }).click();
}

async function verifySettingsDrawerLifecycle(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  const drawer = page.locator(".settings-drawer");
  const readState = () =>
    drawer.evaluate((dialog) => ({
      display: getComputedStyle(dialog).display,
      open: dialog instanceof HTMLDialogElement && dialog.open,
      width: dialog.getBoundingClientRect().width,
    }));

  const initiallyClosed = await readState();
  if (initiallyClosed.open || initiallyClosed.display !== "none" || initiallyClosed.width !== 0) {
    throw new Error(
      `Settings drawer should start closed and hidden: ${JSON.stringify(initiallyClosed)}`,
    );
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
      drawerFocused: document.activeElement === dialog,
    };
  });
  if (!initialFocus.drawerFocused || initialFocus.backdropTabIndex !== -1) {
    throw new Error(`Settings drawer initial focus is incorrect: ${JSON.stringify(initialFocus)}`);
  }

  await page.keyboard.press("Tab");
  const firstTabTarget = await page.evaluate(() =>
    document.activeElement?.getAttribute("aria-label"),
  );
  if (firstTabTarget !== "Close menu") {
    throw new Error(
      `Settings drawer first Tab target should be the close button, received: ${JSON.stringify(firstTabTarget)}`,
    );
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
    const firstControl = trackpad?.querySelector(
      ".settings-section-body button, .settings-section-body input, .settings-section-body select, .settings-section-body textarea, .settings-section-body a[href], .settings-section-body [tabindex]",
    );
    if (
      !(scrollRegion instanceof HTMLElement) ||
      !(connection instanceof HTMLDetailsElement) ||
      !(trackpad instanceof HTMLDetailsElement) ||
      !(summary instanceof HTMLElement) ||
      !(firstControl instanceof HTMLElement) ||
      connection.open ||
      !trackpad.open
    ) {
      return false;
    }

    const regionBounds = scrollRegion.getBoundingClientRect();
    const summaryBounds = summary.getBoundingClientRect();
    const controlBounds = firstControl.getBoundingClientRect();
    return (
      summaryBounds.top >= regionBounds.top - 1 &&
      summaryBounds.bottom <= regionBounds.bottom + 1 &&
      controlBounds.top >= regionBounds.top - 1 &&
      controlBounds.bottom <= regionBounds.bottom + 1
    );
  });

  const accordionState = await page.evaluate(() => {
    const scrollRegion = document.querySelector(".settings-drawer-scroll-region");
    const connection = document.querySelector('[data-settings-section="connection"]');
    const trackpad = document.querySelector('[data-settings-section="trackpad"]');
    const summary = trackpad?.querySelector("summary");
    const firstControl = trackpad?.querySelector(
      ".settings-section-body button, .settings-section-body input, .settings-section-body select, .settings-section-body textarea, .settings-section-body a[href], .settings-section-body [tabindex]",
    );
    if (
      !(scrollRegion instanceof HTMLElement) ||
      !(connection instanceof HTMLDetailsElement) ||
      !(trackpad instanceof HTMLDetailsElement) ||
      !(summary instanceof HTMLElement) ||
      !(firstControl instanceof HTMLElement)
    ) {
      return { error: "Settings accordion controls were not visible." };
    }

    const regionBounds = scrollRegion.getBoundingClientRect();
    const summaryBounds = summary.getBoundingClientRect();
    const controlBounds = firstControl.getBoundingClientRect();
    return {
      connectionOpen: connection.open,
      firstControlVisible:
        controlBounds.top >= regionBounds.top - 1 &&
        controlBounds.bottom <= regionBounds.bottom + 1,
      summaryFocused: document.activeElement === summary,
      summaryVisible:
        summaryBounds.top >= regionBounds.top - 1 &&
        summaryBounds.bottom <= regionBounds.bottom + 1,
      trackpadOpen: trackpad.open,
    };
  });
  if (
    "error" in accordionState ||
    accordionState.connectionOpen ||
    !accordionState.trackpadOpen ||
    !accordionState.summaryVisible ||
    !accordionState.firstControlVisible ||
    !accordionState.summaryFocused
  ) {
    throw new Error(`Settings accordion assisted reveal failed: ${JSON.stringify(accordionState)}`);
  }

  await page.mouse.click(380, 426);
  const closedByBackdrop = await readState();
  if (
    closedByBackdrop.open ||
    closedByBackdrop.display !== "none" ||
    closedByBackdrop.width !== 0
  ) {
    throw new Error(
      `Settings drawer did not close from its backdrop: ${JSON.stringify(closedByBackdrop)}`,
    );
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  const closedByButton = await readState();
  if (closedByButton.open || closedByButton.display !== "none" || closedByButton.width !== 0) {
    throw new Error(
      `Settings drawer did not close from its close button: ${JSON.stringify(closedByButton)}`,
    );
  }

  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.keyboard.press("Escape");
  const closedByEscape = await readState();
  if (closedByEscape.open || closedByEscape.display !== "none" || closedByEscape.width !== 0) {
    throw new Error(`Settings drawer did not close with Escape: ${JSON.stringify(closedByEscape)}`);
  }
}

async function verifyAccentColorPicker(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.locator('[data-settings-section="appearance"] > summary').click();
  await page.getByRole("button", { name: /Voltura default/u }).click();

  const dialog = page.getByRole("dialog", { name: "Custom color", exact: true });
  await dialog.waitFor({ state: "visible" });
  const input = dialog.getByLabel("Hex color", { exact: true });
  await input.fill("#FFFF00");
  await page.screenshot({ path: path.join(tempArtifactsDir, "accent-picker-portrait.png") });

  const portrait = await dialog.evaluate((element) => {
    const surface = element.querySelector(".accent-picker-surface");
    if (!(surface instanceof HTMLElement)) {
      return { error: "Accent picker surface was not visible." };
    }
    const dialogBounds = element.getBoundingClientRect();
    const surfaceBounds = surface.getBoundingClientRect();
    return {
      dialogBottom: dialogBounds.bottom,
      dialogLeft: dialogBounds.left,
      dialogRight: dialogBounds.right,
      dialogTop: dialogBounds.top,
      surfaceHeight: surfaceBounds.height,
      surfaceWidth: surfaceBounds.width,
    };
  });
  if (
    "error" in portrait ||
    portrait.dialogLeft < 0 ||
    portrait.dialogTop < 0 ||
    portrait.dialogRight > 393 ||
    portrait.dialogBottom > 852 ||
    portrait.surfaceWidth < 260 ||
    portrait.surfaceHeight < 180
  ) {
    throw new Error(`Accent picker portrait layout failed: ${JSON.stringify(portrait)}`);
  }

  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  await page.getByRole("button", { name: /#FFFF00/u }).waitFor({ state: "visible" });
  await page.waitForFunction(() =>
    getComputedStyle(document.documentElement).getPropertyValue("--action").trim(),
  );
  const contrast = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement);
    const parse = (value) => {
      const hex = value.trim().match(/^#([0-9A-F]{6})$/iu)?.[1];
      if (hex) {
        return [0, 2, 4].map((offset) => Number.parseInt(hex.slice(offset, offset + 2), 16));
      }
      const match = value.match(/\d+(?:\.\d+)?/gu);
      return match?.slice(0, 3).map(Number) ?? [];
    };
    const luminance = (value) => {
      const channels = parse(value).map((channel) => {
        const normalized = channel / 255;
        return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
      });
      return channels.length === 3
        ? channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
        : 0;
    };
    const foreground = luminance(style.getPropertyValue("--on-action"));
    const background = luminance(style.getPropertyValue("--action"));
    return (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05);
  });
  if (contrast < 4.5) {
    throw new Error(`Accent action contrast was ${contrast.toFixed(2)} instead of at least 4.5.`);
  }

  await page.getByRole("button", { name: /#FFFF00/u }).click();
  await page.setViewportSize({ width: 852, height: 393 });
  await dialog.waitFor({ state: "visible" });
  await page.screenshot({ path: path.join(tempArtifactsDir, "accent-picker-landscape.png") });
  const landscape = await dialog.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    const actions = element.querySelector(".accent-picker-actions");
    const actionBounds = actions?.getBoundingClientRect();
    return {
      actionsBottom: actionBounds?.bottom ?? Number.POSITIVE_INFINITY,
      actionsTop: actionBounds?.top ?? Number.NEGATIVE_INFINITY,
      bottom: bounds.bottom,
      left: bounds.left,
      right: bounds.right,
      scrollable:
        element.scrollHeight <= element.clientHeight ||
        getComputedStyle(element).overflowY !== "visible",
      top: bounds.top,
    };
  });
  if (
    landscape.left < 0 ||
    landscape.top < 0 ||
    landscape.right > 852 ||
    landscape.bottom > 393 ||
    landscape.actionsTop < landscape.top ||
    landscape.actionsBottom > Math.min(landscape.bottom, 393) ||
    !landscape.scrollable
  ) {
    throw new Error(`Accent picker landscape layout failed: ${JSON.stringify(landscape)}`);
  }

  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await page.getByRole("button", { name: /#FFFF00/u }).waitFor({ state: "visible" });
  await page.getByRole("button", { name: "Use PC default", exact: true }).click();
  await page.getByRole("button", { name: /Voltura default/u }).waitFor({ state: "visible" });
  await page.waitForFunction(
    () => !document.documentElement.style.getPropertyValue("--action").trim(),
  );
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  await page.setViewportSize({ width: 393, height: 852 });
}

async function verifyResponsivePowerLayout(page) {
  await page.getByRole("button", { name: "Remote", exact: true }).click();
  await page.getByRole("button", { name: "Power", exact: true }).click();

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "compact phone portrait", width: 375, height: 667 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet portrait", width: 768, height: 1024 },
    { name: "tablet landscape", width: 1024, height: 768 },
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
        horizontalOverflow:
          document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minActionHeight: Math.min(...rows.map((row) => row.getBoundingClientRect().height)),
        outsideViewport:
          bounds.left < -1 ||
          bounds.top < -1 ||
          bounds.right > window.innerWidth + 1 ||
          bounds.bottom > window.innerHeight + 1,
      };
    });

    if (
      "error" in result ||
      result.actionCount !== 8 ||
      result.horizontalOverflow ||
      result.minActionHeight < 44 ||
      result.outsideViewport
    ) {
      throw new Error(
        `Responsive Power sheet check failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
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
    width: control.getBoundingClientRect().width,
  }));
  if (
    fourthModeMetrics.fontSize < 16 ||
    fourthModeMetrics.height < 48 ||
    fourthModeMetrics.width < 240
  ) {
    throw new Error(
      `Fourth mode button selector is too small: ${JSON.stringify(fourthModeMetrics)}`,
    );
  }
  await page.getByRole("button", { name: "Send text to PC", exact: true }).click();
  await page.getByRole("button", { name: "Use device keyboard", exact: true }).click();
  await page.getByLabel("Text to send").fill("Responsive text transfer check");
  const savedSnippets = page.locator(".saved-snippets");
  const snippetsStartFolded = await savedSnippets.evaluate(
    (details) => details instanceof HTMLDetailsElement && !details.open,
  );
  await page.locator(".saved-snippets > summary").click();
  await page.getByLabel("Snippet name").fill("Smoke snippet");
  await page.getByRole("button", { name: "Save current text", exact: true }).click();

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet landscape", width: 1024, height: 768 },
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(() => {
      const editor = document.querySelector(".text-transfer-editor textarea");
      const editorSurface = document.querySelector(".text-transfer-editor-surface");
      const editorField = document.querySelector(".text-transfer-editor");
      const editorHeading = document.querySelector(".text-transfer-editor-heading");
      const editorLabel = document.querySelector(".text-transfer-editor-heading > label");
      const actions = document.querySelector(".text-transfer-actions");
      const sendButtons = Array.from(document.querySelectorAll(".text-transfer-actions button"));
      const snippetInput = document.querySelector(".snippet-save-row input");
      const saveButton = document.querySelector(".snippet-save-row button");
      const snippetActions = Array.from(
        document.querySelectorAll(".saved-snippets li button:not(.snippet-load)"),
      );
      if (
        !(editor instanceof HTMLTextAreaElement) ||
        !(editorSurface instanceof HTMLElement) ||
        !(editorField instanceof HTMLElement) ||
        !(editorHeading instanceof HTMLElement) ||
        !(editorLabel instanceof HTMLElement) ||
        !(actions instanceof HTMLElement) ||
        sendButtons.length !== 2 ||
        !(snippetInput instanceof HTMLInputElement) ||
        !(saveButton instanceof HTMLButtonElement)
      ) {
        return {
          error: "Text transfer controls were not visible.",
          actions: actions instanceof HTMLElement,
          editor: editor instanceof HTMLTextAreaElement,
          editorField: editorField instanceof HTMLElement,
          editorHeading: editorHeading instanceof HTMLElement,
          editorLabel: editorLabel instanceof HTMLElement,
          editorSurface: editorSurface instanceof HTMLElement,
          saveButton: saveButton instanceof HTMLButtonElement,
          sendButtonCount: sendButtons.length,
          snippetInput: snippetInput instanceof HTMLInputElement,
        };
      }

      const editorSurfaceBounds = editorSurface.getBoundingClientRect();
      const editorFieldBounds = editorField.getBoundingClientRect();
      const editorHeadingBounds = editorHeading.getBoundingClientRect();
      const actionBounds = actions.getBoundingClientRect();
      const sendButtonBounds = sendButtons.map((button) => button.getBoundingClientRect());
      return {
        backButtonPresent: document.querySelector(".text-transfer-mode .tool-back-button") !== null,
        editorHeadingGap: editorSurfaceBounds.top - editorHeadingBounds.bottom,
        editorMisaligned:
          Math.abs(editorSurfaceBounds.left - editorFieldBounds.left) > 1 ||
          Math.abs(editorSurfaceBounds.width - editorFieldBounds.width) > 2,
        editorOverlapsActions: editorSurfaceBounds.bottom > actionBounds.top + 1,
        editorUsesTrackpadGrid: getComputedStyle(editorSurface).backgroundImage !== "none",
        horizontalOverflow:
          document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        maxSnippetActionHeight: Math.max(
          ...snippetActions.map((button) => button.getBoundingClientRect().height),
        ),
        sendButtonsShareRow: Math.abs(sendButtonBounds[0].top - sendButtonBounds[1].top) <= 1,
        snippetInputOpaque: getComputedStyle(snippetInput).backgroundColor !== "rgba(0, 0, 0, 0)",
        snippetInputWidth: snippetInput.getBoundingClientRect().width,
      };
    });

    if (
      !snippetsStartFolded ||
      "error" in result ||
      result.backButtonPresent ||
      (viewport.name === "phone portrait" && result.editorHeadingGap > 5) ||
      result.editorMisaligned ||
      result.editorOverlapsActions ||
      result.editorUsesTrackpadGrid ||
      result.horizontalOverflow ||
      result.maxSnippetActionHeight > 45 ||
      !result.sendButtonsShareRow ||
      !result.snippetInputOpaque ||
      result.snippetInputWidth < 160
    ) {
      throw new Error(
        `Responsive text transfer check failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
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
    if (
      !(standardInput instanceof HTMLInputElement) ||
      !(input instanceof HTMLInputElement) ||
      !(closeButton instanceof HTMLButtonElement) ||
      buttons.length !== 2
    ) {
      return { error: "Themed snippet dialog controls were not visible." };
    }
    return {
      buttonsUseElements: buttons.every((button) => button instanceof HTMLButtonElement),
      closeButtonAccessible: closeButton.getAttribute("aria-label") === "Close Rename snippet",
      fontMatchesApp:
        getComputedStyle(dialog).fontFamily === getComputedStyle(document.body).fontFamily,
      inputMatchesTheme:
        getComputedStyle(input).backgroundColor === getComputedStyle(standardInput).backgroundColor,
      minButtonHeight: Math.min(...buttons.map((button) => button.getBoundingClientRect().height)),
      opaqueBackground: getComputedStyle(dialog).backgroundColor !== "rgba(0, 0, 0, 0)",
    };
  });
  if (
    "error" in dialogMetrics ||
    !dialogMetrics.buttonsUseElements ||
    !dialogMetrics.closeButtonAccessible ||
    !dialogMetrics.fontMatchesApp ||
    !dialogMetrics.inputMatchesTheme ||
    dialogMetrics.minButtonHeight < 48 ||
    !dialogMetrics.opaqueBackground
  ) {
    throw new Error(`Themed snippet dialog check failed: ${JSON.stringify(dialogMetrics)}`);
  }
  await renameDialog.getByRole("button", { name: "Cancel", exact: true }).click();
}

async function verifyResponsiveUrlOpenLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Remote", exact: true }).click();
  await page.getByRole("button", { name: "Fn", exact: true }).click();
  await page.getByRole("button", { name: "Open URL", exact: true }).click();
  const urlDialog = page.getByRole("dialog", { name: "Open URL on PC", exact: true });
  const input = urlDialog.getByRole("textbox", { name: "Web address", exact: true });

  if ((await input.count()) === 0) {
    const permissionMessage = urlDialog.getByText(
      "Allow URL opening in the PC permissions first.",
      { exact: true },
    );
    if (
      (await permissionMessage.count()) !== 1 ||
      (await urlDialog.getByRole("button", { name: "Open", exact: true }).count()) !== 0
    ) {
      throw new Error("URL controls were not hidden with the PC permission disabled.");
    }
    return;
  }

  await input.fill("javascript:alert(1)");
  if (
    !(await urlDialog.getByRole("button", { name: "Open", exact: true }).isDisabled()) ||
    (await urlDialog.getByText("Use an HTTP or HTTPS web address.", { exact: true }).count()) !== 1
  ) {
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
      okOffsetRight: dialogBounds.right - ok.getBoundingClientRect().right,
      text: dialog.textContent ?? "",
    };
  });
  if (
    "error" in infoMetrics ||
    infoMetrics.height < 250 ||
    infoMetrics.okOffsetRight > 40 ||
    !infoMetrics.text.includes("Addresses without a scheme use HTTPS")
  ) {
    throw new Error(`URL information dialog check failed: ${JSON.stringify(infoMetrics)}`);
  }
  await infoDialog.getByRole("button", { name: "OK", exact: true }).click();
  await input.fill("example.com/page?q=responsive-test");

  const viewports = [
    { name: "phone portrait", width: 393, height: 852 },
    { name: "compact phone portrait", width: 375, height: 667 },
    { name: "phone landscape", width: 852, height: 393 },
    { name: "tablet landscape", width: 1024, height: 768 },
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.waitForFunction((width) => {
      const dialog = document.querySelector(".remote-url-dialog");
      return (
        dialog instanceof HTMLElement &&
        getComputedStyle(dialog).getPropertyValue("--modal-visual-viewport-width").trim() ===
          `${width}px`
      );
    }, viewport.width);
    await input.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    const result = await page.evaluate(() => {
      const form = document.querySelector(".remote-url-dialog form");
      const field = document.querySelector("#remote-url-draft");
      const button = document.querySelector(".remote-url-dialog-primary");
      if (
        !(form instanceof HTMLElement) ||
        !(field instanceof HTMLInputElement) ||
        !(button instanceof HTMLButtonElement)
      ) {
        return { error: "URL opening controls were not visible." };
      }

      const bounds = form.getBoundingClientRect();
      const fieldStyle = getComputedStyle(field);
      return {
        active: document.activeElement === field,
        buttonHeight: button.getBoundingClientRect().height,
        draft: field.value,
        fieldWidth: field.getBoundingClientRect().width,
        fieldBackground: fieldStyle.backgroundColor,
        fieldBorderColor: fieldStyle.borderColor,
        fieldOutlineColor: fieldStyle.outlineColor,
        fieldOutlineOffset: fieldStyle.outlineOffset,
        focusVisible: field.matches(":focus-visible"),
        formBackground: getComputedStyle(form).backgroundColor,
        horizontalOverflow:
          document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        outsideViewport: bounds.left < -1 || bounds.right > window.innerWidth + 1,
      };
    });

    if (
      "error" in result ||
      result.buttonHeight < 44 ||
      !result.active ||
      result.fieldWidth < 160 ||
      result.fieldBackground === result.formBackground ||
      result.fieldBorderColor !== result.fieldOutlineColor ||
      result.fieldOutlineOffset !== "0px" ||
      !result.focusVisible ||
      result.horizontalOverflow ||
      result.outsideViewport ||
      result.draft !== "example.com/page?q=responsive-test"
    ) {
      throw new Error(
        `Responsive URL opening check failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
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
  await reconnectPanel
    .getByRole("heading", { name: "PC disconnected", exact: true })
    .waitFor({ state: "visible" });
  const blockingState = await page.evaluate(() => {
    const panel = document.querySelector(".pairing-required");
    const backdrop = document.querySelector(".pairing-backdrop");
    const menuButton = document.querySelector('[aria-label="Open menu"]');
    if (
      !(panel instanceof HTMLElement) ||
      !(backdrop instanceof HTMLElement) ||
      !(menuButton instanceof HTMLElement)
    ) {
      return { error: "Blocking reconnect panel was incomplete." };
    }

    const menuBounds = menuButton.getBoundingClientRect();
    const hitTarget = document.elementFromPoint(
      menuBounds.left + menuBounds.width / 2,
      menuBounds.top + menuBounds.height / 2,
    );
    const backdropBounds = backdrop.getBoundingClientRect();
    return {
      appChromeBlocked: hitTarget === backdrop,
      backdropCoversViewport:
        backdropBounds.left <= 0 &&
        backdropBounds.top <= 0 &&
        backdropBounds.right >= window.innerWidth &&
        backdropBounds.bottom >= window.innerHeight,
      modal: panel.getAttribute("aria-modal") === "true" && panel.getAttribute("role") === "dialog",
    };
  });
  if (
    "error" in blockingState ||
    !blockingState.appChromeBlocked ||
    !blockingState.backdropCoversViewport ||
    !blockingState.modal
  ) {
    throw new Error(
      `Disconnected saved-PC reconnect blocking state failed: ${JSON.stringify(blockingState)}`,
    );
  }
  const reconnectButton = reconnectPanel.getByRole("button", {
    name: "Try reconnect",
    exact: true,
  });
  const qrButton = reconnectPanel.getByRole("button", {
    name: "Take photo of QR code",
    exact: true,
  });

  const viewports = [
    { name: "regular portrait", width: 393, height: 852 },
    { name: "short landscape", width: 640, height: 360 },
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
        horizontalOverflow:
          document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minActionHeight: Math.min(
          ...actions.map((button) => button.getBoundingClientRect().height),
        ),
        outsideViewport: bounds.left < -1 || bounds.right > window.innerWidth + 1,
        scrollableWhenNeeded:
          panel.scrollHeight <= panel.clientHeight + 1 ||
          getComputedStyle(panel).overflowY === "auto",
      };
    });

    if (
      result.actionCount !== 2 ||
      result.horizontalOverflow ||
      result.minActionHeight < 48 ||
      result.outsideViewport ||
      !result.scrollableWhenNeeded
    ) {
      throw new Error(
        `Disconnected saved-PC reconnect layout failed for ${viewport.name}: ${JSON.stringify(result)}`,
      );
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
    viewport: smokeTest
      ? { width: debugDevice.screen.vertical.width, height: debugDevice.screen.vertical.height }
      : null,
    args: smokeTest
      ? ["--test-type"]
      : ["--start-maximized", "--auto-open-devtools-for-tabs", "--test-type"],
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
        maximized: true,
      },
    },
    devtools: {
      preferences: {
        currentDockState: JSON.stringify("right"),
        "custom-emulated-device-list": JSON.stringify(devUiDevices),
        customEmulatedDeviceList: JSON.stringify(devUiDevices),
        "emulation.device-mode-value": JSON.stringify({
          device: device.title,
          orientation: "vertical",
          mode: "",
        }),
        "emulation.device-scale": "0.86",
        "emulation.show-device-mode": "true",
      },
    },
  };

  await writeJson(path.join(browserProfileDir, "Preferences"), preferences);
  await writeJson(path.join(browserProfileDir, "Default", "Preferences"), preferences);
}

async function applyDeviceEmulation(page, device) {
  const vertical = device.screen.vertical;
  const client = await page.context().newCDPSession(page);
  deviceEmulationClients.set(page, client);
  await client.send("Emulation.setDeviceMetricsOverride", {
    width: vertical.width,
    height: vertical.height,
    deviceScaleFactor: device.screen["device-pixel-ratio"],
    mobile: device.capabilities.includes("mobile"),
    screenWidth: vertical.width,
    screenHeight: vertical.height,
  });
  await client.send("Emulation.setTouchEmulationEnabled", {
    enabled: device.capabilities.includes("touch"),
    maxTouchPoints: device.capabilities.includes("touch") ? 1 : 0,
  });
}

async function setEmulatedViewport(page, viewport) {
  const client = deviceEmulationClients.get(page);
  if (!client) {
    throw new Error("Device emulation must be initialized before changing the viewport.");
  }

  await page.setViewportSize({ width: viewport.width, height: viewport.height });
  const landscape = viewport.width > viewport.height;
  await client.send("Emulation.setDeviceMetricsOverride", {
    width: viewport.width,
    height: viewport.height,
    deviceScaleFactor: debugDevice.screen["device-pixel-ratio"],
    mobile: debugDevice.capabilities.includes("mobile"),
    screenWidth: viewport.width,
    screenHeight: viewport.height,
    screenOrientation: {
      type: landscape ? "landscapePrimary" : "portraitPrimary",
      angle: landscape ? 90 : 0,
    },
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
    } catch {}
    await delay(250);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

function spawnCommand(command, args, env, options = {}) {
  const commandLine = [command, ...args].join(" ");
  const child =
    process.platform === "win32"
      ? spawn("cmd.exe", ["/d", "/s", "/c", commandLine], {
          stdio: "inherit",
          env,
          windowsHide: false,
          ...options,
        })
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
    const [spawnFile, spawnArgs] =
      process.platform === "win32"
        ? ["cmd.exe", ["/d", "/s", "/c", [executable, ...args].join(" ")]]
        : [executable, args];
    const child = spawn(spawnFile, spawnArgs, {
      cwd: options.cwd ?? repoRoot,
      stdio: "inherit",
      windowsHide: true,
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
