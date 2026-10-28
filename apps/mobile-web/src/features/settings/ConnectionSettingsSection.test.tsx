import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ConnectionSettingsSection } from "./ConnectionSettingsSection";

import * as diagnostics from "../../foundation/diagnostics/mobileDiagnostics";

afterEach(() => vi.restoreAllMocks());

const baseProps = {
  usesLivePairingQr: false,
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
  selectPc: vi.fn(),
};

describe("ConnectionSettingsSection", () => {
  it.each(["removed", "replaced"])("retires a Forget confirmation when its PC is %s", (change) => {
    const pc = {
      customName: false,
      id: "pc",
      name: "PC",
      url: "http://127.0.0.1",
      hostIdentityFingerprint: "a".repeat(22),
    };
    const view = render(<ConnectionSettingsSection {...baseProps} pairedPcs={[pc]} />);
    fireEvent.click(screen.getByRole("button", { name: "Forget" }));
    expect(screen.getByRole("dialog")).toBeTruthy();
    view.rerender(
      <ConnectionSettingsSection
        {...baseProps}
        pairedPcs={change === "removed" ? [] : [{ ...pc, hostIdentityFingerprint: "b".repeat(22) }]}
      />,
    );
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(baseProps.forgetPc).not.toHaveBeenCalled();
  });

  it("prevents overlapping connection diagnostics copies", async () => {
    let complete!: (value: "copied") => void;
    const copy = vi.spyOn(diagnostics, "copyTextToClipboard").mockReturnValue(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    render(<ConnectionSettingsSection {...baseProps} />);
    const button = screen.getByRole("button", { name: "Copy diagnostics" });
    fireEvent.click(button);
    fireEvent.click(button);
    expect(copy).toHaveBeenCalledOnce();
    await act(async () => {
      complete("copied");
      await Promise.resolve();
    });
    expect((button as HTMLButtonElement).disabled).toBe(false);
  });

  it("uses live scanning as the HTTPS-capable QR action", () => {
    render(<ConnectionSettingsSection {...baseProps} usesLivePairingQr />);

    expect(screen.getByRole("button", { name: "Scan QR code" })).toBeTruthy();
  });

  it("uses photo scanning when live scanning is unavailable", () => {
    render(<ConnectionSettingsSection {...baseProps} usesLivePairingQr={false} />);

    expect(screen.getByRole("button", { name: "Take photo of QR code" })).toBeTruthy();
  });
});
