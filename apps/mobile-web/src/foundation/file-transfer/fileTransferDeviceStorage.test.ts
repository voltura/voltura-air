import { afterEach, describe, expect, it, vi } from "vitest";
import {
  ensureDeviceFileStorageInitialized,
  prepareDeviceFileStorage,
  saveOrShareDeviceFile,
  sweepDeviceFileStorage,
} from "./fileTransferDeviceStorage";

function storageWithEstimate(estimate: () => Promise<StorageEstimate>) {
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
  });
  return { directory, handle, root, transfers, writable };
}

describe("device file storage", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
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
});
