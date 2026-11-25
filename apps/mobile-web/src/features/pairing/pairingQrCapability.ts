export function canUseLivePairingQrScanner(
  protocol = window.location.protocol,
  mediaDevices = navigator.mediaDevices,
): boolean {
  return protocol === "https:" && typeof mediaDevices?.getUserMedia === "function";
}

export async function decodePairingQrImage(file: File, signal?: AbortSignal): Promise<string> {
  const { decodeQrImage } = await import("../../foundation/pairing/qrCode");
  return decodeQrImage(file, signal);
}
