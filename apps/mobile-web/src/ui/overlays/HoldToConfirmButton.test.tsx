import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HoldToConfirmButton } from "./HoldToConfirmButton";

describe("HoldToConfirmButton", () => {
  it("cancels a hold when the page departs", () => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="action" onConfirm={confirm} />);
    fireEvent.keyDown(screen.getByRole("button"), { key: "Enter" });
    window.dispatchEvent(new Event("pagehide"));
    vi.advanceTimersByTime(2000);
    expect(confirm).not.toHaveBeenCalled();
  });
  it("uses the latest completion callback for an unchanged action", () => {
    const old = vi.fn(),
      current = vi.fn();
    const view = render(<HoldToConfirmButton disabled={false} label="action" onConfirm={old} />);
    fireEvent.keyDown(screen.getByRole("button"), { key: "Enter" });
    vi.advanceTimersByTime(800);
    view.rerender(<HoldToConfirmButton disabled={false} label="action" onConfirm={current} />);
    vi.advanceTimersByTime(800);
    expect(old).not.toHaveBeenCalled();
    expect(current).toHaveBeenCalledOnce();
  });
  it("does not cancel the owning pointer hold when another pointer releases", () => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="action" onConfirm={confirm} />);
    const button = screen.getByRole("button");
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(button, { pointerId: 2 });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledOnce();
  });
  it("does not cancel an Enter hold when an unrelated key is released", () => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="action" onConfirm={confirm} />);
    fireEvent.keyDown(screen.getByRole("button"), { key: "Enter" });
    fireEvent.keyUp(screen.getByRole("button"), { key: "Shift" });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledOnce();
  });
  it("can hold when the browser rejects optional pointer capture", () => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="action" onConfirm={confirm} />);
    const button = screen.getByRole("button");
    button.setPointerCapture = () => {
      throw new DOMException("Retired", "NotFoundError");
    };
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledOnce();
  });
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  it.each([
    [
      "lost pointer capture",
      (button: HTMLButtonElement) => {
        fireEvent.lostPointerCapture(button);
      },
    ],
    [
      "button focus loss",
      (button: HTMLButtonElement) => {
        fireEvent.blur(button);
      },
    ],
    [
      "window focus loss",
      () => {
        fireEvent.blur(window);
      },
    ],
    [
      "document hiding",
      () => {
        Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
        fireEvent(document, new Event("visibilitychange"));
      },
    ],
  ])("cancels an active hold after %s", (_, cancel) => {
    const confirm = vi.fn();
    render(<HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />);
    const button = screen.getByRole("button", {
      name: "Hold to shut down pc",
    }) as HTMLButtonElement;

    fireEvent.keyDown(button, { key: "Enter" });
    vi.advanceTimersByTime(800);
    cancel(button);
    vi.advanceTimersByTime(1600);

    expect(confirm).not.toHaveBeenCalled();
  });

  it("can confirm again after the parent completes a disabled pending cycle", () => {
    const confirm = vi.fn();
    const view = render(
      <HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />,
    );
    const button = screen.getByRole("button", { name: "Hold to shut down pc" });
    fireEvent.keyDown(button, { key: "Enter" });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledTimes(1);

    view.rerender(<HoldToConfirmButton disabled label="shut down PC" onConfirm={confirm} />);
    view.rerender(
      <HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />,
    );
    fireEvent.keyDown(button, { key: "Enter" });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it("does not complete a hold after becoming disabled", () => {
    const confirm = vi.fn();
    const view = render(
      <HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />,
    );
    const button = screen.getByRole("button", { name: "Hold to shut down pc" });
    fireEvent.keyDown(button, { key: "Enter" });
    vi.advanceTimersByTime(800);
    view.rerender(<HoldToConfirmButton disabled label="shut down PC" onConfirm={confirm} />);
    vi.advanceTimersByTime(1600);
    expect(confirm).not.toHaveBeenCalled();
  });

  it("cancels the old hold when the displayed action changes", () => {
    const confirm = vi.fn();
    const view = render(
      <HoldToConfirmButton disabled={false} label="shut down PC" onConfirm={confirm} />,
    );
    fireEvent.keyDown(screen.getByRole("button"), { key: "Enter" });
    vi.advanceTimersByTime(800);
    view.rerender(<HoldToConfirmButton disabled={false} label="restart PC" onConfirm={confirm} />);
    vi.advanceTimersByTime(1600);
    expect(confirm).not.toHaveBeenCalled();
    fireEvent.keyDown(screen.getByRole("button"), { key: "Enter" });
    vi.advanceTimersByTime(1600);
    expect(confirm).toHaveBeenCalledOnce();
  });
});
