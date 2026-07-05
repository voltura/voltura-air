import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

const browserProfileDir = path.join(tmpdir(), "voltura-air-dev-ui", "chrome-profile");
const originalRm = fs.rm.bind(fs);

fs.rm = async function rmAndSeedDevUiDevices(target, options) {
  const result = await originalRm(target, options);

  if (path.resolve(String(target)) === path.resolve(browserProfileDir)) {
    await seedDevUiDevices(browserProfileDir);
  }

  return result;
};

async function seedDevUiDevices(userDataDir) {
  const devices = [
    device("Voltura 360x780 - Compact Android", 360, 780, 3, "phone"),
    device("Voltura 375x667 - iPhone SE Small", 375, 667, 2, "phone"),
    device("Voltura 390x844 - Common iPhone", 390, 844, 3, "phone"),
    device("Voltura 393x852 - iPhone Pro", 393, 852, 3, "phone"),
    device("Voltura 412x915 - Large Android", 412, 915, 3, "phone"),
    device("Voltura 430x932 - iPhone Pro Max", 430, 932, 3, "phone"),
    device("Voltura 768x1024 - Small Tablet", 768, 1024, 2, "tablet"),
    device("Voltura 820x1180 - iPad Air", 820, 1180, 2, "tablet")
  ];

  await upsertPreferences(path.join(userDataDir, "Default", "Preferences"), devices);
  await upsertPreferences(path.join(userDataDir, "Preferences"), devices);
}

async function upsertPreferences(preferencesPath, devices) {
  await fs.mkdir(path.dirname(preferencesPath), { recursive: true });

  const preferences = await readJson(preferencesPath);
  preferences.devtools ??= {};
  preferences.devtools.preferences ??= {};

  const devtoolsPreferences = preferences.devtools.preferences;
  const keys = ["custom-emulated-device-list", "customEmulatedDeviceList"];
  const existing = readExistingDevices(devtoolsPreferences, keys);
  const newTitles = new Set(devices.map((item) => item.title));
  const preserved = existing.filter((item) => !newTitles.has(item?.title));
  const value = JSON.stringify([...preserved, ...devices]);

  for (const key of keys) {
    devtoolsPreferences[key] = value;
  }

  await fs.writeFile(preferencesPath, JSON.stringify(preferences, null, 2), "utf8");
}

function readExistingDevices(devtoolsPreferences, keys) {
  for (const key of keys) {
    const value = devtoolsPreferences[key];
    if (typeof value !== "string" || !value.trim()) {
      continue;
    }

    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  return [];
}

async function readJson(filePath) {
  if (!existsSync(filePath)) {
    return {};
  }

  try {
    return JSON.parse(await fs.readFile(filePath, "utf8"));
  } catch {
    return {};
  }
}

function device(title, width, height, dpr, type) {
  return {
    "show-by-default": true,
    title,
    screen: {
      horizontal: { width: height, height: width },
      "device-pixel-ratio": dpr,
      vertical: { width, height }
    },
    capabilities: ["touch", "mobile"],
    "user-agent": "",
    type,
    modes: [
      {
        title: "",
        orientation: "vertical",
        insets: { left: 0, top: 0, right: 0, bottom: 0 }
      },
      {
        title: "",
        orientation: "horizontal",
        insets: { left: 0, top: 0, right: 0, bottom: 0 }
      }
    ],
    "dual-screen": false,
    show: "Default"
  };
}
