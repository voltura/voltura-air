import { maximumInnerMessageBytes } from "../core/index";

export function parseSecureDescription(payload: Uint8Array, expectedType: "secure.offer" | "secure.answer"): string | null {
  if (payload.length === 0 || payload.length > maximumInnerMessageBytes) return null;
  let text: string;
  let value: unknown;
  try {
    text = new TextDecoder("utf-8", { fatal: true }).decode(payload);
    value = JSON.parse(text);
  } catch { return null; }
  if (!isRecord(value) || value.type !== expectedType || typeof value.sdp !== "string" ||
      Object.keys(value).length !== 2 || value.sdp.trim().length === 0 ||
      new TextEncoder().encode(value.sdp).length > 32 * 1024) return null;
  return text;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
