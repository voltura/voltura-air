import { afterEach, describe, expect, it, vi } from "vitest";
import { prepareDeviceTransferStorage, saveOrShareDeviceTransfer } from "./fileTransferDeviceStorage";

function storageWithEstimate(estimate: () => Promise<StorageEstimate>) {
  const writable = {} as FileSystemWritableFileStream;
  const handle = { createWritable: vi.fn(() => Promise.resolve(writable)) } as unknown as FileSystemFileHandle;
  const directory = { getFileHandle: vi.fn(() => Promise.resolve(handle)), removeEntry: vi.fn(() => Promise.resolve()) } as unknown as FileSystemDirectoryHandle;
  const root = { getDirectoryHandle: vi.fn(() => Promise.resolve(directory)) } as unknown as FileSystemDirectoryHandle;
  vi.stubGlobal("navigator", { storage: { estimate, getDirectory: vi.fn(() => Promise.resolve(root)) } });
  return { directory, handle, root, writable };
}

describe("device file-transfer storage", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("rejects a file larger than reported available browser storage", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({ quota: 10, usage: 8 }));

    await expect(prepareDeviceTransferStorage(3, "transfer-a")).rejects.toThrow("enough available browser storage");
    expect(storage.root.getDirectoryHandle).not.toHaveBeenCalled();
  });

  it("accepts zero-byte files and treats an unavailable estimate as advisory", async () => {
    const storage = storageWithEstimate(() => Promise.reject(new DOMException("Unavailable", "NotSupportedError")));

    const prepared = await prepareDeviceTransferStorage(0, "transfer-b");

    expect(prepared).toEqual({ directory: storage.directory, handle: storage.handle, storedName: "transfer-b.partial", writable: storage.writable });
    expect(storage.handle.createWritable).toHaveBeenCalledWith({ keepExistingData: false });
  });

  it("starts native file sharing synchronously from the user action", async () => {
    const share = vi.fn<(data: ShareData) => Promise<void>>(() => Promise.resolve());
    vi.stubGlobal("navigator", { canShare: vi.fn(() => true), share });
    const stored = new File(["report"], "transfer.partial", { lastModified: 123 });

    const saving = saveOrShareDeviceTransfer(stored, "report.txt");

    expect(share).toHaveBeenCalledOnce();
    expect(share.mock.calls[0]?.[0].files?.[0]?.name).toBe("report.txt");
    await saving;
  });

  it("removes a partial when writable creation fails", async () => {
    const storage = storageWithEstimate(() => Promise.resolve({}));
    vi.mocked(storage.handle.createWritable).mockRejectedValueOnce(new DOMException("Failed", "InvalidStateError"));

    await expect(prepareDeviceTransferStorage(1, "transfer-c")).rejects.toThrow("Failed");
    expect(storage.directory.removeEntry).toHaveBeenCalledWith("transfer-c.partial");
  });
});
