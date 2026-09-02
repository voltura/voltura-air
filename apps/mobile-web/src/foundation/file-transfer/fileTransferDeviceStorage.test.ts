import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type {
  ensureDeviceFileStorageInitialized as EnsureDeviceFileStorageInitialized,
  prepareDeviceFileStorage as PrepareDeviceFileStorage,
  saveOrShareDeviceFile as SaveOrShareDeviceFile,
  sweepDeviceFileStorage as SweepDeviceFileStorage,
} from "./fileTransferDeviceStorage";

let ensureDeviceFileStorageInitialized: typeof EnsureDeviceFileStorageInitialized;
let prepareDeviceFileStorage: typeof PrepareDeviceFileStorage;
let saveOrShareDeviceFile: typeof SaveOrShareDeviceFile;
let sweepDeviceFileStorage: typeof SweepDeviceFileStorage;

function availableLockManager(): LockManager {
  return {
    request: vi.fn(
      (name: string, _options: LockOptions, callback: (lock: Lock | null) => unknown) =>
        Promise.resolve(callback({ name, mode: "exclusive" } as Lock)),
    ),
  } as unknown as LockManager;
}

function exclusiveLockManager(): LockManager {
  const held = new Set<string>();
  return {
    request: vi.fn(
      async (name: string, _options: LockOptions, callback: (lock: Lock | null) => unknown) => {
        if (held.has(name)) {
          return callback(null);
        }
        held.add(name);
        try {
          return await callback({ name, mode: "exclusive" } as Lock);
        } finally {
          held.delete(name);
        }
      },
    ),
  } as unknown as LockManager;
}

function ownerStorage(initialOwnerId: string): Storage {
  let ownerId = initialOwnerId;
  return {
    getItem: vi.fn(() => ownerId),
    setItem: vi.fn((_key: string, value: string) => {
      ownerId = value;
    }),
  } as unknown as Storage;
}

function storageWithEstimate(
  estimate: () => Promise<StorageEstimate>,
  locks = availableLockManager(),
) {
  const writable = {} as FileSystemWritableFileStream;
  const handle = {
    createWritable: vi.fn(() => Promise.resolve(writable)),
  } as unknown as FileSystemFileHandle;
  const directory = {
    getFileHandle: vi.fn(() => Promise.resolve(handle)),
    removeEntry: vi.fn(() => Promise.resolve()),
  } as unknown as FileSystemDirectoryHandle;
  const transfers = {
    getDirectoryHandle: vi.fn(() => Promise.resolve(directory)),
    removeEntry: vi.fn(() => Promise.resolve()),
  } as unknown as FileSystemDirectoryHandle;
  const root = {
    getDirectoryHandle: vi.fn(() => Promise.resolve(transfers)),
  } as unknown as FileSystemDirectoryHandle;
  vi.stubGlobal("navigator", {
    storage: { estimate, getDirectory: vi.fn(() => Promise.resolve(root)) },
    locks,
  });
  return { directory, handle, root, transfers, writable };
}

