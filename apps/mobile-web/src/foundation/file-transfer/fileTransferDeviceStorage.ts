const opfsDirectoryName = "voltura-air-transfers";
const opfsOwnerStorageKey = "voltura-air-transfer-owner";
const opfsOwnerLockPrefix = "voltura-air-transfer-owner:";
const validOwnerId = /^[a-zA-Z0-9_-]{1,64}$/;
const ownerClaimLifetime = new Promise<void>(() => undefined);

export interface DeviceFileStorage {
  directory: FileSystemDirectoryHandle;
  handle: FileSystemFileHandle;
  storedName: string;
  writable: FileSystemWritableFileStream;
}

export function supportsDeviceFileStorage(): boolean {
  return (
    typeof navigator !== "undefined" &&
    typeof navigator.storage?.getDirectory === "function" &&
    typeof FileSystemFileHandle !== "undefined" &&
    typeof FileSystemFileHandle.prototype.createWritable === "function"
  );
}

interface DeviceFileStorageOwner {
  ownerId: string;
  sweepInheritedOwner: boolean;
}

let ownerResolution: Promise<DeviceFileStorageOwner> | null = null;
let initialization: Promise<void> | null = null;

function readStoredOwnerId(): string | null {
  try {
    const stored = sessionStorage.getItem(opfsOwnerStorageKey);
    if (stored && validOwnerId.test(stored)) {
      return stored;
    }
  } catch {
    /* A page-unique in-memory owner remains safe when storage is unavailable. */
  }
  return null;
}

function storeOwnerId(ownerId: string): void {
  try {
    sessionStorage.setItem(opfsOwnerStorageKey, ownerId);
  } catch {
    /* The in-memory owner remains valid for this page lifetime. */
  }
}

function tryClaimOwner(ownerId: string): Promise<boolean> {
  const lockManager = (navigator as Navigator & { locks?: LockManager }).locks;
  if (!lockManager) {
    return Promise.resolve(false);
  }

  return new Promise<boolean>((resolve) => {
    let callbackStarted = false;
    try {
      void lockManager
        .request(
          `${opfsOwnerLockPrefix}${ownerId}`,
          { mode: "exclusive", ifAvailable: true },
          async (lock) => {
            callbackStarted = true;
            resolve(lock !== null);
            if (lock) {
              await ownerClaimLifetime;
            }
          },
        )
        .catch(() => {
          if (!callbackStarted) {
            resolve(false);
          }
        });
    } catch {
      resolve(false);
    }
  });
}

function resolveDeviceFileStorageOwner(): Promise<DeviceFileStorageOwner> {
  ownerResolution ??= (async () => {
    const inheritedOwnerId = readStoredOwnerId();
    if (inheritedOwnerId && (await tryClaimOwner(inheritedOwnerId))) {
      return { ownerId: inheritedOwnerId, sweepInheritedOwner: true };
    }

    const ownerId = crypto.randomUUID();
    storeOwnerId(ownerId);
    await tryClaimOwner(ownerId);
    return { ownerId, sweepInheritedOwner: false };
  })();
  return ownerResolution;
}

export async function ensureDeviceFileStorageInitialized(): Promise<void> {
  initialization ??= resolveDeviceFileStorageOwner().then(({ ownerId, sweepInheritedOwner }) =>
    sweepInheritedOwner ? sweepDeviceFileStorageOwner(ownerId) : undefined,
  );
  await initialization;
}

export async function prepareDeviceFileStorage(
  declaredSize: number,
  transferId: string,
  requireAvailableSpace = false,
): Promise<DeviceFileStorage> {
  await ensureDeviceFileStorageInitialized();
  try {
    const estimate = await navigator.storage.estimate();
    if (estimate.quota === undefined || estimate.usage === undefined) {
      if (requireAvailableSpace) {
        throw new Error("This browser could not verify enough available browser storage.");
      }
    } else if (estimate.quota - estimate.usage < declaredSize) {
      throw new Error("This device does not have enough available browser storage.");
    }
  } catch (error) {
    if (error instanceof Error && error.message.includes("enough available browser storage")) {
      throw error;
    }
    if (requireAvailableSpace) {
      throw new Error("This browser could not verify enough available browser storage.", {
        cause: error,
      });
    }
  }
  const root = await navigator.storage.getDirectory();
  const transfers = await root.getDirectoryHandle(opfsDirectoryName, { create: true });
  const { ownerId } = await resolveDeviceFileStorageOwner();
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
      /* The next page initialization retries the owner sweep. */
    }
    throw error;
  }
}

export async function removeDeviceFile(
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

export async function sweepDeviceFileStorage(): Promise<void> {
  const { ownerId } = await resolveDeviceFileStorageOwner();
  await sweepDeviceFileStorageOwner(ownerId);
}

async function sweepDeviceFileStorageOwner(ownerId: string): Promise<void> {
  try {
    const root = await navigator.storage.getDirectory();
    const transfers = await root.getDirectoryHandle(opfsDirectoryName);
    await transfers.removeEntry(ownerId, { recursive: true });
  } catch (error) {
    if (!(error instanceof DOMException && error.name === "NotFoundError")) {
      return;
    }
  }
}

export function saveOrShareDeviceFile(
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
  try {
    link.click();
  } catch (error) {
    URL.revokeObjectURL(url);
    return Promise.reject(
      error instanceof Error
        ? error
        : new Error("This browser could not start the download.", { cause: error }),
    );
  }
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
        webm: "video/webm",
        mp3: "audio/mpeg",
      } as Record<string, string>
    )[extension ?? ""] ?? "application/octet-stream"
  );
}
