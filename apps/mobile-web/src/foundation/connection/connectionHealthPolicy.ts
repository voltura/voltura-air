const interactiveHealthCheckMs = 10000;
const passiveHealthCheckMs = 60000;
const passiveAfterMs = 15000;
const inputAckTimeoutMs = 3500;

export const staleConnectionMs = passiveHealthCheckMs + 6500 + 5000;

export function hasExpiredInputAck(
  pendingAcks: Iterable<number>,
  supportsInputAck: boolean,
  now = Date.now()
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
  now = Date.now()
) {
  const isInteractive = pendingAckCount > 0 || now - lastUserActivityAt < passiveAfterMs;
  const interval = isInteractive ? interactiveHealthCheckMs : passiveHealthCheckMs;
  return Math.max(1000, (lastHealthyAt || now) + interval - now);
}
