export type DeviceClipboardReadResult =
  | { status: "success"; text: string }
  | { status: "empty" | "denied" | "failed" | "unavailable" };

export type DeviceClipboardWriteResult =
  | { status: "copied" }
  | { status: "denied" | "failed" | "unavailable" };

export function canReadTextFromDeviceClipboard(): boolean {
  return window.isSecureContext && typeof navigator.clipboard?.readText === "function";
}

export function canWriteTextToDeviceClipboard(): boolean {
  return window.isSecureContext && typeof navigator.clipboard?.writeText === "function";
}

export function canWriteDeferredTextToDeviceClipboard(): boolean {
  return (
    window.isSecureContext &&
    typeof navigator.clipboard?.write === "function" &&
    typeof ClipboardItem === "function"
  );
}

export async function readTextFromDeviceClipboard(): Promise<DeviceClipboardReadResult> {
  if (!canReadTextFromDeviceClipboard()) {
    return { status: "unavailable" };
  }

  try {
    const text = await navigator.clipboard.readText();
    return text.length === 0 ? { status: "empty" } : { status: "success", text };
  } catch (error) {
    return { status: isClipboardPermissionError(error) ? "denied" : "failed" };
  }
}

export async function writeTextToDeviceClipboard(
  value: string,
): Promise<DeviceClipboardWriteResult> {
  if (!canWriteTextToDeviceClipboard()) {
    return { status: "unavailable" };
  }

  try {
    await navigator.clipboard.writeText(value);
    return { status: "copied" };
  } catch (error) {
    return { status: isClipboardPermissionError(error) ? "denied" : "failed" };
  }
}

export function writeDeferredTextToDeviceClipboard(
  value: Promise<Blob>,
): Promise<DeviceClipboardWriteResult> {
  if (!canWriteDeferredTextToDeviceClipboard()) {
    return Promise.resolve({ status: "unavailable" });
  }

  try {
    return navigator.clipboard
      .write([new ClipboardItem({ "text/plain": value })])
      .then((): DeviceClipboardWriteResult => ({ status: "copied" }))
      .catch((error: unknown): DeviceClipboardWriteResult => ({
        status: isClipboardPermissionError(error) ? "denied" : "failed",
      }));
  } catch (error) {
    return Promise.resolve({ status: isClipboardPermissionError(error) ? "denied" : "failed" });
  }
}

function isClipboardPermissionError(error: unknown): boolean {
  return (
    error instanceof DOMException &&
    (error.name === "NotAllowedError" || error.name === "SecurityError")
  );
}
