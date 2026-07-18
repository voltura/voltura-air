import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "../../mobileDiagnostics";
import type * as PairingFeedbackModule from "../../pairingFeedback";
import { PairingStatus } from "./PairingStatus";

vi.mock("../../mobileDiagnostics", () => ({
  copyTextToClipboard: vi.fn()
}));

vi.mock("../../pairingFeedback", async (importOriginal) => {
  const actual = await importOriginal<typeof PairingFeedbackModule>();
  return {
    ...actual,
    buildPairingDiagnostics: vi.fn(() => "redacted diagnostics")
  };
});

describe("PairingStatus", () => {
  it("keeps keyboard focus inside blocking connection feedback", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={vi.fn()}
      />
    );

    const heading = screen.getByRole("heading", { name: "PC not available" });
    const primaryAction = screen.getByRole("button", { name: "Try reconnect" });
    const lastAction = screen.getByRole("button", { name: "Copy diagnostics" });
    expect(heading.getAttribute("tabindex")).toBeNull();
    expect(primaryAction).toBe(document.activeElement);

    fireEvent.keyDown(primaryAction, { key: "Tab", shiftKey: true });
    expect(lastAction).toBe(document.activeElement);

    fireEvent.keyDown(lastAction, { key: "Tab" });
    expect(primaryAction).toBe(document.activeElement);
  });

  it("keeps the primary action focused and bounded through reconnect progress", () => {
    const onPrimaryAction = vi.fn();
    const view = render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={onPrimaryAction}
        pcName="Living Room PC"
      />
    );

    const initialAction = screen.getByRole("button", { name: "Try reconnect" });
    fireEvent.click(initialAction);
    expect(onPrimaryAction).toHaveBeenCalledOnce();

    view.rerender(
      <PairingStatus
        activePcUnavailable
        connectionProgress="reconnecting"
        message="Connecting"
        onPrimaryAction={onPrimaryAction}
        pcName="Living Room PC"
      />
    );

    const reconnectingAction = screen.getByRole("button", { name: "Reconnecting…" });
    expect(reconnectingAction).toBe(initialAction);
    expect(reconnectingAction).toBe(document.activeElement);
    expect(reconnectingAction.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(reconnectingAction);
    expect(onPrimaryAction).toHaveBeenCalledOnce();

    view.rerender(
      <PairingStatus
        activePcUnavailable
        connectionProgress="connected"
        message="Connected"
        onPrimaryAction={onPrimaryAction}
        pcName="Living Room PC"
      />
    );

    const connectedAction = screen.getByRole("button", { name: "Connected" });
    expect(connectedAction).toBe(initialAction);
    expect(connectedAction).toBe(document.activeElement);
    expect(connectedAction.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(connectedAction);
    expect(onPrimaryAction).toHaveBeenCalledOnce();
  });

  it("shows copied diagnostics as a toast when the selected PC is unavailable", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValueOnce("copied");

    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={vi.fn()}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Copy diagnostics" }));

    await waitFor(() => {
      expect(document.querySelector(".app-toast.success")?.textContent).toBe("Diagnostics copied.");
    });
    expect(screen.queryByText("Could not copy automatically. Select the diagnostics below and copy manually.")).toBeNull();
  });
});
