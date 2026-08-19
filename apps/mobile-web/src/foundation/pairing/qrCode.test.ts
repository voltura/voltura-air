import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createQrDecoderSession, decodeQrImage } from "./qrCode";

interface PostedRequest {
  id: number;
  height: number;
  inversionAttempts: "dontInvert" | "onlyInvert";
  pixels: ArrayBuffer;
  width: number;
}

class FakeWorker {
  static instances: FakeWorker[] = [];
  onerror: ((event: Event) => void) | null = null;
  onmessage: ((event: MessageEvent<{ id: number; data?: string }>) => void) | null = null;
  posted: PostedRequest[] = [];
  responses: (string | null)[] = [];
  terminated = false;

  constructor() {
    FakeWorker.instances.push(this);
  }

  postMessage(request: PostedRequest) {
    this.posted.push(request);
    if (this.responses.length > 0) {
      const data = this.responses.shift();
      queueMicrotask(() => { this.onmessage?.(new MessageEvent("message", { data: { id: request.id, ...(data ? { data } : {}) } })); });
    }
  }

  terminate() {
    this.terminated = true;
  }
}

describe("QR decoder worker boundary", () => {
  beforeEach(() => {
    FakeWorker.instances = [];
    vi.stubGlobal("Worker", FakeWorker);
  });

  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  it("allows one decode at a time and transfers bounded pixel work to the worker", async () => {
    const session = createQrDecoderSession();
    const worker = FakeWorker.instances[0]!;
    const imageData = { data: new Uint8ClampedArray(16), height: 2, width: 2 } as ImageData;
    const first = session.decode(imageData);

    await expect(session.decode(imageData)).rejects.toThrow("QR decoder is busy.");
    expect(worker.posted[0]).toMatchObject({ height: 2, inversionAttempts: "dontInvert", width: 2 });

    worker.onmessage?.(new MessageEvent("message", { data: { id: worker.posted[0]!.id, data: "pairing-link" } }));
    await expect(first).resolves.toBe("pairing-link");
    session.dispose();
    expect(worker.terminated).toBe(true);
  });

  it("rejects pending work when its attempt owner disposes the worker", async () => {
    const session = createQrDecoderSession();
    const pending = session.decode({ data: new Uint8ClampedArray(4), height: 1, width: 1 } as ImageData);

    session.dispose();

    await expect(pending).rejects.toThrow("QR decoder was cancelled.");
  });

  it("preserves normal, center-crop, and inverted photo attempts through one worker", async () => {
    const createObjectUrl = vi.fn(() => "blob:qr");
    const revokeObjectUrl = vi.fn();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl });
    class LoadedImage {
      crossOrigin = "";
      naturalHeight = 1000;
      naturalWidth = 1000;
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      set src(_value: string) { queueMicrotask(() => { this.onload?.(); }); }
    }
    vi.stubGlobal("Image", LoadedImage);
    const imageData = { data: new Uint8ClampedArray(16), height: 2, width: 2 } as ImageData;
    const context = {
      drawImage: vi.fn(),
      getImageData: vi.fn(() => imageData),
      putImageData: vi.fn()
    };
    vi.spyOn(document, "createElement").mockImplementation((tagName) => {
      if (tagName === "canvas") {
        return { getContext: () => context, height: 0, width: 0 } as unknown as HTMLCanvasElement;
      }
      throw new Error(`Unexpected element: ${tagName}`);
    });

    const decoding = decodeQrImage(new File(["qr"], "pairing.png", { type: "image/png" }));
    const worker = FakeWorker.instances[0]!;
    worker.responses.push(null, null, "inverted-pairing-link");

    await expect(decoding).resolves.toBe("inverted-pairing-link");
    expect(worker.posted.map((request) => request.inversionAttempts)).toEqual([
      "dontInvert",
      "dontInvert",
      "onlyInvert"
    ]);
    expect(worker.terminated).toBe(true);
    expect(revokeObjectUrl).toHaveBeenCalledExactlyOnceWith("blob:qr");
  });
});
