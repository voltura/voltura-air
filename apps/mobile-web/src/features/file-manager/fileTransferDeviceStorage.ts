const opfsDirectoryName = "voltura-air-transfers";
const opfsOwnerStorageKey = "voltura-air-transfer-owner";
const validOwnerId = /^[a-zA-Z0-9_-]{1,64}$/;

export interface DeviceTransferStorage {
  directory: FileSystemDirectoryHandle;
  handle: FileSystemFileHandle;
  storedName: string;
  writable: FileSystemWritableFileStream;
}

export function supportsDeviceTransferStorage(): boolean {
  return (
    typeof navigator !== "undefined" &&
    typeof navigator.storage?.getDirectory === "function" &&
    typeof FileSystemFileHandle !== "undefined" &&
    typeof FileSystemFileHandle.prototype.createWritable === "function"
  );
}

function getDeviceTransferStorageOwnerId(): string {
  try {
    const stored = sessionStorage.getItem(opfsOwnerStorageKey);
    if (stored && validOwnerId.test(stored)) {
      return stored;
    }
    const ownerId = crypto.randomUUID();
    sessionStorage.setItem(opfsOwnerStorageKey, ownerId);
    return ownerId;
  } catch {
    return crypto.randomUUID();
  }
}

export async function prepareDeviceTransferStorage(
  declaredSize: number,
  transferId: string,
): Promise<DeviceTransferStorage> {
  try {
    const estimate = await navigator.storage.estimate();
    if (
      estimate.quota !== undefined &&
      estimate.usage !== undefined &&
      estimate.quota - estimate.usage < declaredSize
    ) {
      throw new Error("This device does not have enough available browser storage.");
    }
  } catch (error) {
    if (error instanceof Error && error.message.includes("enough available browser storage")) {
      throw error;
    }
  }
  const root = await navigator.storage.getDirectory();
  const transfers = await root.getDirectoryHandle(opfsDirectoryName, { create: true });
  const ownerId = getDeviceTransferStorageOwnerId();
  const directory = await transfers.getDirectoryHandle(ownerId, { create: true });
  const storedName = `${transferId}.partial`;
  const handle = await directory.getFileHandle(storedName, { create: true });
  try {
    const writable = await handle.createWritable({ keepExistingData: false });
    return { directory, handle, storedName, writable };
  } catch (error) {
    try {
      await directory.removeEntry(storedName);
    } catch {
      /* The next Files start retries the sweep. */
    }
    throw error;
  }
}

export async function removeDeviceTransferFile(
  directory: FileSystemDirectoryHandle,
  storedName: string,
): Promise<void> {
  try {
    await directory.removeEntry(storedName);
  } catch (error) {
    if (!(error instanceof DOMException && error.name === "NotFoundError")) {
      throw error;
    }
  }
}

export async function sweepDeviceTransferStorage(): Promise<void> {
  try {
    const root = await navigator.storage.getDirectory();
    const transfers = await root.getDirectoryHandle(opfsDirectoryName);
    await transfers.removeEntry(getDeviceTransferStorageOwnerId(), { recursive: true });
  } catch (error) {
    if (!(error instanceof DOMException && error.name === "NotFoundError")) {
      return;
    }
  }
}

export function saveOrShareDeviceTransfer(
  stored: File,
  name: string,
): Promise<"shared" | "download-started"> {
  const file = new File([stored], name, {
    type: mimeTypeForName(name),
    lastModified: stored.lastModified,
  });
  const shareNavigator = navigator as Navigator & { canShare?: (data: ShareData) => boolean };
  if (navigator.share && shareNavigator.canShare?.({ files: [file] })) {
    return navigator.share({ files: [file], title: name }).then(() => "shared" as const);
  }
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
  return Promise.resolve("download-started");
}

function mimeTypeForName(name: string): string {
  const extension = name.split(".").pop()?.toLowerCase();
  return (
    (
      {
        pdf: "application/pdf",
        png: "image/png",
        jpg: "image/jpeg",
        jpeg: "image/jpeg",
        gif: "image/gif",
        webp: "image/webp",
        txt: "text/plain",
        csv: "text/csv",
        zip: "application/zip",
        mp4: "video/mp4",
        mp3: "audio/mpeg",
      } as Record<string, string>
    )[extension ?? ""] ?? "application/octet-stream"
  );
}
