import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createSpeechSession,
  getActiveSpeechSession,
  getSpeechDiagnostics,
} from "./speechRecognitionSession";

function recognition() {
  const api: Parameters<typeof createSpeechSession>[0] = {
    continuous: true,
    interimResults: true,
    onstart: null,
    onaudiostart: null,
    onaudioend: null,
    onspeechstart: null,
    onresult: null,
    onend: null,
    onerror: vi.fn(),
    start: vi.fn(),
    stop: vi.fn(),
    abort: vi.fn(),
  };
  return api;
}

afterEach(() => {
  getActiveSpeechSession()?.finish(true);
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("speech session completion", () => {
  it("starts recognition directly without the unsuccessful extra microphone stream", () => {
    const getUserMedia = vi.fn();
    vi.stubGlobal("navigator", { mediaDevices: { getUserMedia } });
    const api = recognition();
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.start();
    expect(api.start).toHaveBeenCalledOnce();
    expect(getUserMedia).not.toHaveBeenCalled();
    expect(getSpeechDiagnostics().lifecycleVersion).toBe(4);
  });

  it("requests normal completion once and waits for end without aborting", async () => {
    vi.useFakeTimers();
    const api = recognition();
    const onFinish = vi.fn();
    const session = createSpeechSession(api, onFinish);
    session.start();
    session.stop();
    session.stop();
    expect(api.stop).toHaveBeenCalledOnce();
    expect(api.abort).not.toHaveBeenCalled();
    expect(onFinish).not.toHaveBeenCalled();
    api.onend?.();
    await expect(session.ended).resolves.toBe(true);
    expect(onFinish).toHaveBeenCalledOnce();
    expect(vi.getTimerCount()).toBe(0);
    expect(api.onresult).toBeNull();
    expect(api.onerror).toBeNull();
    expect(getSpeechDiagnostics().events).toContainEqual(
      expect.objectContaining({ event: "stop-requested" }),
    );
  });

  it("escalates a stalled stop once, and still waits for end before release", async () => {
    vi.useFakeTimers();
    const api = recognition();
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.stop();
    await vi.advanceTimersByTimeAsync(1999);
    expect(api.abort).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(api.abort).toHaveBeenCalledOnce();
    expect(getActiveSpeechSession()).toBe(session);
    session.stop();
    session.abort();
    expect(api.abort).toHaveBeenCalledOnce();
    api.onend?.();
    await expect(session.ended).resolves.toBe(true);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("fails a shutdown after both stop and abort omit end", async () => {
    vi.useFakeTimers();
    const api = recognition();
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.stop();
    await vi.advanceTimersByTimeAsync(4000);
    await expect(session.ended).resolves.toBe(false);
    expect(api.stop).toHaveBeenCalledOnce();
    expect(api.abort).toHaveBeenCalledOnce();
    expect(getActiveSpeechSession()).toBeNull();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("allows an error or hidden page to cancel an already-stopping session immediately", async () => {
    vi.useFakeTimers();
    const api = recognition();
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.stop();
    await vi.advanceTimersByTimeAsync(100);
    session.abort();
    expect(api.abort).toHaveBeenCalledOnce();
    api.onend?.();
    await expect(session.ended).resolves.toBe(true);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("recovers from a stop exception with abort", async () => {
    const api = recognition();
    api.stop = vi.fn(() => {
      throw new Error("stop failed");
    });
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.stop();
    expect(api.abort).toHaveBeenCalledOnce();
    api.onend?.();
    await expect(session.ended).resolves.toBe(true);
  });

  it("releases the session if both shutdown methods throw", async () => {
    const api = recognition();
    api.stop = vi.fn(() => {
      throw new Error("stop failed");
    });
    api.abort = vi.fn(() => {
      throw new Error("abort failed");
    });
    const session = createSpeechSession(api, vi.fn());
    session.start();
    session.stop();
    await expect(session.ended).resolves.toBe(false);
    expect(getActiveSpeechSession()).toBeNull();
  });
});
