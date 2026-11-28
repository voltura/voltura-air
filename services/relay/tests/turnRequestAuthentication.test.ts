import { describe, expect, it } from "vitest";
import {
  isTurnRequestTimestampFresh,
  maximumTurnRequestClockSkewMs,
  readTurnRequestJson,
  turnRequestNonceRetentionMs,
} from "../src/core/turnRequestAuthentication";
import { maximumControlMessageBytes } from "../src/core/constants";

describe("TURN request authentication lifetime", () => {
  const now = 1_800_000_000_000;

  it("accepts managed-PC clock drift through five minutes", () => {
    expect(isTurnRequestTimestampFresh(String(now - maximumTurnRequestClockSkewMs), now)).toBe(
      true,
    );
    expect(isTurnRequestTimestampFresh(String(now + maximumTurnRequestClockSkewMs), now)).toBe(
      true,
    );
  });

  it("rejects timestamps outside the bounded clock-skew window", () => {
    expect(isTurnRequestTimestampFresh(String(now - maximumTurnRequestClockSkewMs - 1), now)).toBe(
      false,
    );
    expect(isTurnRequestTimestampFresh("not-a-timestamp", now)).toBe(false);
  });

  it("retains nonces beyond the full future-timestamp validity window", () => {
    expect(turnRequestNonceRetentionMs).toBeGreaterThan(2 * maximumTurnRequestClockSkewMs);
  });
});

describe("TURN JSON body consumption", () => {
  it("decodes a request split across chunks at the body limit and releases the reader", async () => {
    const payload = { timestamp: "1800000000000" };
    const bytes = new TextEncoder().encode(
      JSON.stringify(payload).padEnd(maximumControlMessageBytes, " "),
    );
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes.subarray(0, 10));
        controller.enqueue(bytes.subarray(10));
        controller.close();
      },
    });

    await expect(readTurnRequestJson(stream)).resolves.toEqual(payload);
    expect(stream.locked).toBe(false);
  });

  it("releases the reader before cancellation completes once the body limit is exceeded", async () => {
    let cancelled = false;
    const { promise: cancellation, resolve: finishCancellation } = Promise.withResolvers<void>();
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array(maximumControlMessageBytes));
        controller.enqueue(new Uint8Array(1));
      },
      cancel() {
        cancelled = true;
        return cancellation;
      },
    });

    try {
      await expect(readTurnRequestJson(stream)).rejects.toThrow(RangeError);
      expect(cancelled).toBe(true);
      expect(stream.locked).toBe(false);
    } finally {
      finishCancellation();
    }
  });
});
