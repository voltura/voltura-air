import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HoldToConfirmButton } from "./HoldToConfirmButton";

describe("HoldToConfirmButton", () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => {
    vi.useRealTimers();
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  it.each([
    ["button focus loss", (button: HTMLButtonElement) => { fireEvent.blur(button); }],
    ["window focus loss", () => { fireEvent.blur(window); }],
    ["document hiding", () => {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
      fireEvent(document, new Event("visibilitychange"));
    }]
  ])("cancels an active hold after %s", (_, cancel) => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />);
    const button = screen.getByRole("button", { name: "Hold to shut down pc" }) as HTMLButtonElement;

    fireEvent.keyDown(button, { key: "Enter" });
    vi.advanceTimersByTime(800);
    cancel(button);
    vi.advanceTimersByTime(1600);

    expect(confirm).not.toHaveBeenCalled();
  });
});
