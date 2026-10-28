import type { KeyboardSpecialMessage, KeyboardTextMessage } from "../protocol/messages";

type KeyboardInputMessage = KeyboardSpecialMessage | KeyboardTextMessage;

export const liveKeyboardSentinel = "\u2060";

export function getKeyboardDeltaMessages(
  previous: string,
  next: string,
  caretAtEnd = true,
): KeyboardInputMessage[] {
  if (previous === next) {
    return [];
  }

  const previousPoints = Array.from(previous);
  const nextPoints = Array.from(next);
  let prefixLength = 0;
  while (
    prefixLength < previousPoints.length &&
    prefixLength < nextPoints.length &&
    previousPoints[prefixLength] === nextPoints[prefixLength]
  ) {
    prefixLength += 1;
  }

  let suffixLength = 0;
  if (!caretAtEnd) {
    while (
      suffixLength < previousPoints.length - prefixLength &&
      suffixLength < nextPoints.length - prefixLength &&
      previousPoints[previousPoints.length - 1 - suffixLength] ===
        nextPoints[nextPoints.length - 1 - suffixLength]
    ) {
      suffixLength += 1;
    }
  }
  // At the end, replace the complete tail so autocorrect preserves its suffix.
  // After explicit navigation, leave the text beyond the remote caret alone.
  const removedCount = previousPoints.length - prefixLength - suffixLength;
  const insertedText = nextPoints.slice(prefixLength, nextPoints.length - suffixLength).join("");
  const messages: KeyboardInputMessage[] = [];

  for (let index = 0; index < removedCount; index += 1) {
    messages.push({ type: "keyboard.special", key: "Backspace" });
  }

  messages.push(...textToMessages(insertedText, insertedText.length === 1));
  return messages;
}

export function toLiveKeyboardValue(text: string): string {
  return `${liveKeyboardSentinel}${text}`;
}

export function fromLiveKeyboardValue(value: string): string {
  return value.startsWith(liveKeyboardSentinel) ? value.slice(liveKeyboardSentinel.length) : value;
}

export function didDeleteLiveKeyboardSentinel(previousText: string, nextRawValue: string): boolean {
  return previousText.length === 0 && nextRawValue.length === 0;
}

export function getEmptyDeleteMessage(
  inputTypeOrKey: string,
  currentText: string,
): KeyboardSpecialMessage | null {
  if (currentText.length > 0) {
    return null;
  }

  if (inputTypeOrKey === "Backspace" || inputTypeOrKey === "deleteContentBackward") {
    return { type: "keyboard.special", key: "Backspace" };
  }

  if (inputTypeOrKey === "Delete" || inputTypeOrKey === "deleteContentForward") {
    return { type: "keyboard.special", key: "Delete" };
  }

  return null;
}

function textToMessages(text: string, preferShortcutKeys: boolean): KeyboardInputMessage[] {
  const messages: KeyboardInputMessage[] = [];
  let buffer = "";

  for (const character of text) {
    if (character === "\n") {
      if (buffer.length > 0) {
        messages.push({ type: "keyboard.text", text: buffer });
        buffer = "";
      }
      messages.push({ type: "keyboard.special", key: "Enter" });
    } else if (preferShortcutKeys && character.toLowerCase() === "f") {
      if (buffer.length > 0) {
        messages.push({ type: "keyboard.text", text: buffer });
        buffer = "";
      }

      messages.push(
        character === "F"
          ? { type: "keyboard.special", key: "F", modifiers: ["Shift"] }
          : { type: "keyboard.special", key: "F" },
      );
    } else {
      buffer += character;
    }
  }

  if (buffer.length > 0) {
    messages.push({ type: "keyboard.text", text: buffer });
  }

  return messages;
}
