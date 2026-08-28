import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import type * as PairingFeedbackModule from "../../foundation/pairing/pairingFeedback";
import { PairingStatus } from "./PairingStatus";

vi.mock("../../foundation/diagnostics/mobileDiagnostics", () => ({
  copyTextToClipboard: vi.fn(),
}));

vi.mock("../../foundation/pairing/pairingFeedback", async (importOriginal) => {
  const actual = await importOriginal<typeof PairingFeedbackModule>();
  return {
    ...actual,
    buildPairingDiagnostics: vi.fn(() => "redacted diagnostics"),
  };
});

describe("PairingStatus", () => {
  it("renders the complete blocking error message without a truncating text treatment", () => {
    const message = "PC identity check failed. Scan a fresh QR code from the PC.";
    render(<PairingStatus blocksAppInteraction message={message} onPrimaryAction={vi.fn()} />);

    const description = screen.getByText(message);
    expect(description.textContent).toBe(message);
    expect(description.classList.contains("pairing-message")).toBe(true);
  });

  it("shows the detected device name as a placeholder without blocking edits", () => {
    const onDeviceNameChange = vi.fn();
    render(
      <PairingStatus
        deviceName=""
        deviceNamePlaceholder="Android phone"
        message="Confirm the device name"
        onDeviceNameChange={onDeviceNameChange}
        onPrimaryAction={vi.fn()}
      />,
    );

    const input = screen.getByRole("textbox", { name: "Device name" });
    expect(input.getAttribute("placeholder")).toBe("Android phone");
    expect((input as HTMLInputElement).value).toBe("");

    fireEvent.change(input, { target: { value: "Kitchen phone" } });
    expect(onDeviceNameChange).toHaveBeenCalledExactlyOnceWith("Kitchen phone");
  });

  it("keeps keyboard focus inside blocking connection feedback", () => {
    render(
      <PairingStatus activePcUnavailable message="PC is not available" onPrimaryAction={vi.fn()} />,
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

  it("shows relay-specific recovery while retaining the standard unavailable actions", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available. Retrying..."
        onPrimaryAction={vi.fn()}
        transportMode="relay"
      />,
    );

    expect(screen.getByRole("heading", { name: "Relay connection unavailable" })).toBeTruthy();
    expect(screen.getByText(/VPN or work network/u)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Try reconnect" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Enter host manually" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Open troubleshooting help" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copy diagnostics" })).toBeTruthy();
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
          { id: "pc-b", label: "Living Room PC" },
        ]}
        selectedSavedPcId="pc-a"
      />,
    );

    const savedPcSelect = screen.getByRole("combobox", { name: "Saved PC" });
    const primaryAction = screen.getByRole("button", { name: "Take photo of QR code" });

    expect(primaryAction).toBe(document.activeElement);
    expect(fireEvent.keyDown(primaryAction, { key: "Tab", shiftKey: true })).toBe(true);

    savedPcSelect.focus();
    expect(fireEvent.keyDown(savedPcSelect, { key: "Tab", shiftKey: true })).toBe(false);
    expect(primaryAction).toBe(document.activeElement);
  });

  it("describes live scanning without changing the saved-PC recovery structure", () => {
    render(
      <PairingStatus
        blocksAppInteraction
        message="Choose a saved PC"
        onPrimaryAction={vi.fn()}
        onSavedPcChange={vi.fn()}
        savedPcOptions={[{ id: "pc-a", label: "Office PC" }]}
        secondaryLabel="Scan QR code"
        selectedSavedPcId="pc-a"
        usesLivePairingQr
      />,
    );

    expect(
      screen.getByText("Reconnect to Office PC, or pair another PC by scanning its QR code."),
    ).toBeTruthy();
  });

  it("replaces the disabled reconnect action with visible bounded progress", async () => {
    vi.useFakeTimers();
    const onPrimaryAction = vi.fn();
    try {
      const view = render(
        <PairingStatus
          activePcUnavailable
          message="PC is not available"
          onPrimaryAction={onPrimaryAction}
          pcName="Living Room PC"
          transportMode="secure-direct"
        />,
      );

      fireEvent.click(screen.getByRole("button", { name: "Try reconnect" }));
      expect(onPrimaryAction).toHaveBeenCalledOnce();

      view.rerender(
        <PairingStatus
          activePcUnavailable
          connectionProgress="reconnecting"
          message="Connecting"
          onPrimaryAction={onPrimaryAction}
          pcName="Living Room PC"
          transportMode="secure-direct"
        />,
      );

      expect(screen.queryByRole("button", { name: "Reconnecting…" })).toBeNull();
      expect(screen.getByText("Checking private LAN")).toBeTruthy();
      expect(screen.getByText("About 20 seconds remaining")).toBeTruthy();
      const progress = screen.getByRole("progressbar", {
        name: "Connection check in progress",
      });
      expect(progress).toBeTruthy();
      expect(progress.firstElementChild?.classList.contains("is-determinate")).toBe(true);
      expect((progress.firstElementChild as HTMLElement).style.animationDuration).toBe("20000ms");
      expect(screen.getByRole("dialog")).toBe(document.activeElement);

      await act(() => vi.advanceTimersByTimeAsync(19_000));
      expect(screen.getByText("About 1 second remaining")).toBeTruthy();

      await act(() => vi.advanceTimersByTimeAsync(1000));
      expect(screen.getByText("Finishing check…")).toBeTruthy();

      view.rerender(
        <PairingStatus
          activePcUnavailable
          connectionProgress="connected"
          message="Connected"
          onPrimaryAction={onPrimaryAction}
          pcName="Living Room PC"
          transportMode="secure-direct"
        />,
      );

      expect(screen.getByText("Connected")).toBeTruthy();
      expect(screen.queryByRole("progressbar")).toBeNull();
      expect(screen.getByRole("dialog")).toBe(document.activeElement);

      view.rerender(
        <PairingStatus
          activePcUnavailable
          connectionProgress="reconnecting"
          message="Connecting again"
          onPrimaryAction={onPrimaryAction}
          pcName="Living Room PC"
          transportMode="secure-direct"
        />,
      );
      expect(screen.getByText("About 20 seconds remaining")).toBeTruthy();
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps one reconnect countdown running through portrait and landscape changes", async () => {
    vi.useFakeTimers();
    const originalWidth = window.innerWidth;
    const originalHeight = window.innerHeight;
    try {
      render(
        <PairingStatus
          activePcUnavailable
          connectionProgress="reconnecting"
          message="Connecting"
          onPrimaryAction={vi.fn()}
          pcName="Living Room PC"
          transportMode="secure-direct"
        />,
      );
      const progress = screen.getByRole("progressbar", { name: "Connection check in progress" });

      await act(() => vi.advanceTimersByTimeAsync(5000));
      expect(screen.getByText("About 15 seconds remaining")).toBeTruthy();

      Object.defineProperties(window, {
        innerWidth: { configurable: true, value: 844 },
        innerHeight: { configurable: true, value: 390 },
      });
      act(() => {
        window.dispatchEvent(new Event("resize"));
      });

      expect(screen.getByRole("progressbar", { name: "Connection check in progress" })).toBe(
        progress,
      );
      expect(screen.getByText("About 15 seconds remaining")).toBeTruthy();

      await act(() => vi.advanceTimersByTimeAsync(5000));
      expect(screen.getByText("About 10 seconds remaining")).toBeTruthy();
    } finally {
      Object.defineProperties(window, {
        innerWidth: { configurable: true, value: originalWidth },
        innerHeight: { configurable: true, value: originalHeight },
      });
      vi.useRealTimers();
    }
  });

  it("makes QR decoding visibly pending and non-interactive", () => {
    const onPrimaryAction = vi.fn();
    render(
      <PairingStatus
        blocksAppInteraction
        message="Reading QR code..."
        onPrimaryAction={onPrimaryAction}
        primaryActionPending
      />,
    );

    const action = screen.getByRole("button", { name: "Reading QR code…" });
    expect((action as HTMLButtonElement).disabled).toBe(true);
    expect(action.getAttribute("aria-busy")).toBe("true");
    fireEvent.click(action);
    expect(onPrimaryAction).not.toHaveBeenCalled();
  });

  it("shows copied diagnostics as a toast when the selected PC is unavailable", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValueOnce("copied");

    render(
      <PairingStatus activePcUnavailable message="PC is not available" onPrimaryAction={vi.fn()} />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Copy diagnostics" }));

    await waitFor(() => {
      expect(document.querySelector(".app-toast.success")?.textContent).toBe("Diagnostics copied.");
    });
    expect(
      screen.queryByText(
        "Could not copy automatically. Select the diagnostics below and copy manually.",
      ),
    ).toBeNull();
  });

  it("keeps invalid manual input and does not pass it to the connection controller", () => {
    const onManualHostSubmit = vi.fn();
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onManualHostSubmit={onManualHostSubmit}
        onPrimaryAction={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Enter host manually" }));
    const input = screen.getByRole("textbox", { name: "Host or pairing link" });
    fireEvent.change(input, { target: { value: "https://pc.local:51395/path" } });
    fireEvent.click(screen.getByRole("button", { name: "Connect" }));

    expect(onManualHostSubmit).not.toHaveBeenCalled();
    expect((input as HTMLInputElement).value).toBe("https://pc.local:51395/path");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("alert").textContent).toBe(
      "Host addresses cannot include a path, query, or fragment.",
    );
  });

  it("keeps recovery labels stable and presents manual host entry as a dismissible dialog", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onManualHostSubmit={vi.fn()}
        onPrimaryAction={vi.fn()}
      />,
    );

    const trigger = screen.getByRole("button", { name: "Enter host manually" });
    trigger.focus();
    fireEvent.click(trigger);

    expect(screen.getByRole("button", { name: "Enter host manually" }).textContent).toBe(
      "Enter host manually",
    );
    expect(screen.getByRole("dialog", { name: "Enter host manually" })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "Host or pairing link" })).toBe(
      document.activeElement,
    );

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("button", { name: "Close Enter host manually" }));
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("dialog", { name: "Enter host manually" }), {
      clientX: -1,
      clientY: -1,
    });
    expect(screen.queryByRole("dialog", { name: "Enter host manually" })).toBeNull();
  });

  it("presents troubleshooting as an information dialog with a stable trigger", () => {
    render(
      <PairingStatus activePcUnavailable message="PC is not available" onPrimaryAction={vi.fn()} />,
    );

    const trigger = screen.getByRole("button", { name: "Open troubleshooting help" });
    trigger.focus();
    fireEvent.click(trigger);

    expect(screen.getByRole("button", { name: "Open troubleshooting help" }).textContent).toBe(
      "Open troubleshooting help",
    );
    expect(screen.getByRole("dialog", { name: "Troubleshooting help" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "OK" })).toBe(document.activeElement);

    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog", { name: "Troubleshooting help" })).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});
