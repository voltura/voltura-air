import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useScreenWakeLock } from "./useScreenWakeLock";

function lock() {
  return { release: vi.fn(() => Promise.resolve()) };
}

function setVisibility(value: DocumentVisibilityState) {
  Object.defineProperty(document, "visibilityState", { configurable: true, value });
  act(() => {
    document.dispatchEvent(new Event("visibilitychange"));
  });
}

beforeEach(() => setVisibility("visible"));
afterEach(() => vi.unstubAllGlobals());

describe("screen wake lock ownership", () => {
  it("acquires only when enabled and visible, and releases on disable and unmount", async () => {
    const first = lock();
    const second = lock();
    const request = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second);
    vi.stubGlobal("navigator", { wakeLock: { request } });
    const view = renderHook(({ enabled }) => useScreenWakeLock(enabled), {
      initialProps: { enabled: false },
    });
    expect(request).not.toHaveBeenCalled();
    await act(async () => {
      view.rerender({ enabled: true });
      await Promise.resolve();
    });
    expect(request).toHaveBeenCalledExactlyOnceWith("screen");
    view.rerender({ enabled: false });
    expect(first.release).toHaveBeenCalledOnce();
    await act(async () => {
      view.rerender({ enabled: true });
      await Promise.resolve();
    });
    view.unmount();
    expect(second.release).toHaveBeenCalledOnce();
  });

  it("reacquires once after returning and cleans up page lifecycle listeners", async () => {
    const first = lock();
    const second = lock();
    const request = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second);
    vi.stubGlobal("navigator", { wakeLock: { request } });
    setVisibility("hidden");
    const view = renderHook(() => useScreenWakeLock(true));
    expect(request).not.toHaveBeenCalled();
    await act(async () => {
      setVisibility("visible");
      await Promise.resolve();
    });
    act(() => {
      window.dispatchEvent(new Event("pagehide"));
    });
    expect(first.release).toHaveBeenCalledOnce();
    await act(async () => {
      window.dispatchEvent(new Event("pageshow"));
      await Promise.resolve();
    });
    setVisibility("visible");
    expect(request).toHaveBeenCalledTimes(2);
    setVisibility("hidden");
    expect(second.release).toHaveBeenCalledOnce();
    view.unmount();
    setVisibility("visible");
    act(() => {
      window.dispatchEvent(new Event("pageshow"));
    });
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("releases a late lock without touching the replacement after hide and return", async () => {
    let resolve!: (value: ReturnType<typeof lock>) => void;
    const late = lock();
    const current = lock();
    const request = vi
      .fn()
      .mockReturnValueOnce(
        new Promise((done) => {
          resolve = done;
        }),
      )
      .mockResolvedValueOnce(current);
    vi.stubGlobal("navigator", { wakeLock: { request } });
    const view = renderHook(() => useScreenWakeLock(true));
    setVisibility("hidden");
    await act(async () => {
      setVisibility("visible");
      await Promise.resolve();
    });
    await act(async () => {
      resolve(late);
      await Promise.resolve();
    });
    expect(late.release).toHaveBeenCalledOnce();
    expect(current.release).not.toHaveBeenCalled();
    view.unmount();
    expect(current.release).toHaveBeenCalledOnce();
  });

  it("releases a request completing after unmount", async () => {
    let resolve!: (value: ReturnType<typeof lock>) => void;
    const late = lock();
    vi.stubGlobal("navigator", {
      wakeLock: {
        request: () =>
          new Promise((done) => {
            resolve = done;
          }),
      },
    });
    const view = renderHook(() => useScreenWakeLock(true));
    view.unmount();
    await act(async () => {
      resolve(late);
      await Promise.resolve();
    });
    expect(late.release).toHaveBeenCalledOnce();
  });

  it("tolerates unavailable APIs, synchronous throws, and denied requests without looping", async () => {
    vi.stubGlobal("navigator", {});
    const unavailable = renderHook(() => useScreenWakeLock(true));
    unavailable.unmount();
    const request = vi
      .fn()
      .mockImplementationOnce(() => {
        throw new Error("denied");
      })
      .mockRejectedValue(new Error("denied"));
    vi.stubGlobal("navigator", { wakeLock: { request } });
    const view = renderHook(() => useScreenWakeLock(true));
    await act(async () => {
      setVisibility("visible");
      await Promise.resolve();
    });
    view.rerender();
    expect(request).toHaveBeenCalledOnce();
    setVisibility("hidden");
    await act(async () => {
      setVisibility("visible");
      await Promise.resolve();
    });
    expect(request).toHaveBeenCalledTimes(2);
    view.unmount();
  });

  it("does not reacquire on a browser release or release another consumer's sentinel", async () => {
    const appLock = Object.assign(new EventTarget(), lock());
    const gyroLock = lock();
    appLock.release.mockRejectedValue(new Error("already released"));
    const request = vi.fn().mockResolvedValueOnce(appLock).mockResolvedValueOnce(gyroLock);
    vi.stubGlobal("navigator", { wakeLock: { request } });
    const app = renderHook(() => useScreenWakeLock(true));
    const gyro = renderHook(() => useScreenWakeLock(true));
    await act(() => Promise.resolve());
    act(() => {
      appLock.dispatchEvent(new Event("release"));
    });
    gyro.unmount();
    expect(gyroLock.release).toHaveBeenCalledOnce();
    expect(appLock.release).not.toHaveBeenCalled();
    setVisibility("visible");
    expect(request).toHaveBeenCalledTimes(2);
    await act(async () => {
      app.unmount();
      await Promise.resolve();
    });
  });
});
