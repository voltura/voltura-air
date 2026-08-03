export type RelayRateRole = "host" | "device";

export interface RelayRateState {
  windowStartedAt: number;
  frames: number;
  bytes: number;
}

export interface RelayRateResult {
  allowed: boolean;
  state: RelayRateState;
}

const limits = {
  host: { frames: 4096, bytes: 32 * 1024 * 1024 },
  device: { frames: 256, bytes: 2 * 1024 * 1024 }
} as const;

export function consumeRelayRate(
  previous: RelayRateState | undefined,
  role: RelayRateRole,
  byteLength: number,
  now = Date.now()
): RelayRateResult {
  const reset = !previous || now < previous.windowStartedAt || now - previous.windowStartedAt >= 1000;
  const state: RelayRateState = reset
    ? { windowStartedAt: now, frames: 1, bytes: byteLength }
    : { windowStartedAt: previous.windowStartedAt, frames: previous.frames + 1, bytes: previous.bytes + byteLength };
  const limit = limits[role];
  return { allowed: state.frames <= limit.frames && state.bytes <= limit.bytes, state };
}
