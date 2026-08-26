import { describe, expect, it } from "vitest";
import { screenKeyboardMessage } from "./screenKeyboardInput";

function keyEvent(key: string, overrides: Partial<KeyboardEvent> = {}) {
  return {
    altKey: false,
    code: key,
    ctrlKey: false,
    getModifierState: () => false,
    isComposing: false,
    key,
    metaKey: false,
    shiftKey: false,
    ...overrides,
  } as Pick<
    KeyboardEvent,
    | "altKey"
    | "code"
    | "ctrlKey"
    | "getModifierState"
    | "isComposing"
    | "key"
    | "metaKey"
    | "shiftKey"
  >;
}

describe("screen keyboard input", () => {
  it("forwards resolved printable text without duplicating Shift", () => {
    expect(screenKeyboardMessage(keyEvent("A", { code: "KeyA", shiftKey: true }))).toEqual({
      type: "keyboard.text",
      text: "A",
    });
    expect(screenKeyboardMessage(keyEvent("å", { code: "BracketLeft" }))).toEqual({
      type: "keyboard.text",
      text: "å",
    });
    expect(screenKeyboardMessage(keyEvent("😀", { code: "" }))).toEqual({
      type: "keyboard.text",
      text: "😀",
    });
    expect(
      screenKeyboardMessage(
        keyEvent("@", {
          altKey: true,
          code: "Digit2",
          ctrlKey: true,
          getModifierState: (modifier) => modifier === "AltGraph",
        }),
      ),
    ).toEqual({ type: "keyboard.text", text: "@" });
  });

  it("forwards Escape, navigation keys, and shortcut chords", () => {
    expect(screenKeyboardMessage(keyEvent("Escape"))).toEqual({
      type: "keyboard.special",
      key: "Escape",
    });
    expect(screenKeyboardMessage(keyEvent("ArrowLeft"))).toEqual({
      type: "keyboard.special",
      key: "ArrowLeft",
    });
    expect(screenKeyboardMessage(keyEvent("c", { code: "KeyC", ctrlKey: true }))).toEqual({
      type: "keyboard.special",
      key: "c",
      modifiers: ["Control"],
    });
  });

  it("ignores composition and standalone modifier events", () => {
    expect(screenKeyboardMessage(keyEvent("Dead"))).toBeNull();
    expect(screenKeyboardMessage(keyEvent("a", { isComposing: true }))).toBeNull();
    expect(screenKeyboardMessage(keyEvent("Shift", { shiftKey: true }))).toBeNull();
  });
});
