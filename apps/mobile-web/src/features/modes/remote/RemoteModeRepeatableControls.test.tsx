import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../../../remoteSettings";
import { renderRemote } from "./remoteModeTestUtils";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;
describe("RemoteMode repeatable controls", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it.each([
    ["Seek backward", "ArrowLeft"],
    ["Seek forward", "ArrowRight"],
    ["D-pad up", "ArrowUp"],
    ["D-pad left", "ArrowLeft"],
    ["D-pad right", "ArrowRight"],
    ["D-pad down", "ArrowDown"]
  ] as const)("repeats %s until release", (buttonName, key) => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    const button = screen.getByRole("button", { name: buttonName });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, key);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, key);
    expect(sendSpecial).toHaveBeenNthCalledWith(3, key);
  });

  it("repeats volume key presses until release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    const button = screen.getByRole("button", { name: "Volume up" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "VolumeUp");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "VolumeUp");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "VolumeUp");
  });

  it("repeats YouTube volume shortcuts until release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "youtube" }, sendSpecial });

    const button = screen.getByRole("button", { name: "Volume down" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "ArrowDown");
  });
});
