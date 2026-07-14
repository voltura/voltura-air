import { act, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRemote } from "./remoteModeTestUtils";
describe("RemoteMode app switcher", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("keeps a short Switch app press equivalent to Alt+Tab", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 100 });
    act(() => vi.advanceTimersByTime(200));
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 100 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Tab", ["Alt"]);
  });

  it("opens the persistent switcher, steps in both directions, and commits on release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 2, clientX: 100 });
    act(() => vi.advanceTimersByTime(400));

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

  it("cancels an active switcher when the pointer gesture is cancelled", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 3, clientX: 100 });
    act(() => vi.advanceTimersByTime(400));
    fireEvent.pointerCancel(button, { pointerId: 3 });

    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Tab", ["Control", "Alt"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Escape");
  });
});
