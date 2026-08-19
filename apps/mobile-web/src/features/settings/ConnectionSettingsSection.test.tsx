import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ConnectionSettingsSection } from "./ConnectionSettingsSection";

const baseProps = {
  activePc: null,
  deviceName: "Phone",
  diagnostics: "{}",
  disconnectActivePc: vi.fn(),
  forgetPc: vi.fn(),
  isPairingQrReading: false,
  onManualHostSubmit: vi.fn(),
  onPairingQrSelected: vi.fn(),
  pairedPcs: [],
  pairingQrInputRef: { current: null },
  pairingScanMessage: "Scan the QR code shown on your PC.",
  renameDevice: vi.fn(),
  renamePc: vi.fn(),
  scanPairingQr: vi.fn(),
  selectPc: vi.fn()
};

describe("ConnectionSettingsSection", () => {
  it("uses live scanning as the HTTPS-capable QR action", () => {
    render(<ConnectionSettingsSection {...baseProps} usesLivePairingQr />);

    expect(screen.getByRole("button", { name: "Scan QR code" })).toBeTruthy();
  });

  it("uses photo scanning when live scanning is unavailable", () => {
    render(<ConnectionSettingsSection {...baseProps} usesLivePairingQr={false} />);

    expect(screen.getByRole("button", { name: "Take photo of QR code" })).toBeTruthy();
  });
});
