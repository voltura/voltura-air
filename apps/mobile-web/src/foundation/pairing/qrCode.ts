import type jsQrDecoder from "jsqr";

type QrDecoder = typeof jsQrDecoder;

let qrDecoderPromise: Promise<QrDecoder> | null = null;

export async function decodeQrImage(file: File): Promise<string> {
  const imageUrl = URL.createObjectURL(file);
  try {
    const jsQR = await loadQrDecoder();
    const image = await loadImage(imageUrl);
    const imageData = drawImageToCanvas(image, 1600);
    const code = scanImageData(imageData, jsQR, "dontInvert");
    if (code?.data) {
      return code.data;
    }

    const centerCode = scanCenterCrop(imageData, jsQR);
    if (centerCode?.data) {
      return centerCode.data;
    }

    const invertedCode = scanImageData(imageData, jsQR, "onlyInvert");
    if (invertedCode?.data) {
      return invertedCode.data;
    }

    throw new Error("QR code not found in image");
  } catch (error) {
    if (error instanceof Error) {
      throw error;
    }

    throw new Error(`Failed to decode QR code: ${String(error)}`, { cause: error });
  } finally {
    URL.revokeObjectURL(imageUrl);
  }
}

function loadQrDecoder(): Promise<QrDecoder> {
  qrDecoderPromise ??= import("jsqr").then((module) => module.default);
  return qrDecoderPromise;
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

function scanImageData(imageData: ImageData, jsQR: QrDecoder, inversionAttempts: "dontInvert" | "onlyInvert") {
  return jsQR(imageData.data, imageData.width, imageData.height, { inversionAttempts });
}

function scanCenterCrop(imageData: ImageData, jsQR: QrDecoder) {
  const centerCrop = cropCenter(imageData, 0.8);
  return scanImageData(centerCrop, jsQR, "dontInvert");
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
