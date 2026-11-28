import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishFileManagerResult } from "../connection/fileManagerResultBus";
import type { ClientMessage } from "../protocol/messages";

const storage = vi.hoisted(() => {
  const file = new File([], "transfer.partial");
  const handle = { getFile: vi.fn(() => Promise.resolve(file)) };
  const writable = {
    abort: vi.fn(() => Promise.resolve()),
    close: vi.fn(() => Promise.resolve()),
    write: vi.fn(() => Promise.resolve()),
  };
  const prepared = { directory: {}, handle, storedName: "transfer.partial", writable };
  return {
    handle,
    writable,
    prepared,
    prepare: vi.fn(() => Promise.resolve(prepared)),
    remove: vi.fn(() => Promise.resolve()),
    save: vi.fn<() => Promise<"shared" | "download-started">>(() =>
      Promise.resolve("download-started"),
    ),
  };
});

vi.mock("../connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));
vi.mock("../webrtc/sessionCrypto", () => ({
  hashSessionDescription: () => "sdp-hash",
  verifyHostSessionSignature: () => true,
}));
vi.mock("./fileTransferDeviceStorage", () => ({
  ensureDeviceFileStorageInitialized: () => Promise.resolve(),
  prepareDeviceFileStorage: storage.prepare,
  removeDeviceFile: storage.remove,
  saveOrShareDeviceFile: storage.save,
  supportsDeviceFileStorage: () => true,
}));

import { useFileTransfer, type FileTransferTarget } from "./useFileTransfer";

class TestDataChannel {
  label = "voltura-file-transfer";
  readyState: RTCDataChannelState = "open";
  binaryType: BinaryType = "arraybuffer";
  bufferedAmount = 0;
  bufferedAmountLowThreshold = 0;
  private readonly listeners = new Map<string, EventListener[]>();
  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  send = vi.fn();
  close = vi.fn(() => {
    this.readyState = "closed";
  });
  dispatch(type: string, event: Event = new Event(type)) {
    this.listeners.get(type)?.forEach((listener) => listener(event));
  }
}

class TestPeerConnection {
  static latest: TestPeerConnection | null = null;
  connectionState: RTCPeerConnectionState = "new";
  iceGatheringState: RTCIceGatheringState = "complete";
  localDescription: RTCSessionDescriptionInit | null = { type: "answer", sdp: "answer-sdp" };
  readonly channel = new TestDataChannel();
  private readonly listeners = new Map<string, EventListener[]>();
  constructor() {
    TestPeerConnection.latest = this;
  }
  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  removeEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, (this.listeners.get(type) ?? []).filter((candidate) => candidate !== listener));
  }
  deliverChannel(channel: TestDataChannel) {
    this.listeners
      .get("datachannel")
      ?.forEach((listener) => listener({ channel } as unknown as Event));
  }
  setRemoteDescription() {
    this.deliverChannel(this.channel);
    this.channel.dispatch("open");
    return Promise.resolve();
  }
  createAnswer = vi.fn(() => Promise.resolve({ type: "answer" as const, sdp: "answer-sdp" }));
  setLocalDescription = vi.fn(() => Promise.resolve());
  close = vi.fn(() => {
    this.connectionState = "closed";
    this.listeners
      .get("connectionstatechange")
      ?.forEach((listener) => listener(new Event("connectionstatechange")));
  });
}

