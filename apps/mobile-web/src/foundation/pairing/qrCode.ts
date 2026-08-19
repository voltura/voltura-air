export interface QrDecoderSession {
  decode: (imageData: ImageData, inversionAttempts?: "dontInvert" | "onlyInvert") => Promise<string | null>;
  dispose: () => void;
}

interface QrDecodeResponse {
  id: number;
  data?: string;
  error?: string;
}

export function createQrDecoderSession(): QrDecoderSession {
  const worker = new Worker(new URL("./qrDecoder.worker.ts", import.meta.url), { type: "module" });
  let nextId = 0;
  let pending: { id: number; reject: (error: Error) => void; resolve: (data: string | null) => void } | null = null;
  let disposed = false;

  worker.onmessage = (event: MessageEvent<QrDecodeResponse>) => {
    if (pending?.id !== event.data.id) {
      return;
    }

    const current = pending;
    pending = null;
    if (event.data.error) {
      current.reject(new Error(event.data.error));
    } else {
      current.resolve(event.data.data ?? null);
    }
  };
  worker.onerror = () => {
    const current = pending;
    pending = null;
    current?.reject(new Error("QR decoder worker failed."));
  };

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
        pending = { id, reject, resolve };
        worker.postMessage({
          id,
          height: imageData.height,
          inversionAttempts,
          pixels: pixels.buffer,
          width: imageData.width
        }, [pixels.buffer]);
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
      current?.reject(new Error("QR decoder was cancelled."));
    }
  };
}

export async function decodeQrImage(file: File): Promise<string> {
  const decoder = createQrDecoderSession();
  let imageUrl: string | null = null;
  try {
    imageUrl = URL.createObjectURL(file);
    const image = await loadImage(imageUrl);
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

function loadImage(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => { resolve(image); };
    image.onerror = () => { reject(new Error("Image failed to load")); };
    image.crossOrigin = "anonymous";
    image.src = source;
  });
}
