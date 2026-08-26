import { act, renderHook, waitFor } from "@testing-library/react";
import type { ChangeEvent } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { canUseLivePairingQrScanner, decodePairingQrImage } from "./pairingQrCapability";
import { usePairingController } from "./usePairingController";

vi.mock("./pairingQrCapability", () => ({
  canUseLivePairingQrScanner: vi.fn(),
  decodePairingQrImage: vi.fn(),
}));

const pairToken = "a".repeat(32);
const secondPairToken = "b".repeat(32);
const relayRoute = "r".repeat(22);

function createOptions() {
  return {
    beginNewPairing: vi.fn(),
    connectManualPc: vi.fn(),
    deviceName: "Phone",
    initialPairing: null,
    message: "PC is currently not available. Retrying...",
    pairWithToken: vi.fn(),
    setIsSettingsOpen: vi.fn(),
    state: "unavailable" as const,
  };
}

function qrSelection(
  file = new File(["qr"], "pairing.png", { type: "image/png" }),
): ChangeEvent<HTMLInputElement> {
  return {
    target: {
      files: [file],
      value: file.name,
    },
  } as unknown as ChangeEvent<HTMLInputElement>;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

function pairingUrl(token: string, host: string): string {
  return `http://phone.local:5173/pair?t=${token}&v=0.6.1&h=${encodeURIComponent(host)}`;
}

describe("usePairingController", () => {
  beforeEach(() => {
    vi.mocked(decodePairingQrImage).mockReset();
    vi.mocked(canUseLivePairingQrScanner).mockReturnValue(false);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("uses the detected device default as an empty field placeholder", () => {
    const options = { ...createOptions(), deviceName: "Mobile device" };
    const { result } = renderHook(() => usePairingController(options));

    expect(result.current.pairingDeviceName).toBe("");
    expect(result.current.pairingDeviceNamePlaceholder).toBe("Mobile device");
  });

  it.each([
    ["Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)", "iPhone"],
    ["Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)", "iPad"],
    ["Mozilla/5.0 (Linux; Android 15; Pixel 9) Mobile", "Android phone"],
    ["Mozilla/5.0 (Linux; Android 15; SM-T970)", "Android tablet"],
  ])("uses the platform suggestion for %s", (userAgent, expectedName) => {
    vi.spyOn(window.navigator, "userAgent", "get").mockReturnValue(userAgent);
    const options = { ...createOptions(), deviceName: expectedName };
    const { result } = renderHook(() => usePairingController(options));

    expect(result.current.pairingDeviceName).toBe("");
    expect(result.current.pairingDeviceNamePlaceholder).toBe(expectedName);
  });

  it("keeps a previously customized device name as the editable value", () => {
    const options = { ...createOptions(), deviceName: "Joakim's phone" };
    const { result } = renderHook(() => usePairingController(options));

    expect(result.current.pairingDeviceName).toBe("Joakim's phone");
    expect(result.current.pairingDeviceNamePlaceholder).toBe("Mobile device");
  });

  it("keeps a cleared custom name empty and pairs with the detected default", () => {
    const options = { ...createOptions(), deviceName: "Joakim's phone" };
    const { result } = renderHook(() => usePairingController(options));

    act(() => {
      result.current.connectManualHost({
        kind: "pairing",
        pairToken,
        pcUrl: "http://pc-two.local:51395",
      });
      result.current.setPairingDeviceName("");
    });

    expect(result.current.pairingDeviceName).toBe("");

    act(() => {
      result.current.confirmPendingPairing();
    });

    expect(options.pairWithToken).toHaveBeenCalledExactlyOnceWith(
      pairToken,
      "http://pc-two.local:51395",
      "Mobile device",
    );
  });

  it.each([
    ["  Replacement phone  ", "Replacement phone"],
    ["   ", "Mobile device"],
  ])("pairs with the entered name or default fallback for %j", (enteredName, expectedName) => {
    const options = { ...createOptions(), deviceName: "Mobile device" };
    const { result } = renderHook(() => usePairingController(options));

    act(() => {
      result.current.connectManualHost({
        kind: "pairing",
        pairToken,
        pcUrl: "http://pc-two.local:51395",
      });
      result.current.setPairingDeviceName(enteredName);
    });
    act(() => {
      result.current.confirmPendingPairing();
    });

    expect(options.pairWithToken).toHaveBeenCalledExactlyOnceWith(
      pairToken,
      "http://pc-two.local:51395",
      expectedName,
    );
  });

  it("keeps connection progress out of the QR scan guidance", () => {
    const options = {
      ...createOptions(),
      message: "Connecting to PC...",
      state: "paired" as const,
    };

    const { result } = renderHook(() => usePairingController(options));

    expect(result.current.pairingScanMessage).toBe(
      "Scan the QR code shown on your PC to pair this device.",
    );
    expect(result.current.pairingStatusMessage).toBe("Connecting to PC...");
  });

  it("uses live scanning as the HTTPS-capable default and falls back to the photo input for the page session", () => {
    vi.mocked(canUseLivePairingQrScanner).mockReturnValue(true);
    const inputClick = vi.fn();
    const { result } = renderHook(() => usePairingController(createOptions()));
    result.current.pairingQrInputRef.current = { click: inputClick } as unknown as HTMLInputElement;

    act(() => {
      result.current.scanPairingQr();
    });
    const attemptId = result.current.livePairingScannerAttempt;
    expect(attemptId).toBeTypeOf("number");
    expect(result.current.usesLivePairingQr).toBe(true);
    expect(inputClick).not.toHaveBeenCalled();

    act(() => {
      result.current.fallbackFromLivePairingQr(attemptId!, "Camera access was not allowed.", false);
    });
    expect(result.current.livePairingScannerAttempt).toBeNull();
    expect(result.current.usesLivePairingQr).toBe(false);

    act(() => {
      result.current.scanPairingQr();
    });
    expect(inputClick).toHaveBeenCalledOnce();
  });

  it.each([
    ["Direct", pairingUrl(pairToken, "http://pc-live.local:51395"), "http://pc-live.local:51395"],
    [
      "Relay",
      `https://voltura.se/a/${relayRoute}?v=1.0.0#${pairToken}`,
      `https://voltura.se/a/${relayRoute}`,
    ],
    [
      "Secure Direct",
      `https://voltura.se/s/${relayRoute}?v=1.0.0#${pairToken}`,
      `https://voltura.se/s/${relayRoute}`,
    ],
  ])(
    "routes a %s live QR result through the existing parser and confirmation",
    (_mode, scannedText, pcUrl) => {
      vi.mocked(canUseLivePairingQrScanner).mockReturnValue(true);
      const options = createOptions();
      const { result } = renderHook(() => usePairingController(options));

      act(() => {
        result.current.scanPairingQr();
      });
      const attemptId = result.current.livePairingScannerAttempt!;
      let accepted = false;
      act(() => {
        accepted = result.current.acceptLivePairingQr(attemptId, scannedText);
      });

      expect(accepted).toBe(true);
      expect(options.beginNewPairing).toHaveBeenCalledOnce();
      expect(result.current.pendingPairing).toEqual({ pairToken, pcUrl });
      expect(result.current.livePairingScannerAttempt).toBeNull();
    },
  );

  it("keeps live scanning open for a non-Voltura QR code", () => {
    vi.mocked(canUseLivePairingQrScanner).mockReturnValue(true);
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    act(() => {
      result.current.scanPairingQr();
    });
    const attemptId = result.current.livePairingScannerAttempt!;
    let accepted = true;
    act(() => {
      accepted = result.current.acceptLivePairingQr(attemptId, "https://example.com/");
    });

    expect(accepted).toBe(false);
    expect(result.current.livePairingScannerAttempt).toBe(attemptId);
    expect(result.current.pairingScanMessage).toBe(
      "No Voltura Air pairing link found in that QR code.",
    );
    expect(options.beginNewPairing).not.toHaveBeenCalled();
  });

  it("reports a bounded reading state until the selected image finishes", async () => {
    const pending = deferred<string>();
    vi.mocked(decodePairingQrImage).mockReturnValueOnce(pending.promise);
    const { result } = renderHook(() => usePairingController(createOptions()));

    let scan!: Promise<void>;
    act(() => {
      scan = result.current.onPairingQrSelected(qrSelection());
    });
    expect(result.current.isPairingQrReading).toBe(true);
    expect(result.current.pairingScanMessage).toBe("Reading QR code...");

    await act(async () => {
      pending.resolve(pairingUrl(pairToken, "http://pc.local:51395"));
      await scan;
    });
    expect(result.current.isPairingQrReading).toBe(false);
  });

  it("leaves an unavailable PC retry before confirming a newly scanned QR code", async () => {
    vi.mocked(decodePairingQrImage).mockResolvedValue(
      `http://phone.local:5173/pair?t=${pairToken}&v=0.6.1&h=http%3A%2F%2Fpc-two.local%3A51395`,
    );
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    await act(async () => {
      await result.current.onPairingQrSelected(qrSelection());
    });

    expect(options.beginNewPairing).toHaveBeenCalledOnce();
    expect(options.setIsSettingsOpen).toHaveBeenCalledWith(false);
    expect(result.current.pendingPairing).toEqual({
      pairToken,
      pcUrl: "http://pc-two.local:51395",
    });
    expect(result.current.pairingScanMessage).toBe(
      "Confirm the device name shown on the PC, or change it before pairing.",
    );
  });

  it("keeps the current PC retry when the selected image is not a pairing QR code", async () => {
    vi.mocked(decodePairingQrImage).mockResolvedValue("https://example.com/not-a-pairing-link");
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    await act(async () => {
      await result.current.onPairingQrSelected(qrSelection());
    });

    await waitFor(() => {
      expect(result.current.pairingScanMessage).toBe(
        "No Voltura Air pairing link found in that QR code.",
      );
    });
    expect(options.beginNewPairing).not.toHaveBeenCalled();
  });

  it("routes a manually entered pairing link through pairing confirmation", () => {
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    act(() => {
      result.current.connectManualHost({
        kind: "pairing",
        pairToken,
        pcUrl: "http://pc-two.local:51395",
      });
    });

    expect(options.beginNewPairing).toHaveBeenCalledOnce();
    expect(options.connectManualPc).not.toHaveBeenCalled();
    expect(options.setIsSettingsOpen).toHaveBeenCalledWith(false);
    expect(result.current.pendingPairing).toEqual({
      pairToken,
      pcUrl: "http://pc-two.local:51395",
    });
    expect(result.current.pairingScanMessage).toBe(
      "Confirm the device name shown on the PC, or change it before pairing.",
    );
  });

  it("routes a validated manual host through connection recovery", () => {
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    act(() => {
      result.current.connectManualHost({ kind: "host", pcUrl: "http://pc-two.local:51395" });
    });

    expect(options.connectManualPc).toHaveBeenCalledExactlyOnceWith("http://pc-two.local:51395");
    expect(options.beginNewPairing).not.toHaveBeenCalled();
    expect(result.current.pendingPairing).toBeNull();
    expect(result.current.pairingScanMessage).toBe("Connecting to manually entered PC...");
  });

  it("keeps a faster newer QR result authoritative over an older scan", async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    vi.mocked(decodePairingQrImage)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    let firstScan!: Promise<void>;
    let secondScan!: Promise<void>;
    act(() => {
      firstScan = result.current.onPairingQrSelected(qrSelection());
      secondScan = result.current.onPairingQrSelected(qrSelection());
    });
    await act(async () => {
      second.resolve(pairingUrl(secondPairToken, "http://pc-new.local:51395"));
      await secondScan;
    });
    await act(async () => {
      first.resolve(pairingUrl(pairToken, "http://pc-old.local:51395"));
      await firstScan;
    });

    expect(options.beginNewPairing).toHaveBeenCalledOnce();
    expect(options.setIsSettingsOpen).toHaveBeenCalledExactlyOnceWith(false);
    expect(result.current.pendingPairing).toEqual({
      pairToken: secondPairToken,
      pcUrl: "http://pc-new.local:51395",
    });
  });

  it("ignores an older decode error after a newer scan succeeds", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {
      /* Expected boundary is asserted below. */
    });
    const first = deferred<string>();
    vi.mocked(decodePairingQrImage)
      .mockReturnValueOnce(first.promise)
      .mockResolvedValueOnce(pairingUrl(secondPairToken, "http://pc-new.local:51395"));
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    let firstScan!: Promise<void>;
    act(() => {
      firstScan = result.current.onPairingQrSelected(qrSelection());
    });
    await act(async () => result.current.onPairingQrSelected(qrSelection()));
    await act(async () => {
      first.reject(new Error("stale decode"));
      await firstScan;
    });

    expect(result.current.pendingPairing?.pairToken).toBe(secondPairToken);
    expect(result.current.pairingScanMessage).toContain("Confirm the device name");
    expect(consoleError).not.toHaveBeenCalled();
    consoleError.mockRestore();
  });

  it("keeps the latest failure when an older scan later succeeds", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {
      /* Expected current failure. */
    });
    const first = deferred<string>();
    vi.mocked(decodePairingQrImage)
      .mockReturnValueOnce(first.promise)
      .mockRejectedValueOnce(new Error("current decode"));
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));

    let firstScan!: Promise<void>;
    act(() => {
      firstScan = result.current.onPairingQrSelected(qrSelection());
    });
    await act(async () => result.current.onPairingQrSelected(qrSelection()));
    await act(async () => {
      first.resolve(pairingUrl(pairToken, "http://pc-old.local:51395"));
      await firstScan;
    });

    expect(options.beginNewPairing).not.toHaveBeenCalled();
    expect(result.current.pendingPairing).toBeNull();
    expect(result.current.pairingScanMessage).toContain("Could not read the QR code");
    expect(consoleError).toHaveBeenCalledOnce();
    consoleError.mockRestore();
  });

  it.each(["success", "failure"] as const)(
    "invalidates a pending scan on unmount before %s",
    async (outcome) => {
      const consoleError = vi.spyOn(console, "error").mockImplementation(() => {
        /* Stale work must stay silent. */
      });
      const pending = deferred<string>();
      vi.mocked(decodePairingQrImage).mockReturnValueOnce(pending.promise);
      const options = createOptions();
      const { result, unmount } = renderHook(() => usePairingController(options));
      let scan!: Promise<void>;
      act(() => {
        scan = result.current.onPairingQrSelected(qrSelection());
      });

      unmount();
      if (outcome === "success") {
        pending.resolve(pairingUrl(pairToken, "http://pc.local:51395"));
      } else {
        pending.reject(new Error("unmounted"));
      }
      await scan;

      expect(options.beginNewPairing).not.toHaveBeenCalled();
      expect(options.setIsSettingsOpen).not.toHaveBeenCalled();
      expect(consoleError).not.toHaveBeenCalled();
      consoleError.mockRestore();
    },
  );

  it("treats repeated selection of the same file as a newer generation", async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    vi.mocked(decodePairingQrImage)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);
    const options = createOptions();
    const { result } = renderHook(() => usePairingController(options));
    const file = new File(["same"], "same.png", { type: "image/png" });
    const firstEvent = qrSelection(file);
    const secondEvent = qrSelection(file);

    let firstScan!: Promise<void>;
    let secondScan!: Promise<void>;
    act(() => {
      firstScan = result.current.onPairingQrSelected(firstEvent);
      secondScan = result.current.onPairingQrSelected(secondEvent);
    });
    expect(firstEvent.target.value).toBe("");
    expect(secondEvent.target.value).toBe("");
    await act(async () => {
      second.resolve(pairingUrl(secondPairToken, "http://pc-new.local:51395"));
      await secondScan;
      first.resolve(pairingUrl(pairToken, "http://pc-old.local:51395"));
      await firstScan;
    });

    expect(decodePairingQrImage).toHaveBeenCalledTimes(2);
    expect(result.current.pendingPairing?.pairToken).toBe(secondPairToken);
  });
});
