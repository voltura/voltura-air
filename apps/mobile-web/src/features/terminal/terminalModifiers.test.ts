import { describe, expect, it } from "vitest";
import { applyTerminalModifier } from "./terminalModifiers";

describe("terminal modifiers", () => {
  it("turns the next printable key into a one-shot Ctrl combination", () => {
    expect(applyTerminalModifier("ctrl", "l")).toEqual({
      bytes: Uint8Array.of(12),
      consumed: true,
    });
  });

  it("prefixes the next printable Alt key with Escape", () => {
    const result = applyTerminalModifier("alt", "b");
    expect(Array.from(result.bytes)).toEqual([27, 98]);
    expect(result.consumed).toBe(true);
  });

  it("does not consume an armed modifier for focus or composition sequences", () => {
    const result = applyTerminalModifier("ctrl", "\u001b[I");
    expect(Array.from(result.bytes)).toEqual([27, 91, 73]);
    expect(result.consumed).toBe(false);
  });
});
