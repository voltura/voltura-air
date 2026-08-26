import jsQR from "jsqr";

interface QrDecodeRequest {
  id: number;
  height: number;
  inversionAttempts: "dontInvert" | "onlyInvert";
  pixels: ArrayBuffer;
  width: number;
}

interface QrDecodeResponse {
  id: number;
  data?: string;
  error?: string;
}

const workerScope = globalThis as unknown as {
  onmessage: ((event: MessageEvent<QrDecodeRequest>) => void) | null;
  postMessage: (message: QrDecodeResponse) => void;
};

workerScope.onmessage = (event) => {
  const request = event.data;
  let response: QrDecodeResponse;
  try {
    const code = jsQR(new Uint8ClampedArray(request.pixels), request.width, request.height, {
      inversionAttempts: request.inversionAttempts,
    });
    response = { id: request.id, ...(code?.data ? { data: code.data } : {}) };
  } catch {
    response = { id: request.id, error: "QR decoder failed." };
  }

  workerScope.postMessage(response);
};
