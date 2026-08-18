import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../../../foundation/settings/remoteSettings";
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
    fireEvent.click(button, { detail: 1 });

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
    fireEvent.click(button, { detail: 1 });

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
    fireEvent.click(button, { detail: 1 });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "ArrowDown");
  });

  it("stops repeating on focus loss and does not swallow the next click", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Seek forward" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    act(() => {vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);});
    fireEvent.blur(window);
    const countAfterBlur = sendSpecial.mock.calls.length;
    act(() => {vi.advanceTimersByTime(repeatIntervalMs * 4);});
    expect(sendSpecial).toHaveBeenCalledTimes(countAfterBlur);
    fireEvent.click(button);
    expect(sendSpecial).toHaveBeenCalledTimes(countAfterBlur + 1);
  });

  it.each(["pointerCancel", "pointerLeave", "lostPointerCapture"] as const)(
    "does not duplicate a pointer press after %s",
    (boundary) => {
      const sendSpecial = vi.fn();
      renderRemote({ sendSpecial });
      const button = screen.getByRole("button", { name: "D-pad up" });

      fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
      fireEvent[boundary](button, { pointerId: 1 });
      fireEvent.click(button, { detail: 1 });

      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("ArrowUp");
    }
  );

  it("sends an ordinary Remote button once for a complete pointer sequence", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Play or pause" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button, { detail: 1 });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("MediaPlayPause");
  });
});
