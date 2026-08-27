export type TerminalModifier = "ctrl" | "alt";

const encoder = new TextEncoder();

export function applyTerminalModifier(
  modifier: TerminalModifier | null,
  data: string,
): { bytes: Uint8Array; consumed: boolean } {
  const code = data.length === 1 ? data.charCodeAt(0) : -1;
  const appliesModifier = code >= 0x20 && code <= 0x7e;
  if (modifier === "ctrl" && appliesModifier) {
    return { bytes: Uint8Array.of(data.toUpperCase().charCodeAt(0) & 31), consumed: true };
  }
  if (modifier === "alt" && appliesModifier) {
    return { bytes: encoder.encode(`\u001b${data}`), consumed: true };
  }
  return { bytes: encoder.encode(data), consumed: false };
}
