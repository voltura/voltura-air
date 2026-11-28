import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { usePowerControl } from "../../../foundation/connection/usePowerControl";
import type { PowerCapabilities } from "../../../foundation/protocol/messages";
import { PowerControlEntry } from "./PowerControlEntry";

const capabilities: PowerCapabilities = {
  lock: true,
  blackoutDisplay: true,
  displayOff: false,
  screenSaver: false,
  screenSaverAvailable: false,
  signOut: false,
  restart: false,
  shutdown: false,
};

function ConnectedPowerEntry({ send }: { send: () => boolean }) {
  const powerControl = usePowerControl("paired", 1, send);
  return (
    <PowerControlEntry
      capabilities={{ ...capabilities, restart: true }}
      onAction={powerControl.requestPowerAction}
      onOpen={() => undefined}
      pendingAction={powerControl.pendingPowerAction}
      result={powerControl.powerActionResult}
    />
  );
}

describe("PowerControlEntry", () => {
  afterEach(() => vi.useRealTimers());

  it("opens one compact power sheet entry and forwards allowed actions", () => {
    const onAction = vi.fn();
    const onOpen = vi.fn();
    render(
      <PowerControlEntry
        capabilities={capabilities}
        onAction={onAction}
        onOpen={onOpen}
        pendingAction={null}
        result={null}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Power" }));
    expect(onOpen).toHaveBeenCalledOnce();
    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /Lock PC/ }));
    expect(onAction).toHaveBeenCalledExactlyOnceWith("lock");
  });

  it("does not render against hosts without power capabilities", () => {
    const { container } = render(
      <PowerControlEntry
        capabilities={null}
        onAction={vi.fn()}
        onOpen={vi.fn()}
        pendingAction={null}
        result={null}
      />,
    );
    expect(container.childElementCount).toBe(0);
  });

  it.each([
    ["blackoutDisplay", /Blackout display/],
    ["restart", /Restart PC/],
  ] as const)("keeps a refused %s request visible in the power sheet", async (action, name) => {
    vi.useFakeTimers();
    const send = vi.fn(() => false);
    render(<ConnectedPowerEntry send={send} />);

    fireEvent.click(screen.getByRole("button", { name: "Power" }));
    fireEvent.click(screen.getByRole("button", { name }));
    if (action === "restart") {
      fireEvent.keyDown(screen.getByRole("button", { name: "Hold to restart pc" }), {
        key: "Enter",
      });
      await act(() => vi.advanceTimersByTime(1600));
    }

    expect(send).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ type: "system.power", action }),
    );
    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toContain("Could not send the power request");
    expect(screen.getByRole<HTMLButtonElement>("button", { name }).disabled).toBe(false);
  });
});
