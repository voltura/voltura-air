import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RemoteMode } from "./RemoteMode";
import { renderRemote } from "./remoteModeTestUtils";
describe("RemoteMode app switcher", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it.each(["capture", "blur", "pagehide", "disconnect"])(
    "cancels a held switcher on %s",
    async (reason) => {
      const sendSpecial = vi.fn();
      const { rerender, ...props } = renderRemote({ sendSpecial });
      const button = screen.getByRole("button", { name: "Switch app" });
      fireEvent.pointerDown(button, { button: 0, pointerId: 31, clientX: 100 });
      await act(() => vi.advanceTimersByTime(400));
      if (reason === "capture") {
        fireEvent.lostPointerCapture(button, { pointerId: 31 });
      } else if (reason === "disconnect") {
        rerender(<RemoteMode {...props} isConnected={false} />);
      } else {
        fireEvent(window, new Event(reason));
      }
      expect(sendSpecial).toHaveBeenLastCalledWith("Escape");
      expect(screen.queryByText("Slide left or right to choose")).toBeNull();
      fireEvent.pointerUp(button, { pointerId: 31, clientX: 100 });
      expect(sendSpecial).not.toHaveBeenCalledWith("Enter");
    },
  );

  it("preserves the first finger while a second pointer touches the switcher", async () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 100 });
    await act(() => vi.advanceTimersByTime(400));
    fireEvent.pointerDown(button, { button: 0, pointerId: 2, clientX: 100 });
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 100 });
    expect(sendSpecial).toHaveBeenLastCalledWith("Enter");
    expect(screen.queryByText("Slide left or right to choose")).toBeNull();
  });

  it("keeps a short Switch app press equivalent to Alt+Tab", async () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 100 });
    await act(() => vi.advanceTimersByTime(200));
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 100 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Tab", ["Alt"]);
  });

  it("opens the persistent switcher, steps in both directions, and commits on release", async () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 2, clientX: 100 });
    await act(() => vi.advanceTimersByTime(400));

    expect(screen.getByRole("status").textContent).toContain("Slide left or right to choose");
    fireEvent.pointerMove(button, { pointerId: 2, clientX: 150 });
    fireEvent.pointerMove(button, { pointerId: 2, clientX: 55 });
    fireEvent.pointerUp(button, { pointerId: 2, clientX: 55 });

    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Tab", ["Control", "Alt"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Tab");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "Tab", ["Shift"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(4, "Tab", ["Shift"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(5, "Enter");
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("cancels an active switcher when the pointer gesture is cancelled", async () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 3, clientX: 100 });
    await act(() => vi.advanceTimersByTime(400));
    fireEvent.pointerCancel(button, { pointerId: 3 });

    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Tab", ["Control", "Alt"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Escape");
  });
});
