import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishTerminalResult } from "../../foundation/connection/terminalResultBus";
import type { ClientMessage, TerminalCapability } from "../../foundation/protocol/messages";
import { createTerminalAcknowledgement } from "../../foundation/terminal/terminalRecords";
import TerminalWorkspace from "./TerminalWorkspace";

const xtermFocus = vi.hoisted(() => vi.fn());
const xtermReset = vi.hoisted(() => vi.fn());
const xtermWrites = vi.hoisted(() => ({
  deferred: false,
  callbacks: [] as (() => void)[],
  payloads: [] as Uint8Array[],
}));
const xtermPaste = vi.hoisted(() => vi.fn());
const xtermScrollLines = vi.hoisted(() => vi.fn());
const copyTextToClipboard = vi.hoisted(() => vi.fn());
const xtermSelect = vi.hoisted(() => vi.fn());
const xtermClearSelection = vi.hoisted(() => vi.fn());
const xtermSelection = vi.hoisted(() => ({ text: "", callback: null as (() => void) | null }));
const xtermOnData = vi.hoisted(() => ({ callback: null as ((data: string) => void) | null }));
const xtermDimensions = vi.hoisted(() => ({ columns: 80, rows: 24 }));
const xtermFitProposal = vi.hoisted(() => ({ available: true, columns: 80, rows: 24 }));
const resizeCallbacks = vi.hoisted(() => new Set<ResizeObserverCallback>());
const animationFrameState = vi.hoisted(() => ({
  nextId: 1,
  callbacks: new Map<number, FrameRequestCallback>(),
}));

vi.mock("@xterm/xterm", () => ({
  Terminal: class {
    get cols() {
      return xtermDimensions.columns;
    }
    get rows() {
      return xtermDimensions.rows;
    }
    options = { fontSize: 14 };
    loadAddon() {
      return undefined;
    }
    open() {
      return undefined;
    }
    onData(callback: (data: string) => void) {
      xtermOnData.callback = callback;
      return { dispose: () => undefined };
    }
    onSelectionChange(callback: () => void) {
      xtermSelection.callback = callback;
      return { dispose: () => undefined };
    }
    hasSelection() {
      return xtermSelection.text.length > 0;
    }
    getSelection() {
      return xtermSelection.text;
    }
    select(column: number, row: number, length: number) {
      xtermSelect(column, row, length);
      xtermSelection.text = "selected terminal text";
      xtermSelection.callback?.();
    }
    clearSelection() {
      xtermClearSelection();
      xtermSelection.text = "";
      xtermSelection.callback?.();
    }
    buffer = { active: { viewportY: 10 } };
    dispose() {
      return undefined;
    }
    focus() {
      xtermFocus();
    }
    paste(text: string) {
      xtermPaste(text);
    }
    scrollLines(lines: number) {
      xtermScrollLines(lines);
    }
    resize(columns: number, rows: number) {
      xtermDimensions.columns = columns;
      xtermDimensions.rows = rows;
    }
    reset() {
      xtermReset();
    }
    write(data: string | Uint8Array, callback?: () => void) {
      if (typeof data !== "string") {
        xtermWrites.payloads.push(data);
      }
      if (callback && xtermWrites.deferred) {
        xtermWrites.callbacks.push(callback);
      } else {
        callback?.();
      }
    }
  },
}));

vi.mock("@xterm/addon-fit", () => ({
  FitAddon: class {
    fit() {
      if (xtermFitProposal.available) {
        xtermDimensions.columns = xtermFitProposal.columns;
        xtermDimensions.rows = xtermFitProposal.rows;
      }
    }
    proposeDimensions() {
      return xtermFitProposal.available
        ? { cols: xtermFitProposal.columns, rows: xtermFitProposal.rows }
        : undefined;
    }
  },
}));

vi.mock("../../foundation/diagnostics/mobileDiagnostics", () => ({
  copyTextToClipboard,
}));

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));

vi.mock("../../foundation/webrtc/sessionCrypto", () => ({
  hashSessionDescription: () => "sdp-hash",
  verifyHostSessionSignature: () => true,
}));

vi.mock("../../foundation/webrtc/iceGathering", () => ({
  hasOnlyRelayCandidates: () => true,
  waitForIceGathering: () => Promise.resolve(),
}));

class FakeDataChannel {
  readonly label = "voltura-terminal";
  binaryType = "arraybuffer";
  bufferedAmount = 0;
  bufferedAmountLowThreshold = 0;
  readyState: RTCDataChannelState = "open";
  readonly send = vi.fn();
  private readonly listeners = new Map<string, Set<(event: Event) => void>>();

  addEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    const callback =
      typeof listener === "function" ? listener : listener.handleEvent.bind(listener);
    const listeners = this.listeners.get(type) ?? new Set();
    listeners.add(callback);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    const callback =
      typeof listener === "function" ? listener : listener.handleEvent.bind(listener);
    this.listeners.get(type)?.delete(callback);
  }

  close() {
    this.readyState = "closed";
    this.emit("close");
  }

  emit(type: string, event = new Event(type)) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class FakePeerConnection {
  static latest: FakePeerConnection | null = null;
  connectionState: RTCPeerConnectionState = "new";
  localDescription: RTCSessionDescriptionInit | null = null;
  private dataChannelListener: ((event: { channel: RTCDataChannel }) => void) | null = null;
  private connectionStateListener: (() => void) | null = null;

  constructor() {
    FakePeerConnection.latest = this;
  }

  addEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    if (type === "datachannel") {
      this.dataChannelListener = listener as unknown as (event: {
        channel: RTCDataChannel;
      }) => void;
    } else if (type === "connectionstatechange") {
      this.connectionStateListener = listener as () => void;
    }
  }

  setRemoteDescription() {
    return Promise.resolve();
  }
  createAnswer() {
    return Promise.resolve({ type: "answer" as const, sdp: "v=0\r\n" });
  }
  setLocalDescription(description: RTCSessionDescriptionInit) {
    this.localDescription = description;
    return Promise.resolve();
  }
  close() {
    if (FakePeerConnection.latest === this) {
      FakePeerConnection.latest = null;
    }
  }
  emitDataChannel(channel: FakeDataChannel) {
    this.dataChannelListener?.({ channel: channel as unknown as RTCDataChannel });
  }
  emitConnectionState(state: RTCPeerConnectionState) {
    this.connectionState = state;
    this.connectionStateListener?.();
  }
}

const terminalId = "0123456789abcdef0123456789abcdef";
const capability: TerminalCapability = {
  enabled: true,
  permissionGranted: true,
  canUse: true,
  requiresRepair: false,
  active: false,
  ownedByClient: false,
  terminalId: null,
  shell: "windows-powershell",
  reconnectGraceSeconds: 900,
};

