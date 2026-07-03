import { describe, expect, it } from "vitest";
import {
  didDeleteLiveKeyboardSentinel,
  fromLiveKeyboardValue,
  getEmptyDeleteMessage,
  getKeyboardDeltaMessages,
  liveKeyboardSentinel,
  toLiveKeyboardValue
} from "./keyboardDelta";

describe("getKeyboardDeltaMessages", () => {
  it("sends inserted text", () => {
    expect(getKeyboardDeltaMessages("hel", "hello")).toEqual([{ type: "keyboard.text", text: "lo" }]);
  });

  it("sends backspace for removed text", () => {
    expect(getKeyboardDeltaMessages("hello", "hel")).toEqual([
      { type: "keyboard.special", key: "Backspace" },
      { type: "keyboard.special", key: "Backspace" }
    ]);
  });

  it("handles autocorrect-style replacements", () => {
    expect(getKeyboardDeltaMessages("teh", "the")).toEqual([
      { type: "keyboard.special", key: "Backspace" },
      { type: "keyboard.special", key: "Backspace" },
      { type: "keyboard.text", text: "he" }
    ]);
  });

  it("turns new lines into Enter key presses", () => {
    expect(getKeyboardDeltaMessages("", "a\nb")).toEqual([
      { type: "keyboard.text", text: "a" },
      { type: "keyboard.special", key: "Enter" },
      { type: "keyboard.text", text: "b" }
    ]);
  });

  it("sends backspace and delete when the local field is empty", () => {
    expect(getEmptyDeleteMessage("Backspace", "")).toEqual({ type: "keyboard.special", key: "Backspace" });
    expect(getEmptyDeleteMessage("deleteContentBackward", "")).toEqual({ type: "keyboard.special", key: "Backspace" });
    expect(getEmptyDeleteMessage("Delete", "")).toEqual({ type: "keyboard.special", key: "Delete" });
    expect(getEmptyDeleteMessage("deleteContentForward", "")).toEqual({ type: "keyboard.special", key: "Delete" });
  });

  it("does not send empty delete messages while local text can still be edited", () => {
    expect(getEmptyDeleteMessage("Backspace", "local")).toBeNull();
  });

  it("wraps live keyboard values with an invisible sentinel", () => {
    expect(toLiveKeyboardValue("abc")).toBe(`${liveKeyboardSentinel}abc`);
    expect(fromLiveKeyboardValue(`${liveKeyboardSentinel}abc`)).toBe("abc");
  });

  it("detects virtual keyboard backspace by sentinel deletion", () => {
    expect(didDeleteLiveKeyboardSentinel("", "")).toBe(true);
    expect(didDeleteLiveKeyboardSentinel("abc", "")).toBe(false);
  });
});
