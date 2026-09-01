import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ConnectionState } from "../connection/connectionTypes";
import { defaultTrackpadSettings } from "./gestures";
import { usePointerInput } from "./usePointerInput";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("usePointerInput", () => {
  it("cancels queued pointer input when the owning surface becomes unavailable", () => {
    const send = vi.fn();
    let frame: FrameRequestCallback | undefined;
    const cancelAnimationFrame = vi.fn();
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        frame = callback;
        return 1;
      }),
    );
    vi.stubGlobal("cancelAnimationFrame", cancelAnimationFrame);
    const { result } = renderHook(() =>
      usePointerInput({
        send,
        state: "paired",
        trackpadSettings: defaultTrackpadSettings,
      }),
    );

    act(() => {
      result.current.emit({ type: "pointer.move", dx: 4, dy: 2 });
    });
    act(() => {
      result.current.cancel();
    });
    act(() => {
      frame?.(0);
    });

    expect(cancelAnimationFrame).toHaveBeenCalledWith(1);
    expect(send).not.toHaveBeenCalled();
  });

  it("does not flush a queued pointer delta after the connection leaves paired", () => {
    const send = vi.fn();
    let frame: FrameRequestCallback | undefined;
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        frame = callback;
        return 1;
      }),
    );
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    const { result, rerender } = renderHook(
      ({ state }: { state: ConnectionState }) =>
        usePointerInput({ send, state, trackpadSettings: defaultTrackpadSettings }),
      {
        initialProps: { state: "paired" as ConnectionState },
      },
    );

    act(() => {
      result.current.emit({ type: "pointer.move", dx: 4, dy: 2 });
    });
    rerender({ state: "connecting" });
    act(() => {
      frame?.(0);
    });

    expect(send).not.toHaveBeenCalled();
  });

  it("keeps trackpad and Gyro movement in separate contextual batches", () => {
    const send = vi.fn();
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        frames.push(callback);
        return frames.length;
      }),
    );
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    const { result } = renderHook(() =>
      usePointerInput({
        send,
        state: "paired",
        trackpadSettings: defaultTrackpadSettings,
        inputContext: "trackpad",
      }),
    );

    act(() => {
      result.current.emit({ type: "pointer.move", dx: 2, dy: 3 });
      result.current.emit({ type: "pointer.move", inputContext: "gyro-mouse", dx: 4, dy: 5 });
    });
    act(() => {
      frames.at(-1)?.(0);
    });

    expect(send.mock.calls).toEqual([
      [{ type: "pointer.move", inputContext: "trackpad", dx: 2, dy: 3 }],
      [{ type: "pointer.move", inputContext: "gyro-mouse", dx: 4, dy: 5 }],
    ]);
  });

  it("can force the existing two-finger gesture to scroll without an end action", () => {
    const send = vi.fn();
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        frames.push(callback);
        return frames.length;
      }),
    );
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    const { result } = renderHook(() =>
      usePointerInput({
        send,
        state: "paired",
        trackpadSettings: { ...defaultTrackpadSettings, zoomGestures: true },
        twoFingerMode: "zoom",
        inputContext: "trackpad",
      }),
    );
    const touchEvent = (
      targetTouches: { identifier: number; clientX: number; clientY: number }[],
      timeStamp: number,
    ) =>
      ({
        preventDefault: vi.fn(),
        targetTouches,
        timeStamp,
      }) as never;
    const start = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 140, clientY: 100 },
    ];

    act(() => {
      result.current.onTouchStart(touchEvent(start, 0));
      result.current.onTouchEnd(touchEvent([], 100), false);
    });
    expect(send).not.toHaveBeenCalled();

    act(() => {
      result.current.onTouchStart(touchEvent(start, 200));
      result.current.onTouchMove(
        touchEvent(
          start.map((touch) => ({ ...touch, clientY: touch.clientY + 20 })),
          220,
        ),
        "scroll",
      );
    });
    act(() => {
      frames.at(-1)?.(0);
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "pointer.wheel",
      inputContext: "trackpad",
      dx: 0,
      dy: -22,
    });
    act(() => {
      result.current.onTouchEnd(touchEvent([], 240), false);
    });
  });

  it("lets a functional owner override the context of a special key", () => {
    const send = vi.fn();
    const { result } = renderHook(() =>
      usePointerInput({
        send,
        state: "paired",
        trackpadSettings: defaultTrackpadSettings,
        inputContext: "keyboard",
      }),
    );

    act(() => {
      result.current.sendSpecial("MediaPlayPause", undefined, "media-controls");
    });

    expect(send).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        type: "keyboard.special",
        key: "MediaPlayPause",
        inputContext: "media-controls",
      }),
    );
  });
});
