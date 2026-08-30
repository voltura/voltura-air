import { describe, expect, it, vi } from "vitest";
import { MomentumScroller } from "./momentumScroller";

describe("MomentumScroller", () => {
  it("coasts after a fast flick and stops immediately on the next touch", () => {
    let now = 0;
    let nextFrame = 1;
    const frames = new Map<number, FrameRequestCallback>();
    const scroll = vi.fn();
    const scroller = new MomentumScroller(scroll, {
      now: () => now,
      requestFrame: (callback) => {
        const frame = nextFrame++;
        frames.set(frame, callback);
        return frame;
      },
      cancelFrame: (frame) => {
        frames.delete(frame);
      },
    });

    scroller.begin();
    now = 16;
    scroller.move("vertical", 32);
    scroller.end();
    expect(scroll).toHaveBeenLastCalledWith("vertical", 32);
    expect(frames.size).toBe(1);

    const momentumFrame = [...frames.values()][0]!;
    frames.clear();
    now = 32;
    momentumFrame(now);
    expect(scroll.mock.calls.at(-1)?.[0]).toBe("vertical");
    expect(scroll.mock.calls.at(-1)?.[1]).toBeGreaterThan(0);
    expect(frames.size).toBe(1);

    scroller.begin();
    expect(frames.size).toBe(0);
    const callsAfterTouch = scroll.mock.calls.length;
    now = 48;
    for (const callback of frames.values()) {
      callback(now);
    }
    expect(scroll).toHaveBeenCalledTimes(callsAfterTouch);
  });

  it("does not coast after a slow drag or a delayed release", () => {
    let now = 0;
    const requestFrame = vi.fn(() => 1);
    const scroller = new MomentumScroller(vi.fn(), {
      now: () => now,
      requestFrame,
      cancelFrame: vi.fn(),
    });

    scroller.begin();
    now = 80;
    scroller.move("horizontal", 2);
    scroller.end();
    expect(requestFrame).not.toHaveBeenCalled();

    scroller.begin();
    now = 96;
    scroller.move("vertical", 32);
    now = 220;
    scroller.end();
    expect(requestFrame).not.toHaveBeenCalled();
  });

  it("expires instead of resuming after animation frames were suspended", () => {
    let now = 0;
    let frame: FrameRequestCallback | null = null;
    const scroll = vi.fn();
    const scroller = new MomentumScroller(scroll, {
      now: () => now,
      requestFrame: (callback) => {
        frame = callback;
        return 1;
      },
      cancelFrame: () => {
        frame = null;
      },
    });

    scroller.begin();
    now = 16;
    scroller.move("vertical", 32);
    scroller.end();
    expect(frame).not.toBeNull();
    const callsBeforeSuspension = scroll.mock.calls.length;

    now = 5_000;
    const suspendedFrame = frame as FrameRequestCallback | null;
    frame = null;
    suspendedFrame?.(now);
    expect(scroll).toHaveBeenCalledTimes(callsBeforeSuspension);
    expect(frame).toBeNull();
  });
});
