import { act, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { publishFileManagerResult } from "../../foundation/connection/fileManagerResultBus";
import type { ClientMessage } from "../../foundation/protocol/messages";

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

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));
vi.mock("../../foundation/webrtc/iceGathering", () => ({
  hasOnlyRelayCandidates: () => true,
  waitForIceGathering: () => Promise.resolve(),
}));
vi.mock("../../foundation/webrtc/sessionCrypto", () => ({
  hashSessionDescription: () => "sdp-hash",
  verifyHostSessionSignature: () => true,
}));
vi.mock("./fileTransferDeviceStorage", () => ({
  prepareDeviceTransferStorage: storage.prepare,
  removeDeviceTransferFile: storage.remove,
  saveOrShareDeviceTransfer: storage.save,
  supportsDeviceTransferStorage: () => true,
  sweepDeviceTransferStorage: () => Promise.resolve(),
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
  localDescription: RTCSessionDescriptionInit | null = { type: "answer", sdp: "answer-sdp" };
  readonly channel = new TestDataChannel();
  private readonly listeners = new Map<string, EventListener[]>();
  constructor() {
    TestPeerConnection.latest = this;
  }
  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  setRemoteDescription() {
    this.listeners
      .get("datachannel")
      ?.forEach((listener) => listener({ channel: this.channel } as unknown as Event));
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
  afterEach(() => {
    vi.unstubAllGlobals();
    TestPeerConnection.latest = null;
    storage.remove.mockClear();
    storage.save.mockClear();
    storage.prepare.mockReset();
    storage.prepare.mockResolvedValue(storage.prepared);
    storage.writable.abort.mockClear();
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
    const send = (message: ClientMessage) => sent.push(message);
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
    const send = (message: ClientMessage) => sent.push(message);
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
    const send = (message: ClientMessage) => sent.push(message);
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
    const send = (message: ClientMessage) => sent.push(message);
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
