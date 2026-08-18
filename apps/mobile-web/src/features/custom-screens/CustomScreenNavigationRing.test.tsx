import { fireEvent, render, screen } from "@testing-library/react";
import type { TouchEventHandler } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CustomScreenNavigationRing } from "./CustomScreenNavigationRing";

describe("CustomScreenNavigationRing", () => {
  afterEach(() => { vi.useRealTimers(); });

  it("repeats directions and exposes a keyboard-accessible mini-trackpad", () => {
    vi.useFakeTimers();
    const sendSpecial = vi.fn();
    const onCenterKey = vi.fn();
    renderRing({ sendSpecial, onCenterKey });

    const down = screen.getByRole("button", { name: "D-pad down" });
    fireEvent.pointerDown(down, { button: 0, pointerId: 7 });
    vi.advanceTimersByTime(455);
    fireEvent.pointerUp(down, { button: 0, pointerId: 7 });
    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "ArrowDown");

    fireEvent.keyDown(
      screen.getByRole("button", { name: "Mini trackpad" }),
      { key: "Enter" });
    expect(onCenterKey).toHaveBeenCalledExactlyOnceWith();
  });

  it("disables every action when remote input is unavailable", () => {
    const sendSpecial = vi.fn();
    const onCenterKey = vi.fn();
    renderRing({ enabled: false, sendSpecial, onCenterKey });

    expect(screen.getByRole<HTMLButtonElement>("button", { name: "D-pad up" }).disabled)
      .toBe(true);
    const center = screen.getByRole("button", { name: "Mini trackpad" });
    expect(center.getAttribute("aria-disabled")).toBe("true");
    expect(center.getAttribute("tabindex")).toBe("-1");
    fireEvent.keyDown(center, { key: "Enter" });
    expect(sendSpecial).not.toHaveBeenCalled();
    expect(onCenterKey).not.toHaveBeenCalled();
  });

  it.each(["pointerCancel", "lostPointerCapture"] as const)(
    "does not duplicate a direction after %s",
    (boundary) => {
      const sendSpecial = vi.fn();
      renderRing({ sendSpecial });
      const up = screen.getByRole("button", { name: "D-pad up" });

      fireEvent.pointerDown(up, { button: 0, pointerId: 7 });
      fireEvent[boundary](up, { pointerId: 7 });
      fireEvent.click(up, { detail: 1 });

      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("ArrowUp");
    }
  );

  it("uses the surrounding regular trackpad surface without treating directions as pointer input", () => {
    const onTouchStart = vi.fn();
    renderRing({ onTouchStart });

    const surface = screen.getByRole("application", {
      name: "Navigation ring trackpad"
    });
    expect(surface.classList.contains("trackpad-surface")).toBe(true);

    fireEvent.touchStart(surface);
    expect(onTouchStart).toHaveBeenCalledOnce();

    fireEvent.touchStart(screen.getByRole("button", { name: "D-pad up" }));
    expect(onTouchStart).toHaveBeenCalledOnce();

    fireEvent.touchStart(screen.getByRole("button", { name: "Mini trackpad" }));
    expect(onTouchStart).toHaveBeenCalledTimes(2);
  });
});

function renderRing({
  enabled = true,
  onCenterKey = vi.fn(),
  onTouchStart = vi.fn(),
  sendSpecial = vi.fn()
}: {
  enabled?: boolean;
  onCenterKey?: () => void;
  onTouchStart?: TouchEventHandler<HTMLDivElement>;
  sendSpecial?: (key: string) => void;
} = {}) {
  return render(
    <CustomScreenNavigationRing
      enabled={enabled}
      name="Navigation ring"
      onCenterKey={onCenterKey}
      onTouchCancel={vi.fn()}
      onTouchEnd={vi.fn()}
      onTouchMove={vi.fn()}
      onTouchStart={onTouchStart}
      sendSpecial={sendSpecial}
    />
  );
}
