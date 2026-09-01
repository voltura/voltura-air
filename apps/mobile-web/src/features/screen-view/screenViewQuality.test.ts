import { afterEach, describe, expect, it, vi } from "vitest";
import { screenViewQualityFromStats, startScreenViewQualityMonitor } from "./screenViewQuality";

afterEach(() => {
  vi.useRealTimers();
});

describe("screenViewQualityFromStats", () => {
  it("reports exact inbound dimensions, frame rate, and interval bitrate", () => {
    const report = new Map([
      [
        "video",
        {
          id: "video",
          timestamp: 2_000,
          type: "inbound-rtp",
          kind: "video",
          frameWidth: 3840,
          frameHeight: 2160,
          framesPerSecond: 59.94,
          bytesReceived: 5_000_000,
          framesDecoded: 120,
          framesDropped: 2,
          freezeCount: 1,
          packetsLost: 3,
        },
      ],
    ]) as unknown as RTCStatsReport;

    const result = screenViewQualityFromStats(
      report,
      null,
      {
        bytesReceived: 1_000_000,
        sampledAt: 1_000,
        framesDecoded: 0,
        framesDropped: 0,
        freezeCount: 0,
        packetsLost: 0,
      },
      2_000,
    );

    expect(result?.text).toBe("3840×2160 · 59.9 fps · 32.00 Mbps");
    expect(result?.feedback).toEqual({
      width: 3840,
      height: 2160,
      framesPerSecond: 59.94,
      framesDecoded: 120,
      framesDropped: 2,
      freezeCount: 1,
      packetsLost: 3,
    });
  });

  it("ignores reports without an inbound video stream", () => {
    const report = new Map([
      ["audio", { id: "audio", timestamp: 0, type: "inbound-rtp", kind: "audio" }],
    ]) as unknown as RTCStatsReport;

    expect(screenViewQualityFromStats(report, null, null, 1_000)).toBeNull();
  });

  it("uses only video counters when the peer also carries system audio", () => {
    const report = new Map([
      [
        "video",
        {
          id: "video",
          type: "inbound-rtp",
          kind: "video",
          frameWidth: 1920,
          frameHeight: 1080,
          framesPerSecond: 30,
          bytesReceived: 2_000_000,
          framesDecoded: 60,
          framesDropped: 1,
          freezeCount: 0,
          packetsLost: 2,
        },
      ],
      [
        "audio",
        {
          id: "audio",
          type: "inbound-rtp",
          kind: "audio",
          bytesReceived: 99_000_000,
          packetsLost: 50_000,
        },
      ],
    ]) as unknown as RTCStatsReport;

    const result = screenViewQualityFromStats(
      report,
      null,
      {
        bytesReceived: 1_000_000,
        sampledAt: 1_000,
        framesDecoded: 30,
        framesDropped: 0,
        freezeCount: 0,
        packetsLost: 1,
      },
      2_000,
    );

    expect(result?.text).toBe("1920×1080 · 30.0 fps · 8.00 Mbps");
    expect(result?.feedback?.packetsLost).toBe(1);
  });
});

describe("startScreenViewQualityMonitor", () => {
  it("keeps at most one browser statistics request in flight", async () => {
    vi.useFakeTimers();
    let resolveFirst: ((report: RTCStatsReport) => void) | undefined;
    const getStats = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<RTCStatsReport>((resolve) => {
            resolveFirst = resolve;
          }),
      )
      .mockResolvedValue(new Map() as unknown as RTCStatsReport);
    const onReport = vi.fn();
    const stop = startScreenViewQualityMonitor(
      { getStats } as Pick<RTCPeerConnection, "getStats">,
      onReport,
    );

    await vi.advanceTimersByTimeAsync(5_000);
    expect(getStats).toHaveBeenCalledTimes(1);
    resolveFirst?.(new Map() as unknown as RTCStatsReport);
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(1_999);
    expect(getStats).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(getStats).toHaveBeenCalledTimes(2);
    stop();
  });

  it("does not publish or reschedule an in-flight sample after cleanup", async () => {
    vi.useFakeTimers();
    let resolve: ((report: RTCStatsReport) => void) | undefined;
    const getStats = vi.fn(
      () =>
        new Promise<RTCStatsReport>((complete) => {
          resolve = complete;
        }),
    );
    const onReport = vi.fn();
    const stop = startScreenViewQualityMonitor(
      { getStats } as Pick<RTCPeerConnection, "getStats">,
      onReport,
    );

    await vi.advanceTimersByTimeAsync(2_000);
    stop();
    resolve?.(new Map() as unknown as RTCStatsReport);
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(5_000);

    expect(onReport).not.toHaveBeenCalled();
    expect(getStats).toHaveBeenCalledTimes(1);
  });
});
