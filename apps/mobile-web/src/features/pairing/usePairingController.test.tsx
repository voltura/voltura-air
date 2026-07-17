import { act, renderHook, waitFor } from "@testing-library/react";
import type { ChangeEvent } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { decodeQrImage } from "../../qrCode";
import { usePairingController } from "./usePairingController";

vi.mock("../../qrCode", () => ({
  decodeQrImage: vi.fn()
}));

function createOptions() {
  return {
    beginNewPairing: vi.fn(),
    connectManualPc: vi.fn(),
    deviceName: "Phone",
    initialPairing: null,
    message: "PC is currently not available. Retrying...",
    pairWithToken: vi.fn(),
    setIsSettingsOpen: vi.fn(),
    state: "unavailable" as const
  };
}

function qrSelection(): ChangeEvent<HTMLInputElement> {
  return {
    target: {
      files: [new File(["qr"], "pairing.png", { type: "image/png" })],
      value: "pairing.png"
    }
  } as unknown as ChangeEvent<HTMLInputElement>;
}

describe("usePairingController", () => {
  beforeEach(() => {
    vi.mocked(decodeQrImage).mockReset();
  });

  it("leaves an unavailable PC retry before confirming a newly scanned QR code", async () => {
    vi.mocked(decodeQrImage).mockResolvedValue("http://phone.local/?t=fresh-token&h=http%3A%2F%2Fpc-two.local%3A51395");
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    await act(async () => {
      await result.current.onPairingQrSelected(qrSelection());
    });

    expect(options.beginNewPairing).toHaveBeenCalledOnce();
    expect(options.setIsSettingsOpen).toHaveBeenCalledWith(false);
    expect(result.current.pendingPairing).toEqual({
      pairToken: "fresh-token",
      pcUrl: "http://pc-two.local:51395"
    });
    expect(result.current.pairingScanMessage).toBe("Confirm the device name shown on the PC, or change it before pairing.");
  });

  it("keeps the current PC retry when the selected image is not a pairing QR code", async () => {
    vi.mocked(decodeQrImage).mockResolvedValue("https://example.com/not-a-pairing-link");
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    await act(async () => {
      await result.current.onPairingQrSelected(qrSelection());
    });

    await waitFor(() => { expect(result.current.pairingScanMessage).toBe("No Voltura Air pairing link found in that QR code."); });
    expect(options.beginNewPairing).not.toHaveBeenCalled();
  });
});
