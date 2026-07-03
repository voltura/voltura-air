import jsQR from "jsqr";

export async function decodeQrImage(file: File): Promise<string> {
  const imageUrl = URL.createObjectURL(file);
  try {
    const image = await loadImage(imageUrl);
    const imageData = drawImageToCanvas(image, 2048);
    const code = scanImageData(imageData);
    if (code?.data) {
      return code.data;
    }

    const smallerImageData = drawImageToCanvas(image, 1024);
    const smallerCode = scanImageData(smallerImageData) ?? scanRotatedImageData(smallerImageData);
    if (smallerCode?.data) {
      return smallerCode.data;
    }

    const centerCode = scanCenterCrop(imageData);
    if (centerCode?.data) {
      return centerCode.data;
    }

    throw new Error("QR code not found in image");
  } catch (error) {
    if (error instanceof Error) {
      throw error;
    }

    throw new Error(`Failed to decode QR code: ${String(error)}`);
  } finally {
    URL.revokeObjectURL(imageUrl);
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

function scanImageData(imageData: ImageData) {
  return jsQR(imageData.data, imageData.width, imageData.height);
}

function scanRotatedImageData(imageData: ImageData) {
  const rotated90 = rotateImageData(imageData, 90);
  const code90 = scanImageData(rotated90);
  if (code90?.data) {
    return code90;
  }

  const rotated180 = rotateImageData(imageData, 180);
  const code180 = scanImageData(rotated180);
  if (code180?.data) {
    return code180;
  }

  return scanImageData(rotateImageData(imageData, 270));
}

function scanCenterCrop(imageData: ImageData) {
  const centerCrop = cropCenter(imageData, 0.8);
  return scanImageData(centerCrop) ?? scanRotatedImageData(centerCrop);
}

function rotateImageData(imageData: ImageData, degrees: 90 | 180 | 270): ImageData {
  const sourceCanvas = document.createElement("canvas");
  sourceCanvas.width = imageData.width;
  sourceCanvas.height = imageData.height;
  const sourceContext = sourceCanvas.getContext("2d");
  if (!sourceContext) {
    throw new Error("Canvas unavailable");
  }

  sourceContext.putImageData(imageData, 0, 0);

  const canvas = document.createElement("canvas");
  if (degrees === 180) {
    canvas.width = imageData.width;
    canvas.height = imageData.height;
  } else {
    canvas.width = imageData.height;
    canvas.height = imageData.width;
  }

  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("Canvas unavailable");
  }

  context.translate(canvas.width / 2, canvas.height / 2);
  context.rotate((Math.PI / 180) * degrees);
  context.drawImage(sourceCanvas, -imageData.width / 2, -imageData.height / 2);
  return context.getImageData(0, 0, canvas.width, canvas.height);
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
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("Image failed to load"));
    image.crossOrigin = "anonymous";
    image.src = source;
  });
}