describe("useFileTransfer download lifecycle", () => {
  beforeEach(() => {
    storage.writable.abort.mockReset().mockResolvedValue(undefined);
    storage.writable.close.mockReset().mockResolvedValue(undefined);
    storage.writable.write.mockReset().mockResolvedValue(undefined);
    storage.handle.getFile.mockReset().mockResolvedValue(new File([], "transfer.partial"));
    storage.save.mockReset().mockResolvedValue("download-started");
  });
  const pc = {
    customName: false,
    id: "pc",
    name: "PC",
    url: "https://pc.invalid",
    hostIdentityPublicKey: "host-key",
    transportMode: "secure-direct" as const,
  };
  const target: FileTransferTarget = {
    sessionId: "session",
    panel: "left",
    revision: "revision",
    entry: {
      id: "file",
      name: "report.txt",
      kind: "file",
      extension: "txt",
      size: 0,
      modifiedUtc: "2026-08-25T00:00:00Z",
      attributes: [],
    },
  };
  const completion = {
    type: "file.transfer.result" as const,
    transferId: "transfer-a",
    direction: "download" as const,
    succeeded: true,
    message: "Ready",
    fileName: "report.txt",
    declaredSize: 0,
  };
  const offer = {
    type: "file.transfer.offer" as const,
    transferId: "transfer-a",
    direction: "download" as const,
    fileName: "report.txt",
    declaredSize: 0,
    offerSdp: "offer-sdp",
    hostSignature: "host-signature",
  };
  function setup(canSend?: (message: ClientMessage) => boolean) {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const send = (message: ClientMessage): boolean | void => {
      sent.push(message);
      return canSend?.(message);
    };
    const hook = renderHook(
      ({ sender, host = pc, identity = "client" }) => useFileTransfer(host, identity, true, sender),
      { initialProps: { sender: send, host: pc, identity: "client" } },
    );
    act(() => hook.result.current.startDownload(target));
    const request = sent.find((message) => message.type === "file.transfer.start")!;
    const startResult = {
      type: "file.transfer.start.result" as const,
      operationId: request.operationId!,
      succeeded: true,
      message: "Started",
      transferId: "transfer-a",
    };
    act(() => publishFileManagerResult(startResult));
    return { ...hook, sent, send, startResult };
  }
  async function connected(declaredSize = 0) {
    const hook = setup();
    await act(async () => {
      publishFileManagerResult({ ...offer, declaredSize });
      await Promise.resolve();
    });
    await waitFor(() =>
      expect(hook.sent.some((message) => message.type === "file.transfer.answer")).toBe(true),
    );
    return hook;
  }
  async function ready() {
    const hook = await connected();
    await act(async () => {
      publishFileManagerResult(completion);
      await Promise.resolve();
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);
    return hook;
  }

  it("removes candidate gathering listeners as soon as a transfer is canceled", async () => {
    const setDescription = TestPeerConnection.prototype.setRemoteDescription;
    const apply = vi.spyOn(TestPeerConnection.prototype, "setRemoteDescription").mockImplementationOnce(function (this: TestPeerConnection) {
      this.iceGatheringState = "gathering";
      return setDescription.call(this);
    });
    const removeListener = vi.spyOn(TestPeerConnection.prototype, "removeEventListener");
    const hook = setup();
    await act(() => {
      publishFileManagerResult(offer);
      return Promise.resolve();
    });
    expect(apply).toHaveBeenCalledOnce();
    const peer = TestPeerConnection.latest!;
    peer.close.mockImplementationOnce(() => {
      peer.connectionState = "closed";
    });
    await act(() => {
      hook.result.current.cancel();
      return Promise.resolve();
    });
    expect(removeListener).toHaveBeenCalledWith("icegatheringstatechange", expect.any(Function));
    expect(peer.close).toHaveBeenCalledOnce();
    expect(hook.sent.some((message) => message.type === "file.transfer.answer")).toBe(false);
  });

  it("cleans up a transfer when its signed answer cannot be sent", async () => {
    const hook = setup((message) => message.type !== "file.transfer.answer");
    await act(() => {
      publishFileManagerResult(offer);
      return Promise.resolve();
    });
    expect(hook.result.current.presentation.message).toContain("answer could not be sent");
    expect(TestPeerConnection.latest?.close).toHaveBeenCalledOnce();
    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(hook.sent).toContainEqual(expect.objectContaining({ type: "file.transfer.cancel" }));
  });

  it("cleans up a rejected answer only for the current file transfer operation", async () => {
    const hook = await connected();
    const rejected = {
      type: "file.transfer.answer.result" as const,
      operationId: "retired-answer",
      succeeded: false,
      code: "offer-expired",
      message: "The file-transfer offer expired.",
    };
    act(() => publishFileManagerResult(rejected));
    expect(hook.result.current.presentation.active).toBe(true);
    const answer = hook.sent.find((message) => message.type === "file.transfer.answer")!;
    await act(() => {
      publishFileManagerResult({ ...rejected, operationId: answer.operationId });
      return Promise.resolve();
    });
    expect(hook.result.current.presentation.active).toBe(false);
    expect(hook.result.current.presentation.message).toBe(rejected.message);
    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(TestPeerConnection.latest?.close).toHaveBeenCalledOnce();
  });

  it("keeps a completed download ready even when retired channel events arrive", async () => {
    const hook = await ready();
    const peer = TestPeerConnection.latest!;
    const lateChannel = new TestDataChannel();
    act(() => {
      peer.channel.dispatch("message", new MessageEvent("message", { data: new ArrayBuffer(0) }));
      peer.deliverChannel(lateChannel);
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);
    expect(lateChannel.close).toHaveBeenCalledOnce();
    expect(storage.remove).not.toHaveBeenCalled();
    await act(() => hook.result.current.discardReadyFile());
    expect(peer.close).toHaveBeenCalledOnce();
    expect(peer.channel.close).toHaveBeenCalledOnce();
  });

  it("keeps download finalization independent of later data-channel messages", async () => {
    const hook = await connected();
    let finishWrite!: () => void;
    storage.writable.close.mockImplementationOnce(() =>
      new Promise<void>((resolve) => { finishWrite = resolve; }),
    );
    await act(() => {
      publishFileManagerResult(completion);
      return Promise.resolve();
    });
    expect(storage.writable.close).toHaveBeenCalledOnce();
    const peer = TestPeerConnection.latest!;
    act(() => peer.channel.dispatch("message", new MessageEvent("message", { data: new ArrayBuffer(0) })));
    expect(storage.writable.abort).not.toHaveBeenCalled();
    await act(() => {
      finishWrite();
      return Promise.resolve();
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);
    expect(storage.remove).not.toHaveBeenCalled();
  });

  it.each(["close", "commit"])(
    "contains transfer %s failure even if cleanup also fails",
    async (boundary) => {
      const hook = await connected();
      if (boundary === "close") {
        storage.writable.close.mockRejectedValueOnce(new Error("close failed"));
      } else {
        storage.handle.getFile.mockRejectedValueOnce(new Error("commit failed"));
      }
      storage.writable.abort.mockRejectedValueOnce(new Error("abort failed"));
      storage.remove.mockRejectedValueOnce(new Error("remove failed"));
      await act(async () => {
        publishFileManagerResult(completion);
        await Promise.resolve();
      });
      expect(hook.result.current.presentation.active).toBe(false);
      expect(hook.result.current.presentation.readyToSave).toBe(false);
      expect(hook.result.current.presentation.message).toContain("could not finish storing");
      expect(storage.remove).toHaveBeenCalledOnce();
    },
  );
  it("ignores a duplicate offer while storage preparation is still pending", async () => {
    let finish!: (value: typeof storage.prepared) => void;
    storage.prepare.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const hook = setup();
    act(() => {
      publishFileManagerResult(offer);
      publishFileManagerResult(offer);
    });
    expect(storage.prepare).toHaveBeenCalledOnce();
    await act(async () => {
      finish(storage.prepared);
      await Promise.resolve();
    });
    hook.unmount();
  });
  it("does not finalize a download twice for simultaneous completion results", async () => {
    const hook = await connected();
    await act(async () => {
      publishFileManagerResult(completion);
      publishFileManagerResult(completion);
      await Promise.resolve();
    });
    expect(storage.writable.close).toHaveBeenCalledOnce();
    expect(hook.result.current.presentation.readyToSave).toBe(true);
  });
  it("keeps a ready download when an already-consumed completion is replayed", async () => {
    const hook = await ready();
    await act(async () => {
      publishFileManagerResult(completion);
      await Promise.resolve();
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);
    expect(storage.remove).not.toHaveBeenCalled();
  });
  it("does not let late transport status hide a ready file", async () => {
    const hook = await ready();
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.status",
        direction: "download",
        bytesTotal: 0,
        transferId: "transfer-a",
        state: "transferring",
        bytesCompleted: 0,
      }),
    );
    expect(hook.result.current.presentation.readyToSave).toBe(true);
    expect(hook.result.current.presentation.active).toBe(false);
  });
  it("does not replace a live transfer ID with a duplicate start response", async () => {
    const hook = setup();
    act(() => publishFileManagerResult({ ...hook.startResult, transferId: "other-transfer" }));
    await act(async () => {
      publishFileManagerResult(offer);
      await Promise.resolve();
    });
    expect(
      hook.sent.some(
        (message) => message.type === "file.transfer.answer" && message.transferId === "transfer-a",
      ),
    ).toBe(true);
  });
  it("keeps a transfer when only the send callback identity changes", async () => {
    const hook = setup();
    let finish!: (value: typeof storage.prepared) => void;
    storage.prepare.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    act(() => publishFileManagerResult(offer));
    const next = vi.fn((message: ClientMessage) => {
      hook.sent.push(message);
    });
    hook.rerender({
      sender: next,
      host: pc,
      identity: "client",
    });
    await act(async () => {
      finish(storage.prepared);
      await Promise.resolve();
    });
    expect(hook.sent.some((message) => message.type === "file.transfer.cancel")).toBe(false);
    expect(hook.result.current.presentation.active).toBe(true);
    expect(next).toHaveBeenCalledWith(expect.objectContaining({ type: "file.transfer.answer" }));
  });
  it.each(["pc", "key", "client"])("retires a transfer when its %s identity changes", (reason) => {
    const hook = setup();
    hook.rerender({
      sender: hook.send,
      host: {
        ...pc,
        id: reason === "pc" ? "other-pc" : pc.id,
        hostIdentityPublicKey: reason === "key" ? "other-key" : pc.hostIdentityPublicKey,
      },
      identity: reason === "client" ? "other-client" : "client",
    });
    expect(hook.sent.some((message) => message.type === "file.transfer.cancel")).toBe(true);
    expect(hook.result.current.presentation.active).toBe(false);
  });
  it.each(["close", "error"])("fails promptly when the data channel emits %s", async (event) => {
    const hook = await connected(1);
    act(() => TestPeerConnection.latest!.channel.dispatch(event));
    expect(hook.result.current.presentation.active).toBe(false);
    expect(hook.result.current.presentation.message).toContain("channel was lost");
  });
  it("accepts a download result after completed data transport closes", async () => {
    const hook = await connected();
    act(() => {
      TestPeerConnection.latest!.channel.dispatch("close");
      TestPeerConnection.latest!.close();
    });
    await act(async () => {
      publishFileManagerResult(completion);
      await Promise.resolve();
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);
  });
  it("waits for upload commit after the host closes its completed data transport", async () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const send = (message: ClientMessage) => {
      sent.push(message);
    };
    const uploaded = vi.fn();
    const hook = renderHook(() => useFileTransfer(pc, "client", true, send, uploaded));
    act(() => hook.result.current.startUpload(target, new File([], "zero.txt")));
    const request = sent.find((message) => message.type === "file.transfer.start")!;
    await act(async () => {
      publishFileManagerResult({
        type: "file.transfer.start.result",
        operationId: request.operationId!,
        succeeded: true,
        message: "Started",
        transferId: "transfer-a",
      });
      publishFileManagerResult({ ...offer, direction: "upload" });
      await Promise.resolve();
    });
    act(() => {
      TestPeerConnection.latest!.channel.dispatch("close");
      TestPeerConnection.latest!.close();
    });
    expect(hook.result.current.presentation.active).toBe(true);
    expect(sent.some((message) => message.type === "file.transfer.cancel")).toBe(false);
    await act(async () => {
      publishFileManagerResult({ ...completion, direction: "upload" });
      await Promise.resolve();
    });
    expect(uploaded).toHaveBeenCalledWith("left", "report.txt");
  });
  it("opens at most one share operation for a ready transfer", async () => {
    const hook = await ready();
    let finish!: (value: "shared") => void;
    storage.save.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    let pending!: Promise<void>;
    act(() => {
      pending = hook.result.current.saveReadyFile();
      void hook.result.current.saveReadyFile();
    });
    expect(storage.save).toHaveBeenCalledOnce();
    await act(async () => {
      finish("shared");
      await pending;
    });
  });
  it.each(["success", "failure"])(
    "ignores a stale share %s after starting another transfer",
    async (outcome) => {
      const hook = await ready();
      let resolve!: (value: "shared") => void, reject!: (error: Error) => void;
      storage.save.mockImplementationOnce(
        () =>
          new Promise((done, fail) => {
            resolve = done;
            reject = fail;
          }),
      );
      let pending!: Promise<void>;
      act(() => {
        pending = hook.result.current.saveReadyFile();
      });
      await act(() => hook.result.current.discardReadyFile());
      act(() => hook.result.current.startDownload(target));
      await act(async () => {
        if (outcome === "success") {
          resolve("shared");
        } else {
          reject(new Error("share failed"));
        }
        await pending;
      });
      expect(hook.result.current.presentation.active).toBe(true);
      expect(hook.result.current.presentation.message).not.toContain("could not be saved");
    },
  );
  it("does not let old discard cleanup erase a new transfer", async () => {
    const hook = await ready();
    let finish!: () => void;
    storage.remove.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    let pending!: Promise<void>;
    act(() => {
      pending = hook.result.current.discardReadyFile();
    });
    act(() => hook.result.current.startDownload(target));
    await act(async () => {
      finish();
      await pending;
    });
    expect(hook.result.current.presentation.active).toBe(true);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    TestPeerConnection.latest = null;
    storage.remove.mockClear();
    storage.save.mockClear();
    storage.prepare.mockReset();
    storage.prepare.mockResolvedValue(storage.prepared);
    storage.writable.abort.mockClear();
  });

  function startScreenshot(canSend?: (message: ClientMessage) => boolean) {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const send = (message: ClientMessage) => {
      sent.push(message);
      return canSend?.(message);
    };
    const hook = renderHook(() => useFileTransfer(pc, "client", true, send));

    const retry = () => act(() => hook.result.current.startScreenCapture({
      screenOperationId: "screen-request",
      displayId: "display-1-1",
    }));
    retry();
    const request = sent.find((message) => message.type === "file.transfer.start");
    if (!request?.operationId) {
      throw new Error("Missing screenshot request");
    }
    return { ...hook, sent, requestId: request.operationId, retry };
  }

  it("starts a screenshot download bound to the active screen operation and display", () => {
    const { sent, result } = startScreenshot();
    expect(sent.at(-1)).toMatchObject({
      type: "file.transfer.start",
      direction: "download",
      source: "screen-capture",
      screenOperationId: "screen-request",
      displayId: "display-1-1",
      clientSignature: "client-signature",
    });
    expect(result.current.presentation.message).toBe("Capturing…");
  });

  it("releases a screenshot immediately when its request cannot send", () => {
    const send = vi.fn(() => false);
    const hook = startScreenshot(send);
    expect(hook.result.current.presentation).toMatchObject({
      active: false, message: "The screenshot request could not be sent. Try again.",
    });
    expect(hook.sent.map((message) => message.type)).toEqual(["file.transfer.start"]);
    send.mockReturnValue(true);
    hook.retry();
    expect(hook.result.current.presentation.active).toBe(true);
  });

  it("releases the regular file transfer when its request cannot send", () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const hook = renderHook(() => useFileTransfer(pc, "client", true, () => false));
    act(() => hook.result.current.startDownload(target));
    expect(hook.result.current.presentation).toMatchObject({
      active: false, message: "The file transfer request could not be sent. Try again.",
    });
  });

  it.each(["download", "upload"] as const)(
    "releases an unanswered file %s start and ignores its reply after retry",
    (direction) => {
      vi.useFakeTimers();
      vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
      const sent: ClientMessage[] = [];
      const send = (message: ClientMessage) => {
        sent.push(message);
      };
      const hook = renderHook(() => useFileTransfer(pc, "client", true, send));
      const start = () => act(() => {
        if (direction === "download") {
          hook.result.current.startDownload(target);
        } else {
          hook.result.current.startUpload(target, new File(["test"], "upload.txt"));
        }
      });
      try {
        start();
        const first = sent[0];
        if (first?.type !== "file.transfer.start") {
          throw new Error("Missing file transfer request");
        }
        act(() => { vi.advanceTimersByTime(30_000); });
        expect(sent.at(-1)).toMatchObject({
          type: "file.transfer.cancel", requestId: first.operationId,
        });
        expect(hook.result.current.presentation).toMatchObject({
          active: false, message: "The PC did not respond to the file transfer request. Try again.",
        });

        start();
        const replacement = sent.at(-1);
        if (replacement?.type !== "file.transfer.start") {
          throw new Error("Missing replacement request");
        }
        expect(replacement.operationId).not.toBe(first.operationId);
        act(() => publishFileManagerResult({
          type: "file.transfer.start.result", operationId: first.operationId,
          succeeded: false, message: "Old request response",
        }));
        expect(hook.result.current.presentation.active).toBe(true);
        act(() => publishFileManagerResult({
          type: "file.transfer.start.result", operationId: replacement.operationId,
          succeeded: true, message: "Started", transferId: "new-transfer",
        }));
        act(() => { vi.advanceTimersByTime(30_000); });
        expect(sent.at(-1)).toBe(replacement);
        expect(hook.result.current.presentation.active).toBe(true);
      } finally {
        hook.unmount();
        vi.useRealTimers();
      }
    },
  );

  it("cancels an unacknowledged screenshot request and allows a retry", () => {
    vi.useFakeTimers();
    try {
      const hook = startScreenshot();
      act(() => { vi.advanceTimersByTime(30_000); });
      expect(hook.sent.at(-1)).toMatchObject({
        type: "file.transfer.cancel", requestId: hook.requestId,
      });
      expect(hook.result.current.presentation).toMatchObject({
        active: false, message: "The PC did not respond to the screenshot request. Try again.",
      });
      hook.retry();
      expect(hook.sent.at(-1)).toMatchObject({ type: "file.transfer.start" });
      act(() => publishFileManagerResult({
        type: "file.transfer.start.result", operationId: hook.requestId,
        succeeded: true, message: "Late", transferId: "old-transfer",
      }));
      expect(hook.result.current.presentation.message).toBe("Capturing…");
      hook.unmount();
    } finally {
      vi.useRealTimers();
    }
  });

  it("clears the screenshot start deadline on acknowledgment", () => {
    vi.useFakeTimers();
    try {
      const hook = startScreenshot();
      act(() => publishFileManagerResult({
        type: "file.transfer.start.result", operationId: hook.requestId,
        succeeded: true, message: "Started", transferId: "transfer-a",
      }));
      act(() => { vi.advanceTimersByTime(30_000); });
      expect(hook.sent.map((message) => message.type)).toEqual(["file.transfer.start"]);
      expect(hook.result.current.presentation.message).toBe("Preparing…");
      hook.unmount();
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps a fully staged screenshot after the mirror stops", async () => {
    const hook = startScreenshot();
    act(() => publishFileManagerResult({
      type: "file.transfer.start.result", operationId: hook.requestId,
      succeeded: true, message: "Started", transferId: "transfer-a",
    }));
    await act(() => {
      publishFileManagerResult(offer);
      return Promise.resolve();
    });
    await waitFor(() =>
      expect(hook.sent.some((message) => message.type === "file.transfer.answer")).toBe(true),
    );
    await act(() => {
      publishFileManagerResult(completion);
      return Promise.resolve();
    });
    expect(hook.result.current.presentation.readyToSave).toBe(true);

    act(() => hook.result.current.cancelActiveScreenCapture());

    expect(hook.sent.some((message) => message.type === "file.transfer.cancel")).toBe(false);
    expect(storage.remove).not.toHaveBeenCalled();
    expect(hook.result.current.presentation.readyToSave).toBe(true);
  });

  it("cancels a pending start by request ID before the host issues a transfer ID", () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const target: FileTransferTarget = {
      sessionId: "session",
      panel: "left",
      revision: "revision",
      entry: {
        id: "file-a",
        name: "report.txt",
        kind: "file",
        extension: "txt",
        size: 1,
        modifiedUtc: "2026-08-25T00:00:00Z",
        attributes: [],
      },
    };
    let transfer: ReturnType<typeof useFileTransfer> | null = null;
    const send = (message: ClientMessage) => { sent.push(message); };
    function Harness() {
      transfer = useFileTransfer(
        {
          customName: false,
          id: "pc",
          name: "PC",
          url: "https://pc.invalid",
          hostIdentityPublicKey: "host-key",
          transportMode: "secure-direct",
        },
        "client",
        true,
        send,
      );
      return null;
    }
    render(<Harness />);

    act(() => transfer!.startDownload(target));
    const start = sent.find((message) => message.type === "file.transfer.start");
    if (start?.type !== "file.transfer.start") {
      throw new Error("Expected a transfer start.");
    }
    expect(sent.some((message) => message.type === "file.transfer.cancel")).toBe(false);
    act(() => transfer!.cancel());

    expect(sent.at(-1)).toMatchObject({
      type: "file.transfer.cancel",
      requestId: start.operationId,
    });
    expect(sent.at(-1)).not.toHaveProperty("transferId");
  });

  it("publishes transfer failures to the persistent Files status", () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const notice = vi.fn();
    const target: FileTransferTarget = {
      sessionId: "session",
      panel: "left",
      revision: "revision",
      entry: {
        id: "file-a",
        name: "report.txt",
        kind: "file",
        extension: "txt",
        size: 1,
        modifiedUtc: "2026-08-25T00:00:00Z",
        attributes: [],
      },
    };
    let transfer: ReturnType<typeof useFileTransfer> | null = null;
    const send = (message: ClientMessage) => { sent.push(message); };
    function Harness() {
      transfer = useFileTransfer(
        {
          customName: false,
          id: "pc",
          name: "PC",
          url: "https://pc.invalid",
          hostIdentityPublicKey: "host-key",
          transportMode: "secure-direct",
        },
        "client",
        true,
        send,
        undefined,
        notice,
      );
      return null;
    }
    render(<Harness />);

    act(() => transfer!.startDownload(target));
    const start = sent.find((message) => message.type === "file.transfer.start");
    if (start?.type !== "file.transfer.start") {
      throw new Error("Expected a transfer start.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.start.result",
        operationId: start.operationId,
        succeeded: false,
        code: "file-unavailable",
        message: "The selected file is unavailable.",
      }),
    );

    expect(notice).toHaveBeenCalledWith("The selected file is unavailable.", "error");
  });

  it("keeps a canceled iPhone share ready, then clears it after a successful handoff", async () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    const sent: ClientMessage[] = [];
    const send = (message: ClientMessage) => { sent.push(message); };
    const target: FileTransferTarget = {
      sessionId: "session",
      panel: "left",
      revision: "revision",
      entry: {
        id: "file-a",
        name: "report.txt",
        kind: "file",
        extension: "txt",
        size: 0,
        modifiedUtc: "2026-08-25T00:00:00Z",
        attributes: [],
      },
    };
    let transfer: ReturnType<typeof useFileTransfer> | null = null;
    function Harness() {
      transfer = useFileTransfer(
        {
          customName: false,
          id: "pc",
          name: "PC",
          url: "https://pc.invalid",
          hostIdentityPublicKey: "host-key",
          transportMode: "secure-direct",
        },
        "client",
        true,
        send,
      );
      return (
        <span>{transfer.presentation.readyToSave ? "ready" : transfer.presentation.message}</span>
      );
    }
    render(<Harness />);

    act(() => transfer!.startDownload(target));
    const start = sent.find((message) => message.type === "file.transfer.start");
    if (start?.type !== "file.transfer.start") {
      throw new Error("Expected a transfer start.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.start.result",
        operationId: start.operationId,
        succeeded: true,
        message: "Started.",
        transferId: "transfer-a",
      }),
    );
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.offer",
        transferId: "transfer-a",
        direction: "download",
        fileName: "report.txt",
        declaredSize: 0,
        offerSdp: "offer-sdp",
        hostSignature: "host-signature",
      }),
    );
    await waitFor(() =>
      expect(sent.some((message) => message.type === "file.transfer.answer")).toBe(true),
    );
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.result",
        transferId: "transfer-a",
        direction: "download",
        succeeded: true,
        message: "File ready to save.",
        fileName: "report.txt",
        declaredSize: 0,
      }),
    );

    expect(await screen.findByText("ready")).toBeTruthy();
    expect(storage.remove).not.toHaveBeenCalled();

    storage.save.mockRejectedValueOnce(new DOMException("Canceled", "AbortError"));
    await act(() => transfer!.saveReadyFile());
    expect(screen.getByText("ready")).toBeTruthy();
    expect(storage.remove).not.toHaveBeenCalled();

    storage.save.mockResolvedValueOnce("shared");
    await act(() => transfer!.saveReadyFile());
    expect(storage.save).toHaveBeenCalledTimes(2);
    expect(screen.getByText("Shared.")).toBeTruthy();
    expect(storage.remove).toHaveBeenCalledOnce();

    act(() =>
      transfer!.startDownload({
        ...target,
        entry: { ...target.entry!, id: "file-b", name: "next.txt" },
      }),
    );
    expect(sent.filter((message) => message.type === "file.transfer.start")).toHaveLength(2);
  });

  it("removes storage that finishes preparing after Files has closed", async () => {
    vi.stubGlobal("RTCPeerConnection", TestPeerConnection);
    let releaseStorage: ((value: typeof storage.prepared) => void) | undefined;
    storage.prepare.mockReturnValueOnce(
      new Promise((resolve) => {
        releaseStorage = resolve;
      }),
    );
    const sent: ClientMessage[] = [];
    const send = (message: ClientMessage) => { sent.push(message); };
    const target: FileTransferTarget = {
      sessionId: "session",
      panel: "left",
      revision: "revision",
      entry: {
        id: "file-a",
        name: "report.txt",
        kind: "file",
        extension: "txt",
        size: 0,
        modifiedUtc: "2026-08-25T00:00:00Z",
        attributes: [],
      },
    };
    let transfer: ReturnType<typeof useFileTransfer> | null = null;
    function Harness() {
      transfer = useFileTransfer(
        {
          customName: false,
          id: "pc",
          name: "PC",
          url: "https://pc.invalid",
          hostIdentityPublicKey: "host-key",
          transportMode: "secure-direct",
        },
        "client",
        true,
        send,
      );
      return null;
    }
    const rendered = render(<Harness />);
    act(() => transfer!.startDownload(target));
    const start = sent.find((message) => message.type === "file.transfer.start");
    if (start?.type !== "file.transfer.start") {
      throw new Error("Expected a transfer start.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.start.result",
        operationId: start.operationId,
        succeeded: true,
        message: "Started.",
        transferId: "transfer-a",
      }),
    );
    act(() =>
      publishFileManagerResult({
        type: "file.transfer.offer",
        transferId: "transfer-a",
        direction: "download",
        fileName: "report.txt",
        declaredSize: 0,
        offerSdp: "offer-sdp",
        hostSignature: "host-signature",
      }),
    );
    await waitFor(() => expect(storage.prepare).toHaveBeenCalledOnce());

    rendered.unmount();
    act(() => {
      releaseStorage!(storage.prepared);
    });

    await waitFor(() => expect(storage.remove).toHaveBeenCalledOnce());
    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(sent.some((message) => message.type === "file.transfer.answer")).toBe(false);
  });
});