describe("device file storage", () => {
  beforeEach(async () => {
    vi.resetModules();
    const storageModule = await import("./fileTransferDeviceStorage");
    ensureDeviceFileStorageInitialized = storageModule.ensureDeviceFileStorageInitialized;
    prepareDeviceFileStorage = storageModule.prepareDeviceFileStorage;
    saveOrShareDeviceFile = storageModule.saveOrShareDeviceFile;
    sweepDeviceFileStorage = storageModule.sweepDeviceFileStorage;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it("rejects a file larger than reported available browser storage", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({ quota: 10, usage: 8 }));

    await expect(prepareDeviceFileStorage(3, "transfer-a")).rejects.toThrow(
      "enough available browser storage",
    );
    expect(storage.transfers.getDirectoryHandle).not.toHaveBeenCalled();
    expect(storage.directory.getFileHandle).not.toHaveBeenCalled();
  });

  it("accepts zero-byte files and treats an unavailable estimate as advisory", async () => {
    const storage = storageWithEstimate(() =>
      Promise.reject(new DOMException("Unavailable", "NotSupportedError")),
    );

    const prepared = await prepareDeviceFileStorage(0, "transfer-b");

    expect(prepared).toEqual({
      directory: storage.directory,
      handle: storage.handle,
      storedName: "transfer-b.partial",
      writable: storage.writable,
    });
    expect(storage.handle.createWritable).toHaveBeenCalledWith({ keepExistingData: false });
  });

  it("requires a usable storage estimate when the caller requests an exact reservation", async () => {
    storageWithEstimate(() => Promise.resolve({}));

    await expect(prepareDeviceFileStorage(512, "recording-a", true)).rejects.toThrow(
      "verify enough available browser storage",
    );
  });

  it("starts native file sharing synchronously from the user action", async () => {
    const share = vi.fn<(data: ShareData) => Promise<void>>(() => Promise.resolve());
    vi.stubGlobal("navigator", { canShare: vi.fn(() => true), share });
    const stored = new File(["report"], "transfer.partial", { lastModified: 123 });

    const saving = saveOrShareDeviceFile(stored, "report.txt");

    expect(share).toHaveBeenCalledOnce();
    expect(share.mock.calls[0]?.[0].files?.[0]?.name).toBe("report.txt");
    await saving;
  });

  it("shares locally generated WebM recordings with the matching media type", async () => {
    const share = vi.fn<(data: ShareData) => Promise<void>>(() => Promise.resolve());
    vi.stubGlobal("navigator", { canShare: vi.fn(() => true), share });

    await saveOrShareDeviceFile(new File(["video"], "recording.partial"), "recording.webm");

    expect(share.mock.calls[0]?.[0].files?.[0]?.type).toBe("video/webm");
  });

  it("revokes the fallback object URL immediately when download activation fails", async () => {
    const revokeObjectURL = vi.fn();
    vi.stubGlobal("navigator", {});
    vi.stubGlobal("URL", {
      createObjectURL: vi.fn(() => "blob:recording"),
      revokeObjectURL,
    });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementationOnce(() => {
      throw new Error("click failed");
    });

    await expect(
      saveOrShareDeviceFile(new File(["video"], "recording.partial"), "recording.mp4"),
    ).rejects.toThrow("click failed");

    expect(revokeObjectURL).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:recording");
  });

  it("preserves the create failure when partial cleanup also fails", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({}));
    vi.mocked(storage.handle.createWritable).mockRejectedValueOnce(
      new DOMException("Failed", "InvalidStateError"),
    );
    vi.mocked(storage.directory.removeEntry).mockRejectedValueOnce(new Error("cleanup failed"));

    await expect(prepareDeviceFileStorage(1, "transfer-c")).rejects.toThrow("Failed");
    expect(storage.directory.removeEntry).toHaveBeenCalledWith("transfer-c.partial");
  });

  it("sweeps only the current tab's transfer directory", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({}));
    vi.stubGlobal("sessionStorage", { getItem: vi.fn(() => "tab-a"), setItem: vi.fn() });

    await sweepDeviceFileStorage();

    expect(storage.root.getDirectoryHandle).toHaveBeenCalledWith("voltura-air-transfers");
    expect(storage.transfers.removeEntry).toHaveBeenCalledWith("tab-a", { recursive: true });
    expect(storage.transfers.removeEntry).not.toHaveBeenCalledWith("tab-b", expect.anything());
    expect(storage.root.removeEntry).toBeUndefined();
  });

  it("initializes one shared sweep for concurrent artifact owners in the same tab", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({}));
    vi.stubGlobal("sessionStorage", { getItem: vi.fn(() => "tab-shared"), setItem: vi.fn() });

    await Promise.all([ensureDeviceFileStorageInitialized(), ensureDeviceFileStorageInitialized()]);

    expect(storage.transfers.removeEntry).toHaveBeenCalledTimes(1);
    expect(storage.transfers.removeEntry).toHaveBeenCalledWith("tab-shared", { recursive: true });
  });

  it("gives a cloned live tab a fresh owner without sweeping the original tab", async () => {
    const locks = exclusiveLockManager();
    const storage = storageWithEstimate(() => Promise.resolve({}), locks);
    vi.stubGlobal("sessionStorage", ownerStorage("tab-a"));

    await ensureDeviceFileStorageInitialized();
    vi.mocked(storage.transfers.removeEntry).mockClear();
    vi.mocked(storage.transfers.getDirectoryHandle).mockClear();

    const clonedStorage = ownerStorage("tab-a");
    vi.stubGlobal("sessionStorage", clonedStorage);
    vi.stubGlobal("crypto", { randomUUID: vi.fn(() => "tab-b") });
    vi.resetModules();
    const clonedTab = await import("./fileTransferDeviceStorage");

    await clonedTab.prepareDeviceFileStorage(0, "recording-a");

    expect(storage.transfers.removeEntry).not.toHaveBeenCalled();
    expect(storage.transfers.getDirectoryHandle).toHaveBeenCalledWith("tab-b", { create: true });
    expect(clonedStorage.setItem).toHaveBeenCalledWith("voltura-air-transfer-owner", "tab-b");
  });

  it("uses a fresh owner without sweeping when browser locking fails", async () => {
    const locks = {
      request: vi.fn(() => Promise.reject(new DOMException("Unavailable", "NotSupportedError"))),
    } as unknown as LockManager;
    const storage = storageWithEstimate(() => Promise.resolve({}), locks);
    const currentStorage = ownerStorage("tab-a");
    vi.stubGlobal("sessionStorage", currentStorage);
    vi.stubGlobal("crypto", { randomUUID: vi.fn(() => "tab-b") });

    await prepareDeviceFileStorage(0, "recording-b");

    expect(storage.transfers.removeEntry).not.toHaveBeenCalled();
    expect(storage.transfers.getDirectoryHandle).toHaveBeenCalledWith("tab-b", { create: true });
    expect(currentStorage.setItem).toHaveBeenCalledWith("voltura-air-transfer-owner", "tab-b");
  });
});
