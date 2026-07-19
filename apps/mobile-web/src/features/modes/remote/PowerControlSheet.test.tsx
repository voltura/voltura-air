import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PowerCapabilities, SystemPowerAction } from "../../../foundation/protocol/messages";
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

const actionNames: Record<SystemPowerAction, RegExp> = {
  lock: /Lock PC/,
  blackoutDisplay: /Blackout display/,
  displayOff: /Turn off display/,
  screenSaver: /Turn on screen saver/,
  signOut: /Sign out/,
  restart: /Restart PC/,
  shutdown: /Shut down PC/
};

describe("PowerControlSheet", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("offers one basic Keep awake toggle and leaves screen behavior to the host", () => {
    const onAwakeChange = vi.fn();
    render(
      <PowerControlSheet
        awake={{ canControl: true, active: false, mode: "off" }}
        capabilities={allAllowed}
        onAction={vi.fn()}
        onAwakeChange={onAwakeChange}
        onClose={vi.fn()}
        pendingAction={null}
        result={null}
      />
    );

    const keepAwake = screen.getByRole("button", { name: /Keep awake/ });
    expect(keepAwake.textContent).toContain("using the host screen setting");
    expect(screen.getByLabelText("Keep awake is off").classList.contains("off")).toBe(true);
    expect(screen.queryByText(/Keep screen on/)).toBeNull();
    fireEvent.click(keepAwake);
    expect(onAwakeChange).toHaveBeenCalledExactlyOnceWith(true);
  });

  it("shows active Keep awake state and host permission", () => {
    const { rerender } = render(
      <PowerControlSheet awake={{ canControl: true, active: true, mode: "timed" }} capabilities={allAllowed} onAction={vi.fn()} onClose={vi.fn()} pendingAction={null} result={null} />
    );
    expect(screen.getByRole("button", { name: /Keep awake/ }).textContent).toContain("On.");
    expect(screen.getByLabelText("Keep awake is on").classList.contains("on")).toBe(true);

    rerender(<PowerControlSheet awake={{ canControl: false, active: false, mode: "off" }} capabilities={allAllowed} onAction={vi.fn()} onClose={vi.fn()} pendingAction={null} result={null} />);
    expect(screen.getByRole("button", { name: /Keep awake/ }).hasAttribute("disabled")).toBe(true);
    expect(screen.getByLabelText("Keep awake is off").classList.contains("off")).toBe(true);
  });

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
        result={{ type: "system.power.result", operationId: "power-success", action: "blackoutDisplay", succeeded: true, message: "Displays are blacked out." }}
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
        result={{ type: "system.power.result", operationId: "power-failure", action: "lock", succeeded: false, code: "VAIR-POWER-EXECUTION-FAILED", message: "Windows rejected the lock request." }}
      />
    );

    expect(screen.getByRole("alert").textContent).toBe("Windows rejected the lock request.");
    expect(screen.getByRole("button", { name: /Lock PC/ }).hasAttribute("disabled")).toBe(false);
  });

  it.each(["lock", "blackoutDisplay", "displayOff", "screenSaver", "signOut", "restart", "shutdown"] as const)("disables every power action while %s is pending", (pendingAction) => {
    const onAction = vi.fn();
    const onClose = vi.fn();
    const { rerender } = render(
      <PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={pendingAction} result={null} />
    );

    for (const name of [/Lock PC/, /Blackout display/, /Turn off display/, /Turn on screen saver/, /Sign out/, /Restart PC/, /Shut down PC/]) {
      const action = screen.getByRole<HTMLButtonElement>("button", { name });
      expect(action.disabled).toBe(true);
      fireEvent.click(action);
    }
    expect(screen.getByRole("button", { name: actionNames[pendingAction] }).textContent).toContain("Waiting for the PC");
    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();
    expect(onAction).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();

    rerender(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={null} result={null} />);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: /Restart PC/ }).disabled).toBe(false);
  });

  it("cancels an open destructive confirmation when another request becomes pending", async () => {
    const onAction = vi.fn();
    const onClose = vi.fn();
    const { rerender } = render(
      <PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={null} result={null} />
    );
    fireEvent.click(screen.getByRole("button", { name: /Shut down PC/ }));
    const holdButton = screen.getByRole("button", { name: "Hold to shut down pc" });
    fireEvent.pointerDown(holdButton, { button: 0, pointerId: 1 });
    await act(() => vi.advanceTimersByTime(800));

    rerender(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction="lock" result={null} />);
    expect(screen.getByRole("dialog", { name: "Shut down PC" })).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Hold to shut down pc" }).disabled).toBe(true);
    expect(screen.getByRole("button", { name: "Hold to shut down pc" }).textContent).toContain("Wait for the current power request");
    await act(() => vi.advanceTimersByTime(1000));

    expect(onAction).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();

    rerender(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={onClose} pendingAction={null} result={null} />);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Hold to shut down pc" }).disabled).toBe(false);
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

  it("requires an uninterrupted hold for destructive actions", async () => {
    const onAction = vi.fn();
    render(<PowerControlSheet capabilities={allAllowed} onAction={onAction} onClose={vi.fn()} pendingAction={null} result={null} />);

    fireEvent.click(screen.getByRole("button", { name: /Restart PC/ }));
    const holdButton = screen.getByRole("button", { name: "Hold to restart pc" });

    fireEvent.pointerDown(holdButton, { button: 0, pointerId: 1 });
    await act(() => vi.advanceTimersByTime(900));
    fireEvent.pointerUp(holdButton, { pointerId: 1 });
    await act(() => vi.advanceTimersByTime(1000));
    expect(onAction).not.toHaveBeenCalled();

    fireEvent.pointerDown(holdButton, { button: 0, pointerId: 2 });
    await act(() => vi.advanceTimersByTime(1600));
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
