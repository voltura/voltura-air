import type {
  KeyboardSpecialMessage,
  KeyboardTextMessage,
} from "../../foundation/protocol/messages";

type ScreenKeyboardMessage = KeyboardSpecialMessage | KeyboardTextMessage;

const supportedSpecialKeys = new Set([
  "Backspace",
  "Tab",
  "Enter",
  "Escape",
  "ArrowLeft",
  "ArrowUp",
  "ArrowRight",
  "ArrowDown",
  "Insert",
  "Delete",
  "Home",
  "End",
  "PageUp",
  "PageDown",
  "BrowserBack",
  "BrowserForward",
  "MediaNextTrack",
  "MediaPreviousTrack",
  "MediaStop",
  "MediaPlayPause",
  "VolumeMute",
  "VolumeDown",
  "VolumeUp",
  ...Array.from({ length: 12 }, (_, index) => `F${index + 1}`),
  "Numpad0",
  "Numpad1",
  "Numpad2",
  "Numpad3",
  "Numpad4",
  "Numpad5",
  "Numpad6",
  "Numpad7",
  "Numpad8",
  "Numpad9",
  "NumpadMultiply",
  "NumpadAdd",
  "NumpadSubtract",
  "NumpadDecimal",
  "NumpadDivide",
]);

export function screenKeyboardMessage(
  event: Pick<
    KeyboardEvent,
    | "altKey"
    | "code"
    | "ctrlKey"
    | "getModifierState"
    | "isComposing"
    | "key"
    | "metaKey"
    | "shiftKey"
  >,
): ScreenKeyboardMessage | null {
  if (
    event.isComposing ||
    event.key === "Dead" ||
    event.key === "Process" ||
    event.key === "Unidentified" ||
    isModifierKey(event.key)
  ) {
    return null;
  }

  const altGraph = event.getModifierState("AltGraph");
  const hasShortcutModifier = event.metaKey || (!altGraph && (event.ctrlKey || event.altKey));
  if (isPrintableText(event.key) && !hasShortcutModifier) {
    return { type: "keyboard.text", text: event.key };
  }

  const key = screenSpecialKey(event.key, event.code);
  if (!key) {
    return null;
  }
  const modifiers = keyboardModifiers(event, altGraph);
  return {
    type: "keyboard.special",
    key,
    ...(modifiers.length > 0 ? { modifiers } : {}),
  };
}

function isPrintableText(key: string) {
  return key.length === 1 || Array.from(key).some((character) => character.codePointAt(0)! > 0x7f);
}

function screenSpecialKey(key: string, code: string) {
  if (key === " ") {
    return "Space";
  }
  if (code.startsWith("Numpad") && supportedSpecialKeys.has(code)) {
    return code;
  }
  return supportedSpecialKeys.has(key) || key.length === 1 ? key : null;
}

function keyboardModifiers(
  event: Pick<KeyboardEvent, "altKey" | "ctrlKey" | "metaKey" | "shiftKey">,
  altGraph: boolean,
) {
  const modifiers: string[] = [];
  if (altGraph) {
    modifiers.push("AltGr");
  } else {
    if (event.ctrlKey) {
      modifiers.push("Control");
    }
    if (event.altKey) {
      modifiers.push("Alt");
    }
  }
  if (event.shiftKey) {
    modifiers.push("Shift");
  }
  if (event.metaKey) {
    modifiers.push("Win");
  }
  return modifiers;
}

function isModifierKey(key: string) {
  return (
    key === "Alt" || key === "AltGraph" || key === "Control" || key === "Meta" || key === "Shift"
  );
}