beforeEach(() => {
  xtermWrites.deferred = false;
  xtermWrites.callbacks.length = 0;
  xtermWrites.payloads.length = 0;
  xtermFocus.mockClear();
  xtermReset.mockClear();
  xtermPaste.mockClear();
  xtermScrollLines.mockClear();
  copyTextToClipboard.mockReset();
  copyTextToClipboard.mockResolvedValue("copied");
  xtermSelect.mockClear();
  xtermClearSelection.mockClear();
  xtermSelection.text = "";
  xtermSelection.callback = null;
  xtermOnData.callback = null;
  xtermDimensions.columns = 80;
  xtermDimensions.rows = 24;
  xtermFitProposal.available = true;
  xtermFitProposal.columns = 80;
  xtermFitProposal.rows = 24;
  resizeCallbacks.clear();
  animationFrameState.nextId = 1;
  animationFrameState.callbacks.clear();
  FakePeerConnection.latest = null;
  vi.stubGlobal("RTCPeerConnection", FakePeerConnection);
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = animationFrameState.nextId++;
    animationFrameState.callbacks.set(id, callback);
    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => {
    animationFrameState.callbacks.delete(id);
  });
  vi.stubGlobal(
    "ResizeObserver",
    class {
      constructor(callback: ResizeObserverCallback) {
        resizeCallbacks.add(callback);
      }
      observe() {
        return undefined;
      }
      disconnect() {
        resizeCallbacks.clear();
      }
    },
  );
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function startRequestId(send: ReturnType<typeof vi.fn<(message: ClientMessage) => boolean | void>>): string {
  const request = send.mock.calls.find(([message]) => message.type === "terminal.start")?.[0];
  if (request?.type !== "terminal.start") {
    throw new Error("Expected Terminal start");
  }
  return request.operationId;
}

describe("TerminalWorkspace", () => {
  const terminalProps = {
    active: true,
    activePc: {
      customName: false,
      hostIdentityPublicKey: "host-key",
      id: "pc-1",
      name: "PC",
      url: "https://pc.test",
    },
    capability,
    clientId: "client-1",
    connectionEpoch: 1,
    state: "paired" as const,
  };

  function renderTerminal(send: (message: ClientMessage) => boolean | void) {
    return render(<TerminalWorkspace {...terminalProps} send={send} />);
  }

  async function startTerminalNegotiation(
    send = vi.fn<(message: ClientMessage) => boolean | void>(),
    turnExpiresAt?: string,
  ) {
    const view = renderTerminal(send);
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    const offer = {
      type: "terminal.offer" as const,
      operationId: startRequestId(send),
      terminalId,
      columns: 80,
      rows: 24,
      acknowledgedOffset: 0,
      offerSdp: "v=0\r\n",
      hostSignature: "host-signature",
      ...(turnExpiresAt ? { turnExpiresAt } : {}),
    };
    await act(async () => {
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: startRequestId(send),
        succeeded: true,
        code: null,
        message: "started",
        terminalId,
      });
      publishTerminalResult(offer);
      await Promise.resolve();
    });
    return { send, offer, view };
  }

  async function connectTerminal(turnExpiresAt?: string) {
    const { send, offer, view } = await startTerminalNegotiation(undefined, turnExpiresAt);
    const peer = FakePeerConnection.latest!;
    vi.spyOn(peer, "close");
    const channel = new FakeDataChannel();
    act(() => {
      peer.emitDataChannel(channel);
      channel.emit("open");
    });
    return { send, peer, channel, offer, view };
  }

  it.each(["start", "attach"])(
    "ignores unsolicited %s results for another operation",
    async (kind) => {
      const { peer } = await connectTerminal();
      act(() =>
        publishTerminalResult({
          type: kind === "start" ? "terminal.start.result" : "terminal.attach.result",
          operationId: "unrelated",
          terminalId: "other-terminal",
          succeeded: false,
          code: "late",
          message: "Old failure",
        }),
      );
      expect(screen.queryByText("Old failure")).toBeNull();
      expect(peer.close).not.toHaveBeenCalled();
      expect(screen.getByText("Windows PowerShell is active.")).toBeTruthy();
    },
  );

  it.each(["unrelated", "duplicate"])(
    "ignores an %s offer without replacing the current peer",
    async (kind) => {
      const { peer, offer } = await connectTerminal();
      await act(async () => {
        await Promise.resolve(
          publishTerminalResult({
            ...offer,
            operationId: kind === "duplicate" ? offer.operationId : "old-offer",
          }),
        );
      });
      expect(FakePeerConnection.latest).toBe(peer);
      expect(peer.close).not.toHaveBeenCalled();
    },
  );

  it("retires an offer that starts beyond output rendered for the current terminal", async () => {
    const { channel, offer, send } = await connectTerminal();
    act(() => channel.emit("close"));
    const attach = send.mock.calls.map(([message]) => message).find((message) => message.type === "terminal.attach")!;
    await act(async () => {
      publishTerminalResult({ ...offer, operationId: attach.operationId, acknowledgedOffset: 1 });
      await Promise.resolve();
    });

    expect(FakePeerConnection.latest).toBeNull();
    expect(screen.getByText("The PC offered an invalid Terminal output offset.")).toBeTruthy();
    await act(async () => {
      publishTerminalResult({ ...offer, operationId: attach.operationId });
      await Promise.resolve();
    });
    expect(FakePeerConnection.latest).toBeNull();
  });

  it("unblocks a start whose control response never arrives", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    renderTerminal(send);
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(
      (screen.getByRole("button", { name: "Start Terminal" }) as HTMLButtonElement).disabled,
    ).toBe(false);
    expect(
      screen.getByText(
        "The PC did not finish the Terminal connection. Check the connection or stop Terminal.",
      ),
    ).toBeTruthy();
  });

  it("unblocks Start immediately when its request cannot be sent", () => {
    const send = vi.fn<(message: ClientMessage) => boolean | void>();
    send.mockReturnValueOnce(false);
    renderTerminal(send);

    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    expect(
      screen.getByText("The Terminal start request could not be sent. Reconnect and try again."),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    expect(send.mock.calls.filter(([message]) => message.type === "terminal.start")).toHaveLength(
      2,
    );
  });

  it("reports a reconnect request that cannot be sent without waiting for the setup timeout", async () => {
    const { channel, send } = await connectTerminal();
    send.mockReturnValueOnce(false);

    act(() => channel.emit("close"));

    expect(
      screen.getByText(
        "The Terminal reconnect request could not be sent. Check the connection or stop Terminal.",
      ),
    ).toBeTruthy();
  });

  it.each(["failed", "closed"] as const)(
    "retains a temporarily disconnected peer and reattaches after it becomes %s",
    async (terminalState) => {
      const { peer, channel, send } = await connectTerminal();

      act(() => peer.emitConnectionState("disconnected"));

      expect(peer.close).not.toHaveBeenCalled();
      expect(
        send.mock.calls.filter(([message]) => message.type === "terminal.attach"),
      ).toHaveLength(0);

      act(() => peer.emitConnectionState(terminalState));
      act(() => channel.emit("close"));

      expect(peer.close).toHaveBeenCalledOnce();
      expect(
        send.mock.calls.filter(([message]) => message.type === "terminal.attach"),
      ).toHaveLength(1);
    },
  );

  it("reattaches on foreground return after its peer closed in the background", async () => {
    const { peer, channel, send } = await connectTerminal();
    const visibility = vi.spyOn(document, "visibilityState", "get");
    visibility.mockReturnValue("hidden");

    act(() => channel.emit("close"));

    expect(peer.close).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "terminal.attach")).toBe(false);
    visibility.mockReturnValue("visible");
    fireEvent(document, new Event("visibilitychange"));
    fireEvent(document, new Event("visibilitychange"));

    expect(send.mock.calls.filter(([message]) => message.type === "terminal.attach")).toHaveLength(
      1,
    );
  });

  it("keeps controls disabled and the attach deadline armed if the initial input flush fails", async () => {
    vi.useFakeTimers();
    const { send } = await startTerminalNegotiation();
    const peer = FakePeerConnection.latest!;
    const channel = new FakeDataChannel();
    channel.send.mockImplementation(() => {
      throw new Error("transport closed");
    });

    act(() => {
      xtermOnData.callback?.("queued input");
      peer.emitDataChannel(channel);
      channel.emit("open");
    });

    expect(send.mock.calls.filter(([message]) => message.type === "terminal.attach")).toHaveLength(
      1,
    );
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Enter" }).disabled).toBe(true);

    act(() => {
      vi.advanceTimersByTime(15_000);
    });

    expect(
      screen.getByText(
        "The PC did not finish the Terminal connection. Check the connection or stop Terminal.",
      ),
    ).toBeTruthy();
  });

  it("expires an attach whose response never arrives", async () => {
    const { channel, send } = await connectTerminal();
    vi.useFakeTimers();
    act(() => channel.emit("close"));
    expect(send.mock.calls.some(([message]) => message.type === "terminal.attach")).toBe(true);
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(
      screen.getByText(
        "The PC did not finish the Terminal connection. Check the connection or stop Terminal.",
      ),
    ).toBeTruthy();
  });

  it("bounds a WebRTC setup that never opens its data channel", async () => {
    const { channel, offer, send } = await connectTerminal();
    vi.useFakeTimers();
    act(() => channel.emit("close"));
    const attach = send.mock.calls
      .filter(([message]) => message.type === "terminal.attach")
      .at(-1)?.[0];
    if (attach?.type !== "terminal.attach") {
      throw new Error("Expected attach");
    }
    await act(async () => {
      await Promise.resolve(publishTerminalResult({ ...offer, operationId: attach.operationId }));
    });
    const pendingPeer = FakePeerConnection.latest!;
    vi.spyOn(pendingPeer, "close");
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(pendingPeer.close).toHaveBeenCalled();
    expect(
      screen.getByText(
        "The PC did not finish the Terminal connection. Check the connection or stop Terminal.",
      ),
    ).toBeTruthy();
  });

  it("retires a stalled attach before the browser's offer finishes", async () => {
    const { channel, offer, send } = await connectTerminal();
    vi.useFakeTimers();
    let finishDescription!: () => void;
    const applyingOffer = new Promise<void>((resolve) => {
      finishDescription = resolve;
    });
    vi.spyOn(FakePeerConnection.prototype, "setRemoteDescription").mockReturnValueOnce(applyingOffer);
    const createAnswer = vi.spyOn(FakePeerConnection.prototype, "createAnswer");
    act(() => channel.emit("close"));
    const attach = send.mock.calls.map(([message]) => message).find((message) => message.type === "terminal.attach")!;
    await act(() => {
      publishTerminalResult({ ...offer, operationId: attach.operationId });
      return Promise.resolve();
    });
    const peer = FakePeerConnection.latest!;
    const close = vi.spyOn(peer, "close");
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    await act(() => {
      finishDescription();
      return applyingOffer;
    });
    expect(close).toHaveBeenCalledOnce();
    expect(createAnswer).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({
      type: "terminal.answer",
      offerOperationId: attach.operationId,
    }));
  });

  it("retires a peer immediately when its answer cannot be sent", async () => {
    const send = vi.fn<(message: ClientMessage) => boolean | void>();
    send.mockImplementation((message) => message.type === "terminal.answer" ? false : undefined);
    const close = vi.spyOn(FakePeerConnection.prototype, "close");
    await startTerminalNegotiation(send);
    expect(close).toHaveBeenCalledOnce();
    expect(screen.getByText(/Terminal answer could not be sent/)).toBeTruthy();
  });

  it("ignores stale Terminal answer errors and closes only the peer whose answer was rejected", async () => {
    const { peer, send } = await connectTerminal();
    const answer = send.mock.calls.map(([message]) => message).find((message) => message.type === "terminal.answer")!;
    const failure = {
      type: "terminal.answer.result" as const,
      operationId: "retired-answer",
      succeeded: false,
      message: "Terminal answer rejected",
    };
    act(() => publishTerminalResult(failure));
    expect(peer.close).not.toHaveBeenCalled();
    expect(screen.getByText("Windows PowerShell is active.")).toBeTruthy();
    act(() => publishTerminalResult({ ...failure, operationId: answer.operationId }));
    expect(peer.close).toHaveBeenCalledOnce();
    expect(screen.getByText(failure.message)).toBeTruthy();
  });

  it("keeps the attach deadline when an answer error from the previous peer arrives", async () => {
    const { channel, peer, send } = await connectTerminal();
    vi.useFakeTimers();
    const answer = send.mock.calls.map(([message]) => message).find((message) => message.type === "terminal.answer")!;
    act(() => channel.emit("close"));
    expect(peer.close).toHaveBeenCalledOnce();
    act(() => publishTerminalResult({
      type: "terminal.answer.result",
      operationId: answer.operationId,
      succeeded: false,
      message: "The previous answer expired.",
    }));
    expect(screen.getByText("Reconnecting Terminal…")).toBeTruthy();
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(peer.close).toHaveBeenCalledOnce();
    expect(screen.getByText(/did not finish the Terminal connection/)).toBeTruthy();
  });

  it("shows errors only for the latest Stop request", async () => {
    const { peer, send } = await connectTerminal();
    const stop = screen.getByRole("button", { name: "Stop" });
    fireEvent.click(stop);
    fireEvent.click(stop);
    const stops = send.mock.calls.map(([message]) => message).filter((message) => message.type === "terminal.stop");
    const failure = {
      type: "terminal.stop.result" as const,
      operationId: stops[0]!.operationId,
      succeeded: false,
      message: "Unable to stop this Terminal",
    };
    act(() => publishTerminalResult(failure));
    expect(screen.getByText("Windows PowerShell is active.")).toBeTruthy();
    act(() => publishTerminalResult({ ...failure, operationId: stops[1]!.operationId }));
    expect(screen.getByText(failure.message)).toBeTruthy();
    expect(peer.close).not.toHaveBeenCalled();
  });

  it("reports a Stop request that cannot be sent and lets Stop be retried", async () => {
    const { peer, send } = await connectTerminal();
    send.mockImplementation((message) => message.type === "terminal.stop" ? false : undefined);
    const stop = screen.getByRole("button", { name: "Stop" });
    fireEvent.click(stop);
    expect(screen.getByText(/Terminal stop request could not be sent/)).toBeTruthy();
    fireEvent.click(stop);
    expect(send.mock.calls.filter(([message]) => message.type === "terminal.stop")).toHaveLength(2);
    expect(peer.close).not.toHaveBeenCalled();
  });

  it("closes duplicate and late data channels without replacing the owned transport", async () => {
    const { send, peer, channel, offer } = await connectTerminal();
    const duplicate = new FakeDataChannel();
    act(() => peer.emitDataChannel(duplicate));
    expect(duplicate.readyState).toBe("closed");
    act(() => xtermOnData.callback?.("a"));
    expect(channel.send).toHaveBeenCalledOnce();
    await act(async () => {
      channel.emit("close");
      const attach = send.mock.calls
        .filter(([message]) => message.type === "terminal.attach")
        .at(-1)?.[0];
      if (attach?.type !== "terminal.attach") {
        throw new Error("Expected a reconnect request");
      }
      publishTerminalResult({ ...offer, operationId: attach.operationId });
      await Promise.resolve();
    });
    const late = new FakeDataChannel();
    act(() => peer.emitDataChannel(late));
    expect(late.readyState).toBe("closed");
  });

  it("recovers when an output acknowledgement send fails", async () => {
    const { send, channel } = await connectTerminal();
    channel.send.mockImplementation(() => {
      throw new Error("transport closed");
    });
    const record = new Uint8Array(10);
    record[0] = 0x12;
    record[9] = 65;
    act(() => channel.emit("message", new MessageEvent("message", { data: record.buffer })));
    expect(send).toHaveBeenCalledWith(
      expect.objectContaining({ type: "terminal.attach", acknowledgedOffset: 1 }),
    );
  });

  it.each(["before", "after"])(
    "renders replayed output once when the original write completes %s the replay",
    async (completionOrder) => {
      const { channel, offer, send } = await connectTerminal();
      const record = new Uint8Array(10);
      record[0] = 0x12;
      record[9] = 65;
      xtermWrites.deferred = true;
      act(() => channel.emit("message", new MessageEvent("message", { data: record.buffer })));
      act(() => channel.emit("close"));
      const attach = send.mock.calls
        .map(([message]) => message)
        .find((message) => message.type === "terminal.attach")!;
      expect(attach.acknowledgedOffset).toBe(0);
      await act(async () => {
        publishTerminalResult({ ...offer, operationId: attach.operationId });
        await Promise.resolve();
      });
      const replacement = new FakeDataChannel();
      act(() => {
        FakePeerConnection.latest!.emitDataChannel(replacement);
        replacement.emit("open");
      });
      const finishOriginalWrite = xtermWrites.callbacks.shift()!;
      const replay = () =>
        replacement.emit("message", new MessageEvent("message", { data: record.buffer }));

      act(completionOrder === "before" ? finishOriginalWrite : replay);
      expect(replacement.send).not.toHaveBeenCalled();
      act(completionOrder === "before" ? replay : finishOriginalWrite);

      expect(xtermWrites.payloads).toEqual([new Uint8Array([65])]);
      expect(channel.send).not.toHaveBeenCalled();
      expect(replacement.send).toHaveBeenCalledExactlyOnceWith(createTerminalAcknowledgement(1));
    },
  );

  it("retires the old channel as soon as a renewal attach has been sent", async () => {
    vi.useFakeTimers();
    const expiresAt = new Date(Date.now() + 6 * 60_000).toISOString();
    const { peer, send } = await connectTerminal(expiresAt);

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    expect(send.mock.calls.filter(([message]) => message.type === "terminal.attach")).toHaveLength(
      1,
    );
    expect(peer.close).toHaveBeenCalledOnce();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Enter" }).disabled).toBe(true);
  });

  it("keeps a new Terminal when the previous session's inactive status arrives", async () => {
    const { send, view } = await connectTerminal();
    view.rerender(
      <TerminalWorkspace
        {...terminalProps}
        capability={{ ...capability, active: true, ownedByClient: true, terminalId }}
        send={send}
      />,
    );
    act(() =>
      publishTerminalResult({ type: "terminal.ended", terminalId, reason: "shell-exited" }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    const newStart = send.mock.calls
      .map(([message]) => message)
      .filter((message) => message.type === "terminal.start")
      .at(-1)!;
    act(() =>
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: newStart.operationId,
        terminalId: "new-terminal-id",
        succeeded: true,
        code: null,
        message: "Started.",
      }),
    );

    view.rerender(<TerminalWorkspace {...terminalProps} send={send} />);

    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Stop" }).disabled).toBe(false);
    expect(screen.queryByText("Terminal ended on the PC.")).toBeNull();
  });

  it("queues the new shell reset behind old output without waiting to send Start", async () => {
    const { channel, send } = await connectTerminal();
    xtermReset.mockClear();
    xtermWrites.deferred = true;
    const record = new Uint8Array(10);
    record[0] = 0x12;
    record[9] = 65;
    act(() => channel.emit("message", new MessageEvent("message", { data: record.buffer })));
    act(() => publishTerminalResult({ type: "terminal.ended", terminalId, reason: "shell-exited" }));
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));

    expect(send.mock.calls.filter(([message]) => message.type === "terminal.start")).toHaveLength(2);
    expect(xtermWrites.callbacks).toHaveLength(2);
    const [oldOutput, resetBarrier] = xtermWrites.callbacks;
    expect(xtermReset).not.toHaveBeenCalled();
    act(oldOutput!);
    expect(xtermReset).not.toHaveBeenCalled();
    expect(channel.send).not.toHaveBeenCalled();
    act(resetBarrier!);
    expect(xtermReset).toHaveBeenCalledOnce();
  });

  it("ignores output write completion from a replaced peer", async () => {
    const { send, channel, offer } = await connectTerminal();
    xtermWrites.deferred = true;
    const record = new Uint8Array(10);
    record[0] = 0x12;
    record[9] = 65;
    act(() => channel.emit("message", new MessageEvent("message", { data: record.buffer })));
    expect(xtermWrites.callbacks).toHaveLength(1);
    await act(async () => {
      channel.emit("close");
      const attach = send.mock.calls
        .filter(([message]) => message.type === "terminal.attach")
        .at(-1)?.[0];
      if (attach?.type !== "terminal.attach") {
        throw new Error("Expected a reconnect request");
      }
      publishTerminalResult({ ...offer, operationId: attach.operationId });
      await Promise.resolve();
    });
    // A real browser can finish rendering buffered xterm output after the peer closes.
    channel.readyState = "open";
    act(() => xtermWrites.callbacks[0]?.());
    expect(channel.send).not.toHaveBeenCalled();
  });
  it("refits after xterm cell metrics become available before starting in portrait", () => {
    xtermFitProposal.available = false;
    xtermFitProposal.columns = 42;
    xtermFitProposal.rows = 30;
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <TerminalWorkspace
        active
        activePc={{
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        }}
        capability={capability}
        clientId="client-1"
        connectionEpoch={1}
        send={send}
        state="paired"
      />,
    );
    const terminalScreen = document.querySelector<HTMLElement>(".terminal-screen")!;
    Object.defineProperty(terminalScreen, "clientWidth", { configurable: true, value: 360 });
    Object.defineProperty(terminalScreen, "clientHeight", { configurable: true, value: 520 });
    xtermFitProposal.available = true;

    act(() => {
      for (let round = 0; round < 3; round++) {
        const callbacks = [...animationFrameState.callbacks.values()];
        animationFrameState.callbacks.clear();
        for (const callback of callbacks) {
          callback(performance.now());
        }
      }
    });
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));

    expect(send).toHaveBeenCalledWith(
      expect.objectContaining({ type: "terminal.start", columns: 80, rows: 30 }),
    );
  });

  it("keeps terminal shortcuts in two fitted rows", () => {
    render(
      <TerminalWorkspace
        active
        activePc={{
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        }}
        capability={capability}
        clientId="client-1"
        connectionEpoch={1}
        send={vi.fn()}
        state="paired"
      />,
    );

    expect(screen.getByRole("button", { name: "Backspace" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Tab" })).toBeTruthy();
    expect(document.querySelectorAll(".terminal-key-row")).toHaveLength(2);

    const terminalScreen = document.querySelector<HTMLElement>(".terminal-screen");
    expect(terminalScreen).not.toBeNull();
    fireEvent.touchStart(terminalScreen!, {
      touches: [{ identifier: 7, clientX: 100, clientY: 100 }],
    });
    fireEvent.touchMove(terminalScreen!, {
      cancelable: true,
      touches: [{ identifier: 7, clientX: 99, clientY: 100 }],
    });
    expect(terminalScreen!.scrollLeft).toBe(0);
    fireEvent.touchMove(terminalScreen!, {
      cancelable: true,
      touches: [{ identifier: 7, clientX: 60, clientY: 100 }],
    });
    expect(terminalScreen!.scrollLeft).toBe(40);
    fireEvent.touchEnd(terminalScreen!, {
      changedTouches: [{ identifier: 7, clientX: 60, clientY: 100 }],
    });
    fireEvent.touchStart(terminalScreen!, {
      touches: [{ identifier: 8, clientX: 100, clientY: 100 }],
    });
    fireEvent.touchMove(terminalScreen!, {
      cancelable: true,
      touches: [{ identifier: 8, clientX: 90, clientY: 150 }],
    });
    expect(xtermScrollLines).toHaveBeenCalledWith(-2);
    expect(terminalScreen!.scrollLeft).toBe(40);

    fireEvent.paste(terminalScreen!, {
      clipboardData: { getData: () => "å".repeat(40_000) },
    });
    const pasted = xtermPaste.mock.calls[0]?.[0] as string;
    expect(new TextEncoder().encode(pasted).length).toBeLessThanOrEqual(64 * 1024);
    expect(screen.getByText("Paste was limited to 64 KiB.")).toBeTruthy();
  });

  it.each(["blur", "pagehide", "hidden", "inactive"])(
    "retires terminal touch selection ownership on %s",
    (boundary) => {
      vi.useFakeTimers();
      const props = {
        active: true,
        activePc: {
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        },
        capability,
        clientId: "client-1",
        connectionEpoch: 1,
        send: vi.fn(),
        state: "paired" as const,
      };
      const view = render(<TerminalWorkspace {...props} />);
      const host = document.querySelector<HTMLElement>(".terminal-screen")!;
      vi.spyOn(host, "getBoundingClientRect").mockReturnValue({
        left: 0,
        top: 0,
        width: 800,
        height: 480,
      } as DOMRect);
      fireEvent.touchStart(host, { touches: [{ identifier: 11, clientX: 100, clientY: 50 }] });
      if (boundary === "inactive") {
        view.rerender(<TerminalWorkspace {...props} active={false} />);
      } else if (boundary === "hidden") {
        vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
        fireEvent(document, new Event("visibilitychange"));
      } else {
        fireEvent(window, new Event(boundary));
      }
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(xtermSelect).not.toHaveBeenCalled();
      fireEvent.touchMove(host, {
        cancelable: true,
        touches: [{ identifier: 11, clientX: 60, clientY: 50 }],
      });
      expect(host.scrollLeft).toBe(0);
      view.unmount();
    },
  );

  it("selects and copies terminal text without turning the gesture into a scroll", async () => {
    vi.useFakeTimers();
    render(
      <TerminalWorkspace
        active
        activePc={{
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        }}
        capability={capability}
        clientId="client-1"
        connectionEpoch={1}
        send={vi.fn()}
        state="paired"
      />,
    );
    const terminalScreen = document.querySelector<HTMLElement>(".terminal-screen")!;
    Object.defineProperty(terminalScreen, "clientWidth", { configurable: true, value: 800 });
    Object.defineProperty(terminalScreen, "clientHeight", { configurable: true, value: 480 });
    vi.spyOn(terminalScreen, "getBoundingClientRect").mockReturnValue({
      bottom: 480,
      height: 480,
      left: 0,
      right: 800,
      top: 0,
      width: 800,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    });

    fireEvent.touchStart(terminalScreen, {
      touches: [{ identifier: 11, clientX: 105, clientY: 50 }],
    });
    act(() => {
      vi.advanceTimersByTime(500);
    });

    expect(xtermSelect).toHaveBeenLastCalledWith(10, 12, 1);
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
    expect(document.querySelectorAll(".terminal-key-row")).toHaveLength(2);

    fireEvent.touchMove(terminalScreen, {
      cancelable: true,
      touches: [{ identifier: 11, clientX: 155, clientY: 50 }],
    });
    expect(xtermSelect).toHaveBeenLastCalledWith(10, 12, 6);
    expect(terminalScreen.scrollLeft).toBe(0);
    expect(xtermScrollLines).not.toHaveBeenCalled();

    xtermSelection.text = "";
    act(() => {
      xtermSelection.callback?.();
    });
    expect(screen.queryByRole("button", { name: "Copy" })).toBeNull();
    expect(screen.getByText("Ready to start Windows PowerShell.")).toBeTruthy();

    xtermSelection.text = "selected terminal text";
    act(() => {
      xtermSelection.callback?.();
    });

    fireEvent.touchEnd(terminalScreen, {
      changedTouches: [{ identifier: 11, clientX: 155, clientY: 50 }],
    });
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    await act(async () => {
      await Promise.resolve();
    });
    expect(copyTextToClipboard).toHaveBeenCalledWith("selected terminal text");
    expect(xtermClearSelection).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });

  it("clears selected text and ignores a stale copy result when the terminal ends", async () => {
    let resolveCopy: ((result: "manual") => void) | undefined;
    copyTextToClipboard.mockReturnValue(
      new Promise<"manual">((resolve) => {
        resolveCopy = resolve;
      }),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <TerminalWorkspace
        active
        activePc={{
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        }}
        capability={capability}
        clientId="client-1"
        connectionEpoch={1}
        send={send}
        state="paired"
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    xtermClearSelection.mockClear();
    xtermSelection.text = "old session output";
    act(() => {
      xtermSelection.callback?.();
    });
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    act(() => {
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: startRequestId(send),
        succeeded: true,
        code: null,
        message: "Terminal started.",
        terminalId,
      });
      publishTerminalResult({ type: "terminal.ended", terminalId, reason: "shell-exited" });
    });

    expect(xtermClearSelection).toHaveBeenCalledOnce();
    expect(screen.queryByRole("button", { name: "Copy" })).toBeNull();

    await act(async () => {
      resolveCopy?.("manual");
      await Promise.resolve();
    });
    xtermSelection.text = "new session output";
    act(() => {
      xtermSelection.callback?.();
    });
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Retry copy" })).toBeNull();
    expect(screen.getByText("Terminal ended: shell-exited.")).toBeTruthy();
  });

  it("waits for a visible authenticated connection to supersede a stale pending attach", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const props = {
      active: true,
      activePc: {
        customName: false,
        hostIdentityPublicKey: "host-key",
        id: "pc-1",
        name: "PC",
        url: "https://pc.test",
      },
      capability,
      clientId: "client-1",
      connectionEpoch: 1,
      send,
      state: "paired" as const,
    };
    const { rerender } = render(<TerminalWorkspace {...props} />);
    const terminalScreen = document.querySelector<HTMLElement>(".terminal-screen")!;
    Object.defineProperty(terminalScreen, "clientWidth", { configurable: true, value: 360 });
    Object.defineProperty(terminalScreen, "clientHeight", { configurable: true, value: 520 });

    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    act(() => {
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: startRequestId(send),
        succeeded: true,
        code: null,
        message: "Terminal started.",
        terminalId,
      });
    });
    rerender(
      <TerminalWorkspace
        {...props}
        capability={{ ...capability, active: true, ownedByClient: true, terminalId }}
      />,
    );

    expect(send.mock.calls.map(([message]) => message.type)).toEqual(["terminal.start"]);

    const visibility = vi.spyOn(document, "visibilityState", "get");
    visibility.mockReturnValue("hidden");
    rerender(
      <TerminalWorkspace
        {...props}
        capability={{ ...capability, active: true, ownedByClient: true, terminalId }}
        connectionEpoch={2}
      />,
    );
    expect(send.mock.calls.map(([message]) => message.type)).toEqual(["terminal.start"]);

    xtermDimensions.columns = 90;
    xtermDimensions.rows = 24;
    xtermFitProposal.columns = 42;
    xtermFitProposal.rows = 30;
    visibility.mockReturnValue("visible");
    rerender(
      <TerminalWorkspace
        {...props}
        capability={{ ...capability, active: true, ownedByClient: true, terminalId }}
        connectionEpoch={3}
      />,
    );
    expect(send.mock.calls.map(([message]) => message.type)).toEqual([
      "terminal.start",
      "terminal.attach",
    ]);
    expect(send.mock.calls[1]?.[0]).toEqual(
      expect.objectContaining({ type: "terminal.attach", columns: 80, rows: 30 }),
    );
  });

  it("reattaches when an open DataChannel throws while sending input", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <TerminalWorkspace
        active
        activePc={{
          customName: false,
          hostIdentityPublicKey: "host-key",
          id: "pc-1",
          name: "PC",
          url: "https://pc.test",
        }}
        capability={capability}
        clientId="client-1"
        connectionEpoch={1}
        send={send}
        state="paired"
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Start Terminal" }));
    act(() => {
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: startRequestId(send),
        succeeded: true,
        code: null,
        message: "Terminal started.",
        terminalId,
      });
      publishTerminalResult({
        type: "terminal.offer",
        operationId: startRequestId(send),
        terminalId,
        columns: 80,
        rows: 24,
        acknowledgedOffset: 0,
        offerSdp: "v=0\r\n",
        hostSignature: "host-signature",
      });
    });
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    const channel = new FakeDataChannel();
    act(() => {
      FakePeerConnection.latest?.emitDataChannel(channel);
      channel.emit("open");
    });
    channel.send.mockImplementation(() => {
      throw new DOMException("closed", "OperationError");
    });

    act(() => xtermOnData.callback?.("a"));

    expect(send.mock.calls.map(([message]) => message.type)).toContain("terminal.attach");
    expect(screen.getByText("Reconnecting Terminal…")).toBeTruthy();
  });

  it("does not fit or send an invalid resize while the workspace is hidden", () => {
    const props = {
      activePc: {
        customName: false,
        hostIdentityPublicKey: "host-key",
        id: "pc-1",
        name: "PC",
        url: "https://pc.test",
      },
      capability,
      clientId: "client-1",
      connectionEpoch: 1,
      send: vi.fn<(message: ClientMessage) => void>(),
      state: "paired" as const,
    };
    const { rerender } = render(<TerminalWorkspace {...props} active />);
    const terminalScreen = document.querySelector<HTMLElement>(".terminal-screen")!;
    Object.defineProperty(terminalScreen, "clientWidth", { configurable: true, value: 320 });
    Object.defineProperty(terminalScreen, "clientHeight", { configurable: true, value: 480 });
    rerender(<TerminalWorkspace {...props} active={false} />);
    xtermDimensions.columns = 2;
    xtermDimensions.rows = 1;

    expect(() => {
      act(() => {
        for (const callback of resizeCallbacks) {
          callback([], {} as ResizeObserver);
        }
      });
    }).not.toThrow();
  });
});
