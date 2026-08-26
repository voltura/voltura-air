export interface ScreenViewQualitySample {
  bytesReceived: number;
  sampledAt: number;
  framesDecoded: number;
  framesDropped: number;
  freezeCount: number;
  packetsLost: number;
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
  peer: Pick<RTCPeerConnection, "getStats">,
  onReport: (report: RTCStatsReport) => void,
  intervalMilliseconds = 2_000,
): () => void {
  let canceled = false;
  let timeout: number | undefined;
  const schedule = () => {
    timeout = window.setTimeout(async () => {
      timeout = undefined;
      try {
        const report = await peer.getStats();
        if (!canceled) {
          onReport(report);
        }
      } catch {
        // Browser statistics are optional display information.
      } finally {
        if (!canceled) {
          schedule();
        }
      }
    }, intervalMilliseconds);
  };
  schedule();
  return () => {
    canceled = true;
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
  const width = inbound.frameWidth ?? video?.videoWidth ?? 0;
  const height = inbound.frameHeight ?? video?.videoHeight ?? 0;
  const fps = inbound.framesPerSecond;
  const bytesReceived = inbound.bytesReceived ?? previous?.bytesReceived ?? 0;
  const framesDecoded = inbound.framesDecoded ?? previous?.framesDecoded ?? 0;
  const framesDropped = inbound.framesDropped ?? previous?.framesDropped ?? 0;
  const freezeCount = inbound.freezeCount ?? previous?.freezeCount ?? 0;
  const packetsLost = inbound.packetsLost ?? previous?.packetsLost ?? 0;
  const elapsed = previous ? (sampledAt - previous.sampledAt) / 1000 : 0;
  const bitrateMbps =
    previous && elapsed > 0 && bytesReceived >= previous.bytesReceived
      ? ((bytesReceived - previous.bytesReceived) * 8) / elapsed / 1_000_000
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
  const sample = {
    bytesReceived,
    sampledAt,
    framesDecoded,
    framesDropped,
    freezeCount,
    packetsLost,
  };
  const delta = (current: number, prior: number) =>
    Math.max(0, Math.min(1_000_000, Math.trunc(current - prior)));
  const feedback = previous
    ? {
        width,
        height,
        framesPerSecond: Math.max(0, Math.min(240, fps ?? 0)),
        framesDecoded: delta(framesDecoded, previous.framesDecoded),
        framesDropped: delta(framesDropped, previous.framesDropped),
        freezeCount: delta(freezeCount, previous.freezeCount),
        packetsLost: delta(packetsLost, previous.packetsLost),
      }
    : undefined;
  return feedback ? { text, sample, feedback } : { text, sample };
}
