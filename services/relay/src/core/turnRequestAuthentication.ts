export const maximumTurnRequestClockSkewMs = 5 * 60_000;

// A request timestamp may be up to one skew window in the future and remains
// valid until one skew window after that timestamp.
export const turnRequestNonceRetentionMs = (2 * maximumTurnRequestClockSkewMs) + 60_000;

export function isTurnRequestTimestampFresh(timestamp: string, now = Date.now()): boolean {
  return /^\d{13}$/u.test(timestamp) && Math.abs(now - Number(timestamp)) <= maximumTurnRequestClockSkewMs;
}
