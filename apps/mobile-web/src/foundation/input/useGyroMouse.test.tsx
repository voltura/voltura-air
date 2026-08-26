import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useGyroMouse } from "./useGyroMouse";

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
});

describe("useGyroMouse lifecycle", () => {
  it("does not turn Gyro off for a benign mobile window blur", async () => {
    let resolvePermission: ((granted: boolean) => void) | null = null;
    const permission = new Promise<boolean>((resolve) => {
      resolvePermission = resolve;
    });
    const view = renderHook(() =>
      useGyroMouse({
        activationRequest: { id: 6, permission },
        connected: true,
        enabledSurface: true,
        onMove: vi.fn(),
        onSelectedChange: vi.fn(),
        onStop: vi.fn(),
        sensitivity: 100,
        sessionKey: 1,
      }),
    );

    await act(async () => {
      resolvePermission?.(true);
      await permission;
    });
    await waitFor(() => {
      expect(view.result.current.selected).toBe(true);
    });

    act(() => {
      window.dispatchEvent(new Event("blur"));
    });
    expect(view.result.current.selected).toBe(true);
  });

  it("does not reactivate after a pending permission request is cancelled", async () => {
    let resolvePermission: ((granted: boolean) => void) | null = null;
    const permission = new Promise<boolean>((resolve) => {
      resolvePermission = resolve;
    });
    const view = renderHook(() =>
      useGyroMouse({
        activationRequest: { id: 7, permission },
        connected: true,
        enabledSurface: true,
        onMove: vi.fn(),
        onSelectedChange: vi.fn(),
        onStop: vi.fn(),
        sensitivity: 100,
        sessionKey: 1,
      }),
    );

    act(() => {
      view.result.current.setSelected(false);
    });
    await act(async () => {
      resolvePermission?.(true);
      await permission;
    });

    expect(view.result.current.selected).toBe(false);
  });

  it("moves only while engaged and cleans listeners and wake lock when its surface closes", async () => {
    const release = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, "wakeLock", {
      configurable: true,
      value: { request: vi.fn(() => Promise.resolve({ release })) },
    });
    const removeWindowListener = vi.spyOn(window, "removeEventListener");
    const onMove = vi.fn();
    const onSelectedChange = vi.fn();
    const onStop = vi.fn();
    const activationRequest = { id: 1, permission: Promise.resolve(true) };
    const view = renderHook(
      ({ enabledSurface }) =>
        useGyroMouse({
          activationRequest,
          connected: true,
          enabledSurface,
          onMove,
          onSelectedChange,
          onStop,
          sensitivity: 100,
          sessionKey: 1,
        }),
      { initialProps: { enabledSurface: true } },
    );
    await waitFor(() => {
      expect(view.result.current.selected).toBe(true);
    });

    act(() => {
      window.dispatchEvent(sensorEvent("devicemotion", 0));
      window.dispatchEvent(sensorEvent("devicemotion", 20));
    });
    expect(onMove).not.toHaveBeenCalled();

    act(() => {
      view.result.current.setEngaged(true);
      window.dispatchEvent(sensorEvent("devicemotion", 40));
      window.dispatchEvent(sensorEvent("devicemotion", 60));
    });
    expect(onMove).toHaveBeenCalled();

    view.rerender({ enabledSurface: false });
    await waitFor(() => {
      expect(view.result.current.selected).toBe(false);
    });
    expect(removeWindowListener.mock.calls.some(([type]) => type === "devicemotion")).toBe(true);
    await waitFor(() => {
      expect(release).toHaveBeenCalled();
    });
    expect(onSelectedChange).toHaveBeenLastCalledWith(false);
  });

  it("turns off on visibility loss and connection-session replacement", async () => {
    const onSelectedChange = vi.fn();
    const onStop = vi.fn();
    const activationRequest = { id: 2, permission: Promise.resolve(true) };
    const view = renderHook(
      ({ sessionKey }) =>
        useGyroMouse({
          activationRequest,
          connected: true,
          enabledSurface: true,
          onMove: vi.fn(),
          onSelectedChange,
          onStop,
          sensitivity: 100,
          sessionKey,
        }),
      { initialProps: { sessionKey: 1 } },
    );
    await waitFor(() => {
      expect(view.result.current.selected).toBe(true);
    });

    view.rerender({ sessionKey: 2 });
    await waitFor(() => {
      expect(view.result.current.selected).toBe(false);
    });

    act(() => {
      view.result.current.enableFromUserGesture();
    });
    await waitFor(() => {
      expect(view.result.current.selected).toBe(true);
    });
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });
    expect(view.result.current.selected).toBe(false);
  });
});

function sensorEvent(type: string, timeStamp: number): Event {
  const event = new Event(type);
  Object.defineProperties(event, {
    accelerationIncludingGravity: { value: { x: 0, y: 0, z: 9.8 } },
    rotationRate: { value: { alpha: 0, beta: 0, gamma: 30 } },
    timeStamp: { value: timeStamp },
  });
  return event;
}
