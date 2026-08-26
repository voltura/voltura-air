import { describe, expect, it } from "vitest";
import {
  isTurnRequestTimestampFresh,
  maximumTurnRequestClockSkewMs,
  turnRequestNonceRetentionMs,
} from "../src/core/turnRequestAuthentication";

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
