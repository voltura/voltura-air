import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../../../foundation/settings/remoteSettings";
import { renderRemote } from "./remoteModeTestUtils";
import { RemoteMode } from "./RemoteMode";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;
describe("RemoteMode repeatable controls", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("keeps a newer held shortcut repeating when the old pointer releases", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const first = screen.getByRole("button", { name: "Volume up" });
    const second = screen.getByRole("button", { name: "Volume down" });
    fireEvent.pointerDown(first, { button: 0, pointerId: 1 });
    fireEvent.pointerDown(second, { button: 0, pointerId: 2 });
    fireEvent.pointerUp(first, { pointerId: 1 });
    act(() => {
      vi.advanceTimersByTime(400);
    });
    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial.mock.lastCall?.[0]).toBe("VolumeDown");
  });

  it("stops a held shortcut when the page leaves", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Volume up" }), {
      button: 0,
      pointerId: 1,
    });
    fireEvent(window, new Event("pagehide"));
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(sendSpecial).toHaveBeenCalledTimes(1);
  });

  it.each(["disconnect", "mode"])("cancels a held shortcut on %s", (boundary) => {
    const sendSpecial = vi.fn();
    const view = renderRemote({ sendSpecial });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Volume up" }), {
      button: 0,
      pointerId: 1,
    });
    view.rerender(
      <RemoteMode
        {...view}
        isConnected={boundary !== "disconnect"}
        remoteSettings={
          boundary === "mode"
            ? { ...defaultRemoteSettings, mode: "youtube" }
            : defaultRemoteSettings
        }
      />,
    );
    const count = sendSpecial.mock.calls.length;
    act(() => {
      vi.advanceTimersByTime(1_000);
    });
    expect(sendSpecial).toHaveBeenCalledTimes(count);
  });

  it.each([
    ["Seek backward", "ArrowLeft", true],
    ["Seek forward", "ArrowRight", true],
    ["D-pad up", "ArrowUp", false],
    ["D-pad left", "ArrowLeft", false],
    ["D-pad right", "ArrowRight", false],
    ["D-pad down", "ArrowDown", false],
  ] as const)("repeats %s until release", (buttonName, key, isMediaControl) => {
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
    const expectedArguments = isMediaControl ? [key, undefined, "media-controls"] : [key];
    expect(sendSpecial).toHaveBeenNthCalledWith(1, ...expectedArguments);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, ...expectedArguments);
    expect(sendSpecial).toHaveBeenNthCalledWith(3, ...expectedArguments);
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
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "VolumeUp", undefined, "media-controls");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "VolumeUp", undefined, "media-controls");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "VolumeUp", undefined, "media-controls");
  });

  it("repeats YouTube volume shortcuts until release", () => {
    const sendSpecial = vi.fn();
    renderRemote({
      remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "youtube" },
      sendSpecial,
    });

    const button = screen.getByRole("button", { name: "Volume down" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button, { detail: 1 });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "ArrowDown", undefined, "media-controls");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "ArrowDown", undefined, "media-controls");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "ArrowDown", undefined, "media-controls");
  });

  it("stops repeating on focus loss and does not swallow the next click", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Seek forward" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });
    fireEvent.blur(window);
    const countAfterBlur = sendSpecial.mock.calls.length;
    act(() => {
      vi.advanceTimersByTime(repeatIntervalMs * 4);
    });
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
    },
  );

  it("sends an ordinary Remote button once for a complete pointer sequence", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Play or pause" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button, { detail: 1 });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(
      "MediaPlayPause",
      undefined,
      "media-controls",
    );
  });
});
