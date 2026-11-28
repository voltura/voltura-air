import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { PairingGate } from "./PairingGate";

const savedPc = {
  customName: true,
  id: "http://pc.local:51395",
  name: "Office PC",
  url: "http://pc.local:51395",
};

it.each(["disconnected", "unavailable"] as const)(
  "shows the photo read and its failure while %s with a saved reconnect action",
  (state) => {
    const tryReconnect = vi.fn();
    const message = "Could not reach the PC.";
    const failure = "No Voltura Air pairing link found in that QR code.";
    const props = {
      activePc: state === "unavailable" ? savedPc : null,
      connectManualHost: vi.fn(),
      confirmPendingPairing: vi.fn(),
      diagnostics: "Diagnostics",
      isSettingsOpen: false,
      message,
      pairingDeviceName: "",
      pairingDeviceNamePlaceholder: "Phone",
      pairingStatusMessage: message,
      pendingPairing: false,
      reconnectablePcs: [savedPc],
      scanFeedback: null,
      scanPairingQr: vi.fn(),
      setPairingDeviceName: vi.fn(),
      state,
      tryManualReconnect: tryReconnect,
      tryReconnectPc: tryReconnect,
      usesLivePairingQr: false,
    };
    const view = render(<PairingGate {...props} isPairingQrReading />);

    const readingButton = screen.getByRole<HTMLButtonElement>("button", {
      name: "Reading QR code…",
    });
    const reconnect = screen.getByRole<HTMLButtonElement>("button", { name: "Try reconnect" });
    expect(readingButton.disabled).toBe(true);
    expect(readingButton.getAttribute("aria-busy")).toBe("true");
    expect(reconnect.disabled).toBe(true);
    fireEvent.click(reconnect);
    expect(tryReconnect).not.toHaveBeenCalled();

    view.rerender(<PairingGate {...props} isPairingQrReading={false} scanFeedback={failure} />);

    expect(screen.getByRole("status").textContent).toBe(failure);
    expect(reconnect.disabled).toBe(false);
    fireEvent.click(reconnect);
    expect(tryReconnect).toHaveBeenCalledOnce();
  },
);
