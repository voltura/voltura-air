import { maximumControlMessageBytes } from "./constants";

export const maximumTurnRequestClockSkewMs = 5 * 60_000;

// A request timestamp may be up to one skew window in the future and remains
// valid until one skew window after that timestamp.
export const turnRequestNonceRetentionMs = 2 * maximumTurnRequestClockSkewMs + 60_000;

export function isTurnRequestTimestampFresh(timestamp: string, now = Date.now()): boolean {
  return (
    /^\d{13}$/u.test(timestamp) &&
    Math.abs(now - Number(timestamp)) <= maximumTurnRequestClockSkewMs
  );
}
export async function readTurnRequestJson(
  body: ReadableStream<Uint8Array> | null,
): Promise<unknown> {
  if (!body) throw new TypeError("TURN request body is missing.");
  const reader = body.getReader();
  const buffer = new Uint8Array(maximumControlMessageBytes);
  let length = 0;
  try {
    for (;;) {
      const chunk = await reader.read();
      if (chunk.done) break;
      if (length + chunk.value.byteLength > buffer.byteLength) {
        void reader.cancel().catch(() => undefined);
        throw new RangeError("TURN request body is too large.");
      }
      buffer.set(chunk.value, length);
      length += chunk.value.byteLength;
    }
    return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buffer.subarray(0, length)));
  } finally {
    reader.releaseLock();
  }
}
