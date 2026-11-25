export interface QrDecoderSession {
  decode: (
    imageData: ImageData,
    inversionAttempts?: "dontInvert" | "onlyInvert",
  ) => Promise<string | null>;
  dispose: () => void;
}

interface QrDecodeResponse {
  id: number;
  data?: string;
  error?: string;
}

const frameDecodeTimeoutMs = 5000;
const imageLoadTimeoutMs = 10_000;

export function createQrDecoderSession(): QrDecoderSession {
  const worker = new Worker(new URL("./qrDecoder.worker.ts", import.meta.url), { type: "module" });
  let nextId = 0;
  let pending: {
    id: number;
    timeout: number;
    reject: (error: Error) => void;
    resolve: (data: string | null) => void;
  } | null = null;
  let disposed = false;

  worker.onmessage = (event: MessageEvent<QrDecodeResponse>) => {
    if (pending?.id !== event.data.id) {
      return;
    }

    const current = pending;
    pending = null;
    window.clearTimeout(current.timeout);
    if (event.data.error) {
      current.reject(new Error(event.data.error));
    } else {
      current.resolve(event.data.data ?? null);
    }
  };
  const failWorker = () => {
    if (disposed) {
      return;
    }
    disposed = true;
    worker.terminate();
    const current = pending;
    pending = null;
    window.clearTimeout(current?.timeout);
    current?.reject(new Error("QR decoder worker failed."));
  };
  worker.onerror = failWorker;
  worker.onmessageerror = failWorker;

  return {
    decode(imageData, inversionAttempts = "dontInvert") {
      if (disposed) {
        return Promise.reject(new Error("QR decoder is disposed."));
      }
      if (pending) {
        return Promise.reject(new Error("QR decoder is busy."));
      }

      const id = ++nextId;
      const pixels = new Uint8ClampedArray(imageData.data);
      return new Promise<string | null>((resolve, reject) => {
        const timeout = window.setTimeout(failWorker, frameDecodeTimeoutMs);
        pending = { id, reject, resolve, timeout };
        try {
          worker.postMessage(
            {
              id,
              height: imageData.height,
              inversionAttempts,
              pixels: pixels.buffer,
              width: imageData.width,
            },
            [pixels.buffer],
          );
        } catch (error) {
          pending = null;
          window.clearTimeout(timeout);
          reject(error instanceof Error ? error : new Error("QR decoder request failed."));
        }
      });
    },
    dispose() {
      if (disposed) {
        return;
      }
      disposed = true;
      worker.terminate();
      const current = pending;
      pending = null;
      window.clearTimeout(current?.timeout);
      current?.reject(new Error("QR decoder was cancelled."));
    },
  };
}

export async function decodeQrImage(file: File, signal?: AbortSignal): Promise<string> {
  signal?.throwIfAborted();
  const decoder = createQrDecoderSession();
  const cancelDecode = () => decoder.dispose();
  signal?.addEventListener("abort", cancelDecode, { once: true });
  let imageUrl: string | null = null;
  try {
    imageUrl = URL.createObjectURL(file);
    const image = await loadImage(imageUrl, signal);
    signal?.throwIfAborted();
    const imageData = drawImageToCanvas(image, 1600);
    const code = await decoder.decode(imageData);
    if (code) {
      return code;
    }

    const centerCode = await decoder.decode(cropCenter(imageData, 0.8));
    if (centerCode) {
      return centerCode;
    }

    const invertedCode = await decoder.decode(imageData, "onlyInvert");
    if (invertedCode) {
      return invertedCode;
    }

    throw new Error("QR code not found in image");
  } catch (error) {
    if (error instanceof Error) {
      throw error;
    }

    throw new Error(`Failed to decode QR code: ${String(error)}`, { cause: error });
  } finally {
    signal?.removeEventListener("abort", cancelDecode);
    decoder.dispose();
    if (imageUrl) {
      URL.revokeObjectURL(imageUrl);
    }
  }
}

function drawImageToCanvas(image: HTMLImageElement, maxDimension: number): ImageData {
  const scale = Math.min(1, maxDimension / Math.max(image.naturalWidth, image.naturalHeight));
  const width = Math.max(1, Math.floor(image.naturalWidth * scale));
  const height = Math.max(1, Math.floor(image.naturalHeight * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("Canvas unavailable");
  }

  context.drawImage(image, 0, 0, width, height);
  return context.getImageData(0, 0, width, height);
}

function cropCenter(imageData: ImageData, ratio: number): ImageData {
  const width = Math.max(1, Math.floor(imageData.width * ratio));
  const height = Math.max(1, Math.floor(imageData.height * ratio));
  const offsetX = Math.floor((imageData.width - width) / 2);
  const offsetY = Math.floor((imageData.height - height) / 2);
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("Canvas unavailable");
  }

  context.putImageData(imageData, -offsetX, -offsetY);
  return context.getImageData(0, 0, width, height);
}

function loadImage(source: string, signal?: AbortSignal): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    const timeout = window.setTimeout(() => {
      release();
      image.removeAttribute("src");
      reject(new Error("QR image loading timed out."));
    }, imageLoadTimeoutMs);
    const release = () => {
      window.clearTimeout(timeout);
      image.onload = null;
      image.onerror = null;
      signal?.removeEventListener("abort", abort);
    };
    const abort = () => {
      release();
      image.removeAttribute("src");
      reject(new DOMException("QR image reading was cancelled.", "AbortError"));
    };
    image.onload = () => {
      release();
      resolve(image);
    };
    image.onerror = () => {
      release();
      reject(new Error("Image failed to load"));
    };
    signal?.addEventListener("abort", abort, { once: true });
    image.crossOrigin = "anonymous";
    try {
      image.src = source;
    } catch (error) {
      release();
      reject(error instanceof Error ? error : new Error("QR image could not start loading."));
    }
  });
}
