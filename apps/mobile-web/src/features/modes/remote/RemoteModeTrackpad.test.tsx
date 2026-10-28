import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../../../foundation/settings/remoteSettings";
import { renderRemote } from "./remoteModeTestUtils";
describe("RemoteMode mini trackpad", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it.each(["Mini trackpad", "panel"])("keeps the first pointer in control of %s", (surface) => {
    const onPointerMove = vi.fn();
    renderRemote({ onPointerMove });
    const target =
      surface === "panel"
        ? screen.getByText("Navigation").closest(".remote-navigation-section")!
        : screen.getByRole("button", { name: surface });
    fireEvent.pointerDown(target, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerDown(target, { button: 0, pointerId: 2, clientX: 100, clientY: 200 });
    fireEvent.pointerMove(target, { pointerId: 1, clientX: 14, clientY: 18 });
    expect(onPointerMove).toHaveBeenCalledExactlyOnceWith(5.4, -2.7);
  });

  it.each(["blur", "hidden", "pagehide"])(
    "cancels delayed clicks and active drags on %s",
    (reason) => {
      const onPointerButtonClick = vi.fn();
      const onPointerMove = vi.fn();
      renderRemote({ onPointerButtonClick, onPointerMove });
      const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
      fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
      fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20 });
      fireEvent.pointerDown(trackpad, { button: 0, pointerId: 2, clientX: 10, clientY: 20 });
      if (reason === "hidden") {
        vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
        fireEvent(document, new Event("visibilitychange"));
      } else {
        if (reason === "pagehide") {
          fireEvent(window, new Event("pagehide"));
        } else {
          fireEvent.blur(window);
        }
      }
      fireEvent.pointerMove(trackpad, { pointerId: 2, clientX: 30, clientY: 20 });
      act(() => {
        vi.advanceTimersByTime(300);
      });
      expect(onPointerButtonClick).not.toHaveBeenCalled();
      expect(onPointerMove).not.toHaveBeenCalled();
    },
  );

  it("preserves both separate taps when the second is too far away for a double tap", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });
    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    for (const x of [10, 100]) {
      fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: x, clientY: 20 });
      fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: x, clientY: 20 });
    }
    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(onPointerButtonClick.mock.calls).toEqual([["left"], ["left"]]);
  });

  it.each(["Mini trackpad", "panel"])(
    "cancels %s gestures after pointer capture is lost",
    (surface) => {
      const onPointerButtonClick = vi.fn();
      const onPointerMove = vi.fn();
      renderRemote({ onPointerButtonClick, onPointerMove });
      const target =
        surface === "panel"
          ? screen.getByText("Navigation").closest(".remote-navigation-section")!
          : screen.getByRole("button", { name: surface });
      fireEvent.pointerDown(target, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
      fireEvent.lostPointerCapture(target, { pointerId: 1 });
      fireEvent.pointerMove(target, { pointerId: 1, clientX: 20, clientY: 20 });
      fireEvent.pointerUp(target, { pointerId: 1, clientX: 10, clientY: 20 });
      act(() => {
        vi.advanceTimersByTime(300);
      });
      expect(onPointerButtonClick).not.toHaveBeenCalled();
      expect(onPointerMove).not.toHaveBeenCalled();
    },
  );

  it.each(["Mini trackpad", "panel"])(
    "uses the release position when rejecting a drag as a tap on %s",
    (surface) => {
      const onPointerButtonClick = vi.fn();
      renderRemote({ onPointerButtonClick });
      const target =
        surface === "panel"
          ? screen.getByText("Navigation").closest(".remote-navigation-section")!
          : screen.getByRole("button", { name: surface });
      fireEvent.pointerDown(target, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
      fireEvent.pointerUp(target, { pointerId: 1, clientX: 100, clientY: 20 });
      act(() => {
        vi.advanceTimersByTime(300);
      });
      expect(onPointerButtonClick).not.toHaveBeenCalled();
    },
  );

  it("moves the pointer when dragged", () => {
    const onPointerMove = vi.fn();
    renderRemote({ onPointerMove });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerMove(trackpad, { pointerId: 1, clientX: 14, clientY: 18 });

    expect(onPointerMove).toHaveBeenCalledExactlyOnceWith(5.4, -2.7);
  });

  it("turns a single tap into a left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });

    expect(onPointerButtonClick).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("turns a Kodi mini trackpad tap into Enter instead of a left click", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({
      remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" },
      onPointerButtonClick,
      sendSpecial,
    });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Enter");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("turns Kodi mini trackpad keyboard activation into Enter", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({
      remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" },
      onPointerButtonClick,
      sendSpecial,
    });

    fireEvent.keyDown(screen.getByRole("button", { name: "Mini trackpad" }), { key: "Enter" });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Enter");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("turns a double tap into a right click without sending the pending left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 2, clientX: 12, clientY: 21 });
    fireEvent.pointerUp(trackpad, { pointerId: 2, clientX: 12, clientY: 21, timeStamp: 240 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("right");
  });

  it("turns a navigation panel background tap into a left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section")!;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });

    expect(onPointerButtonClick).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("turns a navigation panel background double tap into a right click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section")!;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });
    fireEvent.pointerDown(panel, { button: 0, pointerId: 2, clientX: 22, clientY: 31 });
    fireEvent.pointerUp(panel, { pointerId: 2, clientX: 22, clientY: 31, timeStamp: 240 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("right");
  });

  it("moves the pointer when the navigation panel background is dragged", () => {
    const onPointerButtonClick = vi.fn();
    const onPointerMove = vi.fn();
    renderRemote({ onPointerButtonClick, onPointerMove });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section")!;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerMove(panel, { pointerId: 1, clientX: 32, clientY: 24 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 32, clientY: 24, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerMove).toHaveBeenCalledExactlyOnceWith(16.2, -8.1);
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("does not turn ring button presses into mouse clicks", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({ onPointerButtonClick, sendSpecial });

    const button = screen.getByRole("button", { name: "D-pad left" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("ArrowLeft");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });
});
