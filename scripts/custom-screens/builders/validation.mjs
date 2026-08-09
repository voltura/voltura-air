import { allowedPortableActionKinds } from "./actions.mjs";

const ids = /^[A-Za-z0-9._-]{1,64}$/u;
const allowedKeys = new Set([
  ...Array.from({ length: 26 }, (_, index) => String.fromCharCode(65 + index)),
  ...Array.from({ length: 10 }, (_, index) => String(index)),
  ...Array.from({ length: 12 }, (_, index) => `F${index + 1}`),
  "Backspace", "Delete", "Enter", "Insert", "Tab", "Escape", "Space", "PageUp", "PageDown", "Home", "End",
  "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "BrowserBack", "BrowserForward", "+",
  ".", ",", ";", "/", "\\", "'", "`", "[", "]", "-", "=",
  "MediaStop", "MediaPlayPause", "MediaPreviousTrack", "MediaNextTrack", "VolumeUp", "VolumeDown", "VolumeMute",
  ...Array.from({ length: 10 }, (_, index) => `Numpad${index}`),
  "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadDecimal"
]);
const knownApps = new Set(["browser", "spotify", "vlc", "zoom", "plex", "windowsPhotos", "blender"]);
const hostActions = new Set([
  "power.lock", "power.sleep", "power.hibernate", "power.restart", "power.shutdown",
  "display.off", "display.duplicate", "display.extend", "display.pcOnly", "display.secondOnly"
]);

export function validateDefinition(definition) {
  const screen = definition?.screen;
  if (!screen || !ids.test(screen.id) || !ids.test(screen.revision) || screen.name.length > 24 || screen.assignedClientIds.length !== 0) {
    throw new Error(`Invalid screen identity for ${screen?.id ?? "unknown"}.`);
  }
  const seen = new Set([screen.id]);
  let buttonCount = 0;
  for (const section of screen.sections) {
    if (!ids.test(section.id) || seen.has(section.id) || ![3, 4, 6, 8, 9, 12].includes(section.widthColumns)) {
      throw new Error(`Invalid or duplicate panel ${section.id} in ${screen.id}.`);
    }
    seen.add(section.id);
    buttonCount += section.buttons.length;
    for (const control of section.buttons) {
      if (!ids.test(control.id) || seen.has(control.id)) {
        throw new Error(`Invalid or duplicate control ${control.id} in ${screen.id}.`);
      }
      seen.add(control.id);
      validateAction(control.action, screen.id, control.id);
    }
  }
  if (screen.sections.length > 64 || buttonCount > 256) {
    throw new Error(`Layout limits exceeded by ${screen.id}.`);
  }
}

function validateAction(action, screenId, buttonId) {
  if (!action || !allowedPortableActionKinds.has(action.kind)) {
    throw new Error(`Unsupported portable action on ${screenId}/${buttonId}.`);
  }
  if (action.kind === "shortcut" && (!allowedKeys.has(action.key) || !Array.isArray(action.modifiers))) {
    throw new Error(`Unsupported shortcut on ${screenId}/${buttonId}.`);
  }
  if (action.kind === "urlOpen") {
    const url = new URL(action.url);
    if (url.protocol !== "http:" && url.protocol !== "https:") throw new Error(`Unsupported URL on ${screenId}/${buttonId}.`);
  }
  if (action.kind === "knownApp" && !knownApps.has(action.actionId)) throw new Error(`Unknown app on ${screenId}/${buttonId}.`);
  if (action.kind === "hostAction" && !hostActions.has(action.actionId)) throw new Error(`Unknown host action on ${screenId}/${buttonId}.`);
}

export function stableJson(value) {
  return `${JSON.stringify(sortValue(value), null, 2)}\n`;
}

function sortValue(value) {
  if (Array.isArray(value)) return value.map(sortValue);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, sortValue(value[key])]));
  }
  return value;
}
