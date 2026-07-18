import type { ClientMessage, KeyboardSpecialMessage } from "../protocol/messages";

export const liveKeyboardSentinel = "\u2060";

export function getKeyboardDeltaMessages(previous: string, next: string): ClientMessage[] {
  if (previous === next) {
    return [];
  }

  let prefixLength = 0;
  while (prefixLength < previous.length && prefixLength < next.length && previous[prefixLength] === next[prefixLength]) {
    prefixLength += 1;
  }

  let suffixLength = 0;
  while (
    suffixLength < previous.length - prefixLength &&
    suffixLength < next.length - prefixLength &&
    previous[previous.length - 1 - suffixLength] === next[next.length - 1 - suffixLength]
  ) {
    suffixLength += 1;
  }

  const removedCount = previous.length - prefixLength - suffixLength;
  const insertedText = next.slice(prefixLength, next.length - suffixLength);
  const messages: ClientMessage[] = [];

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

export function getEmptyDeleteMessage(inputTypeOrKey: string, currentText: string): KeyboardSpecialMessage | null {
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

function textToMessages(text: string, preferShortcutKeys: boolean): ClientMessage[] {
  const messages: ClientMessage[] = [];
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
          : { type: "keyboard.special", key: "F" }
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
