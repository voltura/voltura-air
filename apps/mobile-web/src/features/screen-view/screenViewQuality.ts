export interface ScreenViewQualitySample {
  streamId: string;
  sampledAt: number;
  bytesReceived?: number | undefined;
  framesDecoded?: number | undefined;
  framesDropped?: number | undefined;
  freezeCount?: number | undefined;
  packetsLost?: number | undefined;
}

export interface ScreenViewQualityResult {
  text: string;
  sample: ScreenViewQualitySample;
  feedback?: {
    width: number;
    height: number;
    framesPerSecond: number;
    framesDecoded: number;
    framesDropped: number;
    freezeCount: number;
    packetsLost: number;
  };
}

export function startScreenViewQualityMonitor(
  peer: Pick<
    RTCPeerConnection,
    "connectionState" | "getStats" | "addEventListener" | "removeEventListener"
  >,
  onReport: (report: RTCStatsReport) => void,
  intervalMilliseconds = 2_000,
): () => void {
  let canceled = false;
  let sampling = false;
  let generation = 0;
  let timeout: number | undefined;
  const schedule = () => {
    if (canceled || sampling || timeout !== undefined || peer.connectionState !== "connected") {
      return;
    }
    timeout = window.setTimeout(async () => {
      timeout = undefined;
      sampling = true;
      const epoch = generation;
      try {
        const report = await peer.getStats();
        if (!canceled && generation === epoch && peer.connectionState === "connected") {
          onReport(report);
        }
      } catch {
        // Browser statistics are optional display information.
      } finally {
        sampling = false;
        schedule();
      }
    }, intervalMilliseconds);
  };
  const onConnectionStateChange = () => {
    generation += 1;
    window.clearTimeout(timeout);
    timeout = undefined;
    schedule();
  };
  peer.addEventListener("connectionstatechange", onConnectionStateChange);
  schedule();
  return () => {
    canceled = true;
    peer.removeEventListener("connectionstatechange", onConnectionStateChange);
    window.clearTimeout(timeout);
  };
}

export function screenViewQualityFromStats(
  report: RTCStatsReport,
  video: HTMLVideoElement | null,
  previous: ScreenViewQualitySample | null,
  sampledAt: number,
): ScreenViewQualityResult | null {
  let inbound:
    | (RTCStats & {
        kind?: string;
        mediaType?: string;
        frameWidth?: number;
        frameHeight?: number;
        framesPerSecond?: number;
        bytesReceived?: number;
        framesDecoded?: number;
        framesDropped?: number;
        freezeCount?: number;
        packetsLost?: number;
      })
    | undefined;
  report.forEach((candidate) => {
    const stat = candidate as typeof inbound;
    if (stat?.type === "inbound-rtp" && (stat.kind === "video" || stat.mediaType === "video")) {
      inbound = stat;
    }
  });
  if (!inbound) {
    return null;
  }
  const width = nonNegativeStat(inbound.frameWidth) ?? video?.videoWidth ?? 0;
  const height = nonNegativeStat(inbound.frameHeight) ?? video?.videoHeight ?? 0;
  const fps = nonNegativeStat(inbound.framesPerSecond);
  const bytesReceived = nonNegativeStat(inbound.bytesReceived);
  const framesDecoded = nonNegativeStat(inbound.framesDecoded);
  const framesDropped = nonNegativeStat(inbound.framesDropped);
  const freezeCount = nonNegativeStat(inbound.freezeCount);
  const packetsLost = Number.isFinite(inbound.packetsLost) ? inbound.packetsLost : undefined;
  const baseline =
    previous?.streamId === inbound.id && sampledAt > previous.sampledAt ? previous : null;
  const bitrateMbps =
    baseline?.bytesReceived !== undefined &&
    bytesReceived !== undefined &&
    bytesReceived >= baseline.bytesReceived
      ? ((bytesReceived - baseline.bytesReceived) * 8) / (sampledAt - baseline.sampledAt) / 1000
      : undefined;
  const parts = [width > 0 && height > 0 ? `${width}×${height}` : ""];
  if (fps !== undefined) {
    parts.push(`${fps.toFixed(1)} fps`);
  }
  if (bitrateMbps !== undefined) {
    parts.push(`${bitrateMbps.toFixed(2)} Mbps`);
  }
  const text = parts.filter(Boolean).join(" · ");
  if (!text) {
    return null;
  }
  const sample: ScreenViewQualitySample = {
    streamId: inbound.id,
    sampledAt,
    bytesReceived,
    framesDecoded,
    framesDropped,
    freezeCount,
    packetsLost,
  };
  const feedback = baseline
    ? {
        width: Math.min(16_384, Math.trunc(width)),
        height: Math.min(16_384, Math.trunc(height)),
        framesPerSecond: Math.min(240, fps ?? 0),
        framesDecoded: intervalDelta(framesDecoded, baseline.framesDecoded),
        framesDropped: intervalDelta(framesDropped, baseline.framesDropped),
        freezeCount: intervalDelta(freezeCount, baseline.freezeCount),
        packetsLost: intervalDelta(packetsLost, baseline.packetsLost),
      }
    : undefined;
  return feedback ? { text, sample, feedback } : { text, sample };
}

function nonNegativeStat(value: number | undefined): number | undefined {
  return value !== undefined && Number.isFinite(value) && value >= 0 ? value : undefined;
}

function intervalDelta(current: number | undefined, previous: number | undefined): number {
  return current === undefined || previous === undefined
    ? 0
    : Math.max(0, Math.min(1_000_000, Math.trunc(current - previous)));
}
