import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PowerCapabilities } from "../../protocol";
import { PowerControlSheet } from "./PowerControlSheet";

const allAllowed: PowerCapabilities = {
  lock: true,
  lockAvailability: "notExplicitlyDisabled",
  blackoutDisplay: true,
  displayOff: true,
  screenSaver: true,
  screenSaverAvailable: true,
  signOut: true,
  restart: true,
  shutdown: true
};

describe("PowerControlSheet", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("shows every action and explains host-disabled permissions", () => {
    render(
      <PowerControlSheet
        capabilities={{ ...allAllowed, displayOff: false, restart: false }}
        onAction={vi.fn()}
        onClose={vi.fn()}
        pendingAction={null}
        result={null}
      />
    );

    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Turn off display/ }).hasAttribute("disabled")).toBe(true);
    expect(screen.getByRole("button", { name: /Restart PC/ }).textContent).toContain("Disabled by the host.");
    expect(screen.getByRole("button", { name: /Lock PC/ }).hasAttribute("disabled")).toBe(false);
  });

  it("keeps Lock PC open while the host result is pending", () => {
    const onAction = vi.fn();
    const onClose = vi.fn();
    render(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={null} result={null} />);

    expect(screen.getByRole("button", { name: /Turn off display/ }).textContent).toContain("Some PCs also enter sleep");
    fireEvent.click(screen.getByRole("button", { name: /Lock PC/ }));

    expect(onAction).toHaveBeenCalledExactlyOnceWith("lock");
    expect(onClose).not.toHaveBeenCalled();
  });

  it("warns that display off can require physical wake before sending it", () => {
    const onAction = vi.fn();
    render(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={vi.fn()} pendingAction={null} result={null} />);

    fireEvent.click(screen.getByRole("button", { name: /Turn off display/ }));

    expect(screen.getByText(/cannot wake it remotely/i)).toBeTruthy();
    expect(screen.getByText(/Windows may require sign-in after wake/i)).toBeTruthy();
    expect(onAction).not.toHaveBeenCalled();
  });

  it("only shows the screen saver action when Windows reports it available", () => {
    const { rerender } = render(
      <PowerControlSheet capabilities={{ ...allAllowed, screenSaverAvailable: false }} onAction={vi.fn()} onClose={vi.fn()} pendingAction={null} result={null} />
    );

    expect(screen.queryByRole("button", { name: /Turn on screen saver/ })).toBeNull();

    rerender(<PowerControlSheet capabilities={allAllowed} onAction={vi.fn()} onClose={vi.fn()} pendingAction={null} result={null} />);
    expect(screen.getByRole("button", { name: /Turn on screen saver/ })).toBeTruthy();
  });

  it("closes after requesting blackout and retains its host result for the next open", () => {
    const onAction = vi.fn();
    const onClose = vi.fn();
    const { rerender } = render(
      <PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={null} result={null} />
    );

    fireEvent.click(screen.getByRole("button", { name: /Blackout display/ }));
    expect(onAction).toHaveBeenCalledExactlyOnceWith("blackoutDisplay");
    expect(onClose).toHaveBeenCalledOnce();

    rerender(
      <PowerControlSheet
        capabilities={allAllowed}
        onAction={onAction}
        onClose={onClose}
        pendingAction={null}
        result={{ type: "system.power.result", action: "blackoutDisplay", succeeded: true, message: "Displays are blacked out." }}
      />
    );
    expect(screen.getByRole("status").textContent).toBe("Displays are blacked out.");
  });

  it("disables duplicate Lock PC presses and shows the host result", () => {
    const onAction = vi.fn();
    const { rerender } = render(
      <PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={vi.fn()} pendingAction="lock" result={null} />
    );

    const pendingLock = screen.getByRole("button", { name: /Lock PC/ });
    expect(pendingLock.hasAttribute("disabled")).toBe(true);
    expect(screen.getByRole("status").textContent).toContain("Waiting for the PC");
    fireEvent.click(pendingLock);
    expect(onAction).not.toHaveBeenCalled();

    rerender(
      <PowerControlSheet
        capabilities={allAllowed}
        onAction={onAction}
        onClose={vi.fn()}
        pendingAction={null}
        result={{ type: "system.power.result", action: "lock", succeeded: false, code: "VAIR-POWER-EXECUTION-FAILED", message: "Windows rejected the lock request." }}
      />
    );

    expect(screen.getByRole("alert").textContent).toBe("Windows rejected the lock request.");
    expect(screen.getByRole("button", { name: /Lock PC/ }).hasAttribute("disabled")).toBe(false);
  });

  it("explains when Windows policy disables locking", () => {
    render(
      <PowerControlSheet
        capabilities={{ ...allAllowed, lockAvailability: "disabledByPolicy" }}
        onAction={vi.fn()}
        onClose={vi.fn()}
        pendingAction={null}
        result={null}
      />
    );

    const lock = screen.getByRole("button", { name: /Lock PC/ });
    expect(lock.hasAttribute("disabled")).toBe(true);
    expect(lock.textContent).toContain("Disabled in Windows. Open Voltura Air on the PC to enable it.");
  });

  it("requires an uninterrupted hold for destructive actions", () => {
    const onAction = vi.fn();
    render(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={vi.fn()} pendingAction={null} result={null} />);

    fireEvent.click(screen.getByRole("button", { name: /Restart PC/ }));
    const holdButton = screen.getByRole("button", { name: "Hold to restart pc" });

    fireEvent.pointerDown(holdButton, { button: 0, pointerId: 1 });
    act(() => vi.advanceTimersByTime(900));
    fireEvent.pointerUp(holdButton, { pointerId: 1 });
    act(() => vi.advanceTimersByTime(1000));
    expect(onAction).not.toHaveBeenCalled();

    fireEvent.pointerDown(holdButton, { button: 0, pointerId: 2 });
    act(() => vi.advanceTimersByTime(1600));
    expect(onAction).toHaveBeenCalledExactlyOnceWith("restart");
  });

  it("returns from confirmation without triggering the action", () => {
    const onAction = vi.fn();
    render(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={vi.fn()} pendingAction={null} result={null} />);

    fireEvent.click(screen.getByRole("button", { name: /Shut down PC/ }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();
    expect(onAction).not.toHaveBeenCalled();
  });
});
