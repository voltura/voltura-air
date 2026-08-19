import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createQrDecoderSession } from "../../foundation/pairing/qrCode";
import { PairingQrScannerDialog } from "./PairingQrScannerDialog";
import { canUseLivePairingQrScanner } from "./pairingQrCapability";

vi.mock("../../foundation/pairing/qrCode", () => ({
  createQrDecoderSession: vi.fn()
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
    addEventListener: vi.fn((type: string, listener: EventListener) => { listeners.set(type, listener); }),
    removeEventListener: vi.fn((type: string) => { listeners.delete(type); }),
    stop: vi.fn()
  } as unknown as MediaStreamTrack;
  const stream = {
    getTracks: () => [track],
    getVideoTracks: () => [track]
  } as unknown as MediaStream;
  return { listeners, stream, track };
}

describe("PairingQrScannerDialog", () => {
  beforeEach(() => {
    vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
    vi.mocked(createQrDecoderSession).mockReturnValue({
      decode: vi.fn(() => new Promise<string | null>(() => undefined)),
      dispose: vi.fn()
    });
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it("requires HTTPS and the camera API without identifying the browser", () => {
    const mediaDevices = { getUserMedia: vi.fn() } as unknown as MediaDevices;
    expect(canUseLivePairingQrScanner("https:", mediaDevices)).toBe(true);
    expect(canUseLivePairingQrScanner("http:", mediaDevices)).toBe(false);
    expect(canUseLivePairingQrScanner("https:", undefined as unknown as MediaDevices)).toBe(false);
  });

  it("requests only the preferred rear video camera and stops a late grant after unmount", async () => {
    const camera = deferred<MediaStream>();
    const getUserMedia = vi.fn(() => camera.promise);
    Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
    const owned = fakeStream();
    const view = render(
      <PairingQrScannerDialog attemptId={7} onAccept={vi.fn()} onFallback={vi.fn()} />
    );

    expect(getUserMedia).toHaveBeenCalledExactlyOnceWith({
      audio: false,
      video: { facingMode: { ideal: "environment" } }
    });
    view.unmount();
    camera.resolve(owned.stream);

    await waitFor(() => { expect(owned.track.stop).toHaveBeenCalledOnce(); });
    expect(createQrDecoderSession).not.toHaveBeenCalled();
  });

  it("turns permission rejection into photo fallback without opening the picker", async () => {
    const getUserMedia = vi.fn(() => Promise.reject(new DOMException("Denied", "NotAllowedError")));
    Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
    const onFallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={9} onAccept={vi.fn()} onFallback={onFallback} />);

    await waitFor(() => {
      expect(onFallback).toHaveBeenCalledExactlyOnceWith(
        9,
        "Camera access was not allowed. Take a photo of the QR code instead.",
        false
      );
    });
  });

  it("decodes one centered frame at a time and releases the complete attempt after acceptance", async () => {
    const owned = fakeStream();
    const getUserMedia = vi.fn(() => Promise.resolve(owned.stream));
    Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
    Object.defineProperty(HTMLVideoElement.prototype, "videoWidth", { configurable: true, get: () => 1280 });
    Object.defineProperty(HTMLVideoElement.prototype, "videoHeight", { configurable: true, get: () => 720 });
    const getImageData = vi.fn(() => ({
      data: new Uint8ClampedArray(640 * 640 * 4),
      height: 640,
      width: 640
    } as ImageData));
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      drawImage: vi.fn(),
      getImageData
    } as unknown as CanvasRenderingContext2D);
    const dispose = vi.fn();
    const decode = vi.fn(() => Promise.resolve("https://voltura.se/a/route?v=1.0.0#token"));
    vi.mocked(createQrDecoderSession).mockReturnValue({ decode, dispose });
    const onAccept = vi.fn(() => true);

    render(<PairingQrScannerDialog attemptId={10} onAccept={onAccept} onFallback={vi.fn()} />);

    await waitFor(() => { expect(onAccept).toHaveBeenCalledOnce(); });
    expect(decode).toHaveBeenCalledOnce();
    expect(getImageData).toHaveBeenCalledExactlyOnceWith(0, 0, 640, 640);
    expect(dispose).toHaveBeenCalledOnce();
    expect(owned.track.stop).toHaveBeenCalledOnce();
  });

  it("offers explicit cancellation and same-gesture photo fallback", () => {
    const getUserMedia = vi.fn(() => new Promise<MediaStream>(() => undefined));
    Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
    const onFallback = vi.fn();
    render(<PairingQrScannerDialog attemptId={11} onAccept={vi.fn()} onFallback={onFallback} />);

    fireEvent.click(screen.getByRole("button", { name: "Take photo instead" }));
    expect(onFallback).toHaveBeenLastCalledWith(11, "Take a clear photo of the QR code shown on the PC.", true);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onFallback).toHaveBeenLastCalledWith(11, "Live scanning was cancelled. Take a photo of the QR code instead.", false);
  });
});
