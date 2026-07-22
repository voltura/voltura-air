import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import type * as PairingFeedbackModule from "../../foundation/pairing/pairingFeedback";
import { PairingStatus } from "./PairingStatus";

vi.mock("../../foundation/diagnostics/mobileDiagnostics", () => ({
  copyTextToClipboard: vi.fn()
}));

vi.mock("../../foundation/pairing/pairingFeedback", async (importOriginal) => {
  const actual = await importOriginal<typeof PairingFeedbackModule>();
  return {
    ...actual,
    buildPairingDiagnostics: vi.fn(() => "redacted diagnostics")
  };
});

describe("PairingStatus", () => {
  it("shows the detected device name as a placeholder without blocking edits", () => {
    const onDeviceNameChange = vi.fn();
    render(
      <PairingStatus
        deviceName=""
        deviceNamePlaceholder="Android phone"
        message="Confirm the device name"
        onDeviceNameChange={onDeviceNameChange}
        onPrimaryAction={vi.fn()}
      />
    );

    const input = screen.getByRole("textbox", { name: "Device name" });
    expect(input.getAttribute("placeholder")).toBe("Android phone");
    expect((input as HTMLInputElement).value).toBe("");

    fireEvent.change(input, { target: { value: "Kitchen phone" } });
    expect(onDeviceNameChange).toHaveBeenCalledExactlyOnceWith("Kitchen phone");
  });

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

  it("includes the saved-PC selector in the blocking focus order", () => {
    render(
      <PairingStatus
        blocksAppInteraction
        message="Choose a saved PC"
        onPrimaryAction={vi.fn()}
        onSavedPcChange={vi.fn()}
        savedPcOptions={[
          { id: "pc-a", label: "Office PC" },
          { id: "pc-b", label: "Living Room PC" }
        ]}
        selectedSavedPcId="pc-a"
      />
    );

    const savedPcSelect = screen.getByRole("combobox", { name: "Saved PC" });
    const primaryAction = screen.getByRole("button", { name: "Take photo of QR code" });

    expect(primaryAction).toBe(document.activeElement);
    expect(fireEvent.keyDown(primaryAction, { key: "Tab", shiftKey: true })).toBe(true);

    savedPcSelect.focus();
    expect(fireEvent.keyDown(savedPcSelect, { key: "Tab", shiftKey: true })).toBe(false);
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

  it("keeps invalid manual input and does not pass it to the connection controller", () => {
    const onManualHostSubmit = vi.fn();
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onManualHostSubmit={onManualHostSubmit}
        onPrimaryAction={vi.fn()}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Enter host manually" }));
    const input = screen.getByRole("textbox", { name: "Host or pairing link" });
    fireEvent.change(input, { target: { value: "https://pc.local:51395/path" } });
    fireEvent.click(screen.getByRole("button", { name: "Connect" }));

    expect(onManualHostSubmit).not.toHaveBeenCalled();
    expect((input as HTMLInputElement).value).toBe("https://pc.local:51395/path");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("alert").textContent).toBe("Host addresses cannot include a path, query, or fragment.");
  });

  it("keeps recovery labels stable and presents manual host entry as a dismissible dialog", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onManualHostSubmit={vi.fn()}
        onPrimaryAction={vi.fn()}
      />
    );

    const trigger = screen.getByRole("button", { name: "Enter host manually" });
    trigger.focus();
    fireEvent.click(trigger);

    expect(screen.getByRole("button", { name: "Enter host manually" }).textContent).toBe("Enter host manually");
    expect(screen.getByRole("dialog", { name: "Enter host manually" })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "Host or pairing link" })).toBe(document.activeElement);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("button", { name: "Close Enter host manually" }));
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("dialog", { name: "Enter host manually" }), { clientX: -1, clientY: -1 });
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();
  });

  it("presents troubleshooting as an information dialog with a stable trigger", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={vi.fn()}
      />
    );

    const trigger = screen.getByRole("button", { name: "Open troubleshooting help" });
    trigger.focus();
    fireEvent.click(trigger);

    expect(screen.getByRole("button", { name: "Open troubleshooting help" }).textContent).toBe("Open troubleshooting help");
    expect(screen.getByRole("dialog", { name: "Troubleshooting help" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "OK" })).toBe(document.activeElement);

    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog", { name: "Troubleshooting help" })).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});
