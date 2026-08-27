import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishTerminalResult } from "../../foundation/connection/terminalResultBus";
import type { ClientMessage, TerminalCapability } from "../../foundation/protocol/messages";
import TerminalWorkspace from "./TerminalWorkspace";

const xtermFocus = vi.hoisted(() => vi.fn());
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
    clear() {
      return undefined;
    }
    write(_data: Uint8Array, callback?: () => void) {
      callback?.();
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

  emit(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new Event(type));
    }
  }
}

class FakePeerConnection {
  static latest: FakePeerConnection | null = null;
  localDescription: RTCSessionDescriptionInit | null = null;
  private dataChannelListener: ((event: { channel: RTCDataChannel }) => void) | null = null;

  constructor() {
    FakePeerConnection.latest = this;
  }

  addEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    if (type === "datachannel") {
      this.dataChannelListener = listener as unknown as (event: {
        channel: RTCDataChannel;
      }) => void;
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
  xtermFocus.mockClear();
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
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("TerminalWorkspace", () => {
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
    xtermSelection.text = "old session output";
    act(() => {
      xtermSelection.callback?.();
    });
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    act(() => {
      publishTerminalResult({
        type: "terminal.start.result",
        operationId: "start-1",
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
        operationId: "start-1",
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
        operationId: "start-1",
        succeeded: true,
        code: null,
        message: "Terminal started.",
        terminalId,
      });
      publishTerminalResult({
        type: "terminal.offer",
        operationId: "start-1",
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
