import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createQrDecoderSession } from "../../foundation/pairing/qrCode";
import { PairingQrScannerDialog } from "./PairingQrScannerDialog";
import { canUseLivePairingQrScanner } from "./pairingQrCapability";

vi.mock("../../foundation/pairing/qrCode", () => ({
  createQrDecoderSession: vi.fn(),
}));

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

function fakeStream() {
  const listeners = new Map<string, EventListener>();
  const track = {
    addEventListener: vi.fn((type: string, listener: EventListener) => {
      listeners.set(type, listener);
    }),
    removeEventListener: vi.fn((type: string) => {
      listeners.delete(type);
    }),
    stop: vi.fn(),
  } as unknown as MediaStreamTrack;
  const stream = {
    getTracks: () => [track],
    getVideoTracks: () => [track],
  } as unknown as MediaStream;
  return { listeners, stream, track };
}

describe("PairingQrScannerDialog", () => {
  beforeEach(() => {
    vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
    vi.mocked(createQrDecoderSession).mockReturnValue({
      decode: vi.fn(() => new Promise<string | null>(() => undefined)),
      dispose: vi.fn(),
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("requires HTTPS and the camera API without identifying the browser", () => {
    const mediaDevices = { getUserMedia: vi.fn() } as unknown as MediaDevices;
    expect(canUseLivePairingQrScanner("https:", mediaDevices)).toBe(true);
    expect(canUseLivePairingQrScanner("http:", mediaDevices)).toBe(false);
    expect(canUseLivePairingQrScanner("https:", undefined as unknown as MediaDevices)).toBe(false);
  });

  it("does not request the camera when opened while the document is already hidden", async () => {
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
    const getUserMedia = vi.fn();
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    const fallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={30} onAccept={vi.fn()} onFallback={fallback} />);
    await waitFor(() => expect(fallback).toHaveBeenCalledOnce());
    expect(getUserMedia).not.toHaveBeenCalled();
  });

  it("releases an already-ended camera track instead of waiting for frames forever", async () => {
    const owned = fakeStream();
    Object.defineProperty(owned.track, "readyState", { value: "ended" });
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: () => Promise.resolve(owned.stream) },
    });
    const fallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={31} onAccept={vi.fn()} onFallback={fallback} />);
    await waitFor(() => expect(fallback).toHaveBeenCalledOnce());
    expect(owned.track.stop).toHaveBeenCalledOnce();
    expect(createQrDecoderSession).not.toHaveBeenCalled();
  });

  it("requests only the preferred rear video camera and stops a late grant after unmount", async () => {
    const camera = deferred<MediaStream>();
    const getUserMedia = vi.fn(() => camera.promise);
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    const owned = fakeStream();
    const view = render(
      <PairingQrScannerDialog attemptId={7} onAccept={vi.fn()} onFallback={vi.fn()} />,
    );

    expect(getUserMedia).toHaveBeenCalledExactlyOnceWith({
      audio: false,
      video: { facingMode: { ideal: "environment" } },
    });
    view.unmount();
    camera.resolve(owned.stream);

    await waitFor(() => {
      expect(owned.track.stop).toHaveBeenCalledOnce();
    });
    expect(createQrDecoderSession).not.toHaveBeenCalled();
  });

  it("turns permission rejection into photo fallback without opening the picker", async () => {
    const getUserMedia = vi.fn(() => Promise.reject(new DOMException("Denied", "NotAllowedError")));
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    const onFallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={9} onAccept={vi.fn()} onFallback={onFallback} />);

    await waitFor(() => {
      expect(onFallback).toHaveBeenCalledExactlyOnceWith(
        9,
        "Camera access was not allowed. Take a photo of the QR code instead.",
        false,
      );
    });
  });

  it("contains synchronous camera API failures and offers photo fallback", async () => {
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: {
        getUserMedia: () => {
          throw new Error("camera unavailable");
        },
      },
    });
    const onFallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={20} onAccept={vi.fn()} onFallback={onFallback} />);
    await waitFor(() => expect(onFallback).toHaveBeenCalledOnce());
    expect(onFallback).toHaveBeenCalledWith(20, expect.stringContaining("Camera access"), false);
  });

  it("does not schedule decoding after a canceled video play resolves late", async () => {
    const owned = fakeStream();
    const playback = deferred<void>();
    vi.mocked(HTMLMediaElement.prototype.play).mockReturnValue(playback.promise);
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: () => Promise.resolve(owned.stream) },
    });
    const view = render(
      <PairingQrScannerDialog attemptId={21} onAccept={vi.fn()} onFallback={vi.fn()} />,
    );
    await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalledOnce());
    view.unmount();
    const timeout = vi.spyOn(window, "setTimeout");
    await act(async () => {
      playback.resolve();
      await playback.promise;
    });
    expect(timeout).not.toHaveBeenCalled();
    expect(owned.track.stop).toHaveBeenCalledOnce();
  });

  it("resets camera readiness and permission feedback for a replacement attempt", async () => {
    const owned = fakeStream();
    const nextCamera = deferred<MediaStream>();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(owned.stream)
      .mockReturnValue(nextCamera.promise);
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    const view = render(
      <PairingQrScannerDialog attemptId={22} onAccept={vi.fn()} onFallback={vi.fn()} />,
    );
    await waitFor(() =>
      expect(screen.getByRole("status").textContent).toContain("Point the camera"),
    );
    view.rerender(
      <PairingQrScannerDialog attemptId={23} onAccept={vi.fn()} onFallback={vi.fn()} />,
    );
    expect(screen.getByRole("status").textContent).toBe("Waiting for camera permission…");
    expect(document.querySelector(".pairing-qr-waiting")).not.toBeNull();
  });

  it("decodes one centered frame at a time and releases the complete attempt after acceptance", async () => {
    const owned = fakeStream();
    const getUserMedia = vi.fn(() => Promise.resolve(owned.stream));
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", {
      configurable: true,
      get: () => 1280,
    });
    Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", {
      configurable: true,
      get: () => 720,
    });
    const getImageData = vi.fn(
      () =>
        ({
          data: new Uint8ClampedArray(640 * 640 * 4),
          height: 640,
          width: 640,
        }) as ImageData,
    );
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      drawImage: vi.fn(),
      getImageData,
    } as unknown as CanvasRenderingContext2D);
    const dispose = vi.fn();
    const decode = vi.fn(() => Promise.resolve("https://voltura.se/a/route?v=1.0.0#token"));
    vi.mocked(createQrDecoderSession).mockReturnValue({ decode, dispose });
    const onAccept = vi.fn(() => true);

    render(<PairingQrScannerDialog attemptId={10} onAccept={onAccept} onFallback={vi.fn()} />);

    await waitFor(() => {
      expect(onAccept).toHaveBeenCalledOnce();
    });
    expect(decode).toHaveBeenCalledOnce();
    expect(getImageData).toHaveBeenCalledExactlyOnceWith(0, 0, 640, 640);
    expect(dispose).toHaveBeenCalledOnce();
    expect(owned.track.stop).toHaveBeenCalledOnce();
  });

  it("offers explicit cancellation and same-gesture photo fallback", () => {
    const getUserMedia = vi.fn(() => new Promise<MediaStream>(() => undefined));
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia },
    });
    const onFallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={11} onAccept={vi.fn()} onFallback={onFallback} />);

    fireEvent.click(screen.getByRole("button", { name: "Take photo instead" }));
    expect(onFallback).toHaveBeenLastCalledWith(
      11,
      "Take a clear photo of the QR code shown on the PC.",
      true,
    );

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onFallback).toHaveBeenLastCalledWith(
      11,
      "Live scanning was cancelled. Take a photo of the QR code instead.",
      false,
    );
  });
});
