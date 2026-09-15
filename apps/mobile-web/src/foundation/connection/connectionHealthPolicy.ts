import type { PcProfile } from "./pcProfiles";

const interactiveHealthCheckMs = 10000;
const passiveHealthCheckMs = 60000;
const relayPassiveHealthCheckMs = 20000;
const passiveAfterMs = 15000;
const inputAckTimeoutMs = 3500;

export const staleConnectionMs = passiveHealthCheckMs + 6500 + 5000;

export function hasExpiredInputAck(
  pendingAcks: Iterable<number>,
  supportsInputAck: boolean,
  now = Date.now(),
) {
  if (!supportsInputAck) {
    return false;
  }

  for (const sentAt of pendingAcks) {
    if (now - sentAt > inputAckTimeoutMs) {
      return true;
    }
  }

  return false;
}

export function getNextHealthCheckDelay(
  pendingAckCount: number,
  lastUserActivityAt: number,
  lastHealthyAt: number,
  now = Date.now(),
  transportMode?: PcProfile["transportMode"],
) {
  const isInteractive = pendingAckCount > 0 || now - lastUserActivityAt < passiveAfterMs;
  // Relay has a separate phone-to-cloud socket; the host's keepalive does not
  // keep this socket active during a quiet foreground session.
  const passiveInterval =
    transportMode === "relay" ? relayPassiveHealthCheckMs : passiveHealthCheckMs;
  const interval = isInteractive ? interactiveHealthCheckMs : passiveInterval;
  const baseline = isInteractive
    ? Math.max(lastHealthyAt || now, lastUserActivityAt)
    : lastHealthyAt || now;
  return Math.max(1000, baseline + interval - now);
}

export function getNextInputAckCheckDelay(pendingAcks: Iterable<number>, now = Date.now()) {
  let delay = Number.POSITIVE_INFINITY;
  for (const sentAt of pendingAcks) {
    delay = Math.min(delay, Math.max(1, sentAt + inputAckTimeoutMs + 1 - now));
  }
  return delay;
}
