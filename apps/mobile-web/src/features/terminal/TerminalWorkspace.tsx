import { useCallback, useEffect, useRef, useState } from "react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import type { PcProfile } from "../../foundation/connection/useVolturaAirConnection";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { subscribeTerminalResults } from "../../foundation/connection/terminalResultBus";
import type {
  ClientMessage,
  TerminalCapability,
  TerminalOfferMessage,
} from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import {
  hashSessionDescription,
  verifyHostSessionSignature,
} from "../../foundation/webrtc/sessionCrypto";
import { hasOnlyRelayCandidates, waitForIceGathering } from "../../foundation/webrtc/iceGathering";
import {
  createTerminalAcknowledgement,
  createTerminalResize,
  maximumTerminalPasteBytes,
  parseTerminalOutput,
} from "../../foundation/terminal/terminalRecords";
import {
  terminalAnswerTranscript,
  terminalAttachTranscript,
  terminalOfferTranscript,
  terminalStartTranscript,
} from "../../foundation/terminal/terminalTranscripts";
import { applyTerminalModifier, type TerminalModifier } from "./terminalModifiers";
import { limitTerminalPaste, TerminalInputQueue } from "./terminalInputQueue";
import { MomentumScroller, type ScrollAxis } from "../../foundation/input/momentumScroller";
import "./terminal.css";

interface Props {
  active: boolean;
  activePc: PcProfile;
  capability: TerminalCapability;
  clientId: string;
  connectionEpoch: number;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
}

const minimumTerminalColumns = 80;
const terminalSelectionHoldMilliseconds = 500;
const terminalTouchMovementThreshold = 8;

const encoder = new TextEncoder();
const localId = () => crypto.randomUUID().replaceAll("_", "-");

export function TerminalWorkspace({
  active,
  activePc,
  capability,
  clientId,
  connectionEpoch,
  send,
  state,
}: Props) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const terminalRef = useRef<Terminal | null>(null);
  const fitRef = useRef<FitAddon | null>(null);
  const fitAndResizeRef = useRef<() => boolean>(() => false);
  const stopMomentumRef = useRef<() => void>(() => undefined);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const channelRef = useRef<RTCDataChannel | null>(null);
  const terminalIdRef = useRef<string | null>(null);
  const acknowledgedRef = useRef(0);
  const receivedRef = useRef(0);
  const reconnectEpochRef = useRef(-1);
  const connectionEpochRef = useRef(connectionEpoch);
  const pendingAttachRef = useRef<{ epoch: number; operationId: string } | null>(null);
  const observedActiveRef = useRef(false);
  const renewalTimerRef = useRef<number | undefined>(undefined);
  const inputQueueRef = useRef<TerminalInputQueue | null>(null);
  const transportFailureRef = useRef<() => void>(() => undefined);
  const modifierRef = useRef<TerminalModifier | null>(null);
  const selectionGenerationRef = useRef(0);
  const stateRef = useRef(state);
  const activeRef = useRef(active);
  const [modifier, setModifier] = useState<TerminalModifier | null>(null);
  const [status, setStatus] = useState("Ready to start Windows PowerShell.");
  const [running, setRunning] = useState(false);
  const [connecting, setConnecting] = useState(false);
  const [inputReady, setInputReady] = useState(false);
  const [hasSelection, setHasSelection] = useState(false);
  const [selectionCopyFailed, setSelectionCopyFailed] = useState(false);
  const [currentTerminalId, setCurrentTerminalId] = useState<string | null>(null);

  inputQueueRef.current ??= new TerminalInputQueue(() => transportFailureRef.current());

  useEffect(() => {
    stateRef.current = state;
    connectionEpochRef.current = connectionEpoch;
    activeRef.current = active;
  }, [active, connectionEpoch, state]);

  const dimensions = useCallback(
    () => ({
      columns: Math.max(10, Math.min(500, terminalRef.current?.cols ?? 80)),
      rows: Math.max(5, Math.min(300, terminalRef.current?.rows ?? 24)),
    }),
    [],
  );
  const closePeer = useCallback(() => {
    const channel = channelRef.current;
    channelRef.current = null;
    inputQueueRef.current?.disconnect(channel ?? undefined);
    channel?.close();
    setInputReady(false);
    modifierRef.current = null;
    setModifier(null);
    const peer = peerRef.current;
    peerRef.current = null;
    peer?.close();
    if (renewalTimerRef.current !== undefined) {
      window.clearTimeout(renewalTimerRef.current);
    }
    renewalTimerRef.current = undefined;
  }, []);

  const sendBytes = useCallback((bytes: Uint8Array) => {
    if (!terminalIdRef.current) {
      return;
    }
    if (!inputQueueRef.current?.enqueue(bytes)) {
      setStatus("Terminal input paused because its 256 KiB queue is full.");
    }
  }, []);

  useEffect(() => {
    if (!hostRef.current || terminalRef.current) {
      return;
    }
    const terminal = new Terminal({
      cursorBlink: true,
      convertEol: false,
      fontFamily: "Cascadia Mono, Consolas, monospace",
      fontSize: 14,
      scrollback: 5000,
      theme: {
        background: "#07110d",
        foreground: "#e8f3ed",
        cursor: "#6ef2ad",
        selectionBackground: "#2a6b4d",
      },
    });
    const host = hostRef.current;
    const fit = new FitAddon();
    terminal.loadAddon(fit);
    terminal.open(hostRef.current);
    fit.fit();
    terminal.onData((data) => {
      const input = applyTerminalModifier(modifierRef.current, data);
      if (input.consumed) {
        modifierRef.current = null;
        setModifier(null);
        setStatus("Windows PowerShell is active.");
      }
      sendBytes(input.bytes);
    });
    terminal.onSelectionChange(() => {
      selectionGenerationRef.current += 1;
      const selected = terminal.hasSelection();
      setHasSelection(selected);
      if (!selected) {
        setSelectionCopyFailed(false);
      }
    });
    terminalRef.current = terminal;
    fitRef.current = fit;
    const fitAndResize = () => {
      if (!activeRef.current || host.clientWidth <= 0 || host.clientHeight <= 0) {
        return false;
      }
      const proposed = fit.proposeDimensions();
      if (!proposed || proposed.cols < 10 || proposed.rows < 5) {
        return false;
      }
      terminal.resize(Math.max(minimumTerminalColumns, proposed.cols), proposed.rows);
      inputQueueRef.current?.enqueueResize(createTerminalResize(terminal.cols, terminal.rows));
      return true;
    };
    fitAndResizeRef.current = fitAndResize;
    let firstLayoutFrame = 0;
    let secondLayoutFrame = 0;
    const scheduleLayout = () => {
      window.cancelAnimationFrame(firstLayoutFrame);
      window.cancelAnimationFrame(secondLayoutFrame);
      firstLayoutFrame = window.requestAnimationFrame(() => {
        secondLayoutFrame = window.requestAnimationFrame(fitAndResize);
      });
    };
    scheduleLayout();
    let touchIdentifier: number | null = null;
    let previousTouchX = 0;
    let previousTouchY = 0;
    let pendingTouchX = 0;
    let pendingTouchY = 0;
    let pendingTouchDistance = 0;
    let touchAxis: ScrollAxis | null = null;
    let selectionAnchor: { column: number; row: number } | null = null;
    let selectionHoldTimer: number | undefined;
    const resize = new ResizeObserver(() => {
      scheduleLayout();
    });
    resize.observe(hostRef.current);
    const paste = (event: ClipboardEvent) => {
      const text = event.clipboardData?.getData("text");
      if (!text) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      const limited = limitTerminalPaste(text, maximumTerminalPasteBytes);
      terminal.paste(limited.text);
      if (limited.truncated) {
        setStatus("Paste was limited to 64 KiB.");
      }
    };
    window.addEventListener("resize", scheduleLayout);
    window.addEventListener("orientationchange", scheduleLayout);
    window.visualViewport?.addEventListener("resize", scheduleLayout);
    const scrollByDistance = (axis: ScrollAxis, distance: number) => {
      if (axis === "horizontal") {
        host.scrollLeft += distance;
        return;
      }
      pendingTouchDistance += distance;
      const measuredLineHeight = host.clientHeight / Math.max(1, terminal.rows);
      const lineHeight =
        measuredLineHeight > 0 ? measuredLineHeight : (terminal.options.fontSize ?? 14) * 1.2;
      const lines =
        pendingTouchDistance < 0
          ? Math.ceil(pendingTouchDistance / lineHeight)
          : Math.floor(pendingTouchDistance / lineHeight);
      if (lines !== 0) {
        terminal.scrollLines(lines);
        pendingTouchDistance -= lines * lineHeight;
      }
    };
    const momentum = new MomentumScroller(scrollByDistance);
    stopMomentumRef.current = () => momentum.stop();
    const cancelSelectionHold = () => {
      if (selectionHoldTimer !== undefined) {
        window.clearTimeout(selectionHoldTimer);
        selectionHoldTimer = undefined;
      }
    };
    const stopMomentumWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        momentum.stop();
        cancelSelectionHold();
        selectionAnchor = null;
      }
    };
    document.addEventListener("visibilitychange", stopMomentumWhenHidden);
    const cellAt = (clientX: number, clientY: number) => {
      const screen = terminal.element?.querySelector<HTMLElement>(".xterm-screen");
      const bounds = screen?.getBoundingClientRect() ?? host.getBoundingClientRect();
      if (bounds.width <= 0 || bounds.height <= 0) {
        return null;
      }
      const column = Math.max(
        0,
        Math.min(
          terminal.cols - 1,
          Math.floor(((clientX - bounds.left) / bounds.width) * terminal.cols),
        ),
      );
      const viewportRow = Math.max(
        0,
        Math.min(
          terminal.rows - 1,
          Math.floor(((clientY - bounds.top) / bounds.height) * terminal.rows),
        ),
      );
      return { column, row: terminal.buffer.active.viewportY + viewportRow };
    };
    const selectThrough = (cell: { column: number; row: number }) => {
      if (!selectionAnchor) {
        return;
      }
      const anchorOffset = selectionAnchor.row * terminal.cols + selectionAnchor.column;
      const cellOffset = cell.row * terminal.cols + cell.column;
      const start = cellOffset < anchorOffset ? cell : selectionAnchor;
      terminal.select(start.column, start.row, Math.abs(cellOffset - anchorOffset) + 1);
    };
    const touchStart = (event: TouchEvent) => {
      momentum.begin();
      cancelSelectionHold();
      selectionAnchor = null;
      if (event.touches.length !== 1) {
        touchIdentifier = null;
        pendingTouchX = 0;
        pendingTouchY = 0;
        pendingTouchDistance = 0;
        touchAxis = null;
        return;
      }
      const touch = event.touches[0];
      if (!touch) {
        return;
      }
      touchIdentifier = touch.identifier;
      previousTouchX = touch.clientX;
      previousTouchY = touch.clientY;
      pendingTouchX = 0;
      pendingTouchY = 0;
      pendingTouchDistance = 0;
      touchAxis = null;
      const startX = touch.clientX;
      const startY = touch.clientY;
      selectionHoldTimer = window.setTimeout(() => {
        selectionHoldTimer = undefined;
        const anchor = cellAt(startX, startY);
        if (touchIdentifier === touch.identifier && touchAxis === null && anchor) {
          selectionAnchor = anchor;
          terminal.select(anchor.column, anchor.row, 1);
        }
      }, terminalSelectionHoldMilliseconds);
    };
    const touchMove = (event: TouchEvent) => {
      if (touchIdentifier === null) {
        return;
      }
      const touch = Array.from(event.touches).find(
        (candidate) => candidate.identifier === touchIdentifier,
      );
      if (!touch) {
        return;
      }
      if (selectionAnchor) {
        const cell = cellAt(touch.clientX, touch.clientY);
        if (cell) {
          selectThrough(cell);
        }
        if (event.cancelable) {
          event.preventDefault();
        }
        return;
      }
      pendingTouchX += previousTouchX - touch.clientX;
      previousTouchX = touch.clientX;
      pendingTouchY += previousTouchY - touch.clientY;
      previousTouchY = touch.clientY;
      if (
        touchAxis === null &&
        Math.max(Math.abs(pendingTouchX), Math.abs(pendingTouchY)) < terminalTouchMovementThreshold
      ) {
        return;
      }
      cancelSelectionHold();
      touchAxis ??= Math.abs(pendingTouchX) > Math.abs(pendingTouchY) ? "horizontal" : "vertical";
      if (touchAxis === "horizontal") {
        momentum.move("horizontal", pendingTouchX);
        pendingTouchX = 0;
        pendingTouchY = 0;
        if (event.cancelable) {
          event.preventDefault();
        }
        return;
      }
      momentum.move("vertical", pendingTouchY);
      pendingTouchX = 0;
      pendingTouchY = 0;
      if (event.cancelable) {
        event.preventDefault();
      }
    };
    const touchEnd = (event: TouchEvent) => {
      if (
        touchIdentifier !== null &&
        Array.from(event.changedTouches).some((touch) => touch.identifier === touchIdentifier)
      ) {
        cancelSelectionHold();
        if (selectionAnchor) {
          selectionAnchor = null;
        } else {
          momentum.end();
        }
        touchIdentifier = null;
        pendingTouchX = 0;
        pendingTouchY = 0;
        touchAxis = null;
      }
    };
    const touchCancel = () => {
      momentum.stop();
      cancelSelectionHold();
      selectionAnchor = null;
      touchIdentifier = null;
      pendingTouchX = 0;
      pendingTouchY = 0;
      pendingTouchDistance = 0;
      touchAxis = null;
    };
    host.addEventListener("paste", paste, { capture: true });
    host.addEventListener("touchstart", touchStart, { passive: true });
    host.addEventListener("touchmove", touchMove, { passive: false });
    host.addEventListener("touchend", touchEnd, { passive: true });
    host.addEventListener("touchcancel", touchCancel, { passive: true });
    return () => {
      resize.disconnect();
      window.cancelAnimationFrame(firstLayoutFrame);
      window.cancelAnimationFrame(secondLayoutFrame);
      window.removeEventListener("resize", scheduleLayout);
      window.removeEventListener("orientationchange", scheduleLayout);
      window.visualViewport?.removeEventListener("resize", scheduleLayout);
      document.removeEventListener("visibilitychange", stopMomentumWhenHidden);
      cancelSelectionHold();
      host?.removeEventListener("paste", paste, { capture: true });
      host?.removeEventListener("touchstart", touchStart);
      host?.removeEventListener("touchmove", touchMove);
      host?.removeEventListener("touchend", touchEnd);
      host?.removeEventListener("touchcancel", touchCancel);
      momentum.stop();
      stopMomentumRef.current = () => undefined;
      terminal.dispose();
      terminalRef.current = null;
      fitRef.current = null;
      fitAndResizeRef.current = () => false;
      closePeer();
    };
  }, [closePeer, sendBytes]);

  useEffect(() => {
    if (active) {
      let secondFrame: number | undefined;
      const firstFrame = window.requestAnimationFrame(() => {
        secondFrame = window.requestAnimationFrame(() => {
          fitAndResizeRef.current();
          terminalRef.current?.focus();
        });
      });
      return () => {
        window.cancelAnimationFrame(firstFrame);
        if (secondFrame !== undefined) {
          window.cancelAnimationFrame(secondFrame);
        }
      };
    }
    stopMomentumRef.current();
    return undefined;
  }, [active]);

  const attach = useCallback(() => {
    if (document.visibilityState === "hidden") {
      setConnecting(false);
      setStatus("Terminal connection interrupted. Waiting to reconnect…");
      return;
    }
    if (stateRef.current !== "paired") {
      setConnecting(false);
      setStatus("Terminal connection interrupted. Waiting to reconnect…");
      return;
    }
    const terminalId = terminalIdRef.current ?? capability.terminalId;
    if (!terminalId || !activePc.hostIdentityPublicKey) {
      return;
    }
    const epoch = connectionEpochRef.current;
    if (pendingAttachRef.current?.epoch === epoch) {
      return;
    }
    fitAndResizeRef.current();
    const operationId = localId();
    const { columns, rows } = dimensions();
    const transcript = terminalAttachTranscript(
      clientId,
      activePc.hostIdentityPublicKey,
      operationId,
      terminalId,
      acknowledgedRef.current,
      columns,
      rows,
    );
    const signature = signClientPayload(clientId, activePc.id, transcript);
    if (!signature) {
      setStatus("Scan the PC pairing QR again before using Terminal.");
      return;
    }
    terminalIdRef.current = terminalId;
    reconnectEpochRef.current = epoch;
    pendingAttachRef.current = { epoch, operationId };
    receivedRef.current = acknowledgedRef.current;
    setCurrentTerminalId(terminalId);
    setConnecting(true);
    setStatus("Reconnecting Terminal…");
    send({
      type: "terminal.attach",
      operationId,
      terminalId,
      acknowledgedOffset: acknowledgedRef.current,
      columns,
      rows,
      clientSignature: signature,
    });
  }, [activePc, capability.terminalId, clientId, dimensions, send]);

  const acceptOffer = useCallback(
    async (message: TerminalOfferMessage) => {
      if (!activePc.hostIdentityPublicKey || terminalIdRef.current !== message.terminalId) {
        return;
      }
      if (pendingAttachRef.current?.operationId === message.operationId) {
        pendingAttachRef.current = null;
      }
      const offerHash = hashSessionDescription(message.offerSdp);
      const transcript = terminalOfferTranscript(
        clientId,
        activePc.hostIdentityPublicKey,
        message.operationId,
        message.terminalId,
        message.columns,
        message.rows,
        message.acknowledgedOffset,
        offerHash,
      );
      if (
        !verifyHostSessionSignature(
          activePc.hostIdentityPublicKey,
          message.hostSignature,
          transcript,
        )
      ) {
        setStatus("The PC identity signature was invalid.");
        return;
      }
      closePeer();
      let peer: RTCPeerConnection | null = null;
      try {
        const relay = activePc.transportMode === "relay";
        if (relay && !message.iceServers?.length) {
          throw new Error("Relay credentials were unavailable.");
        }
        peer = new RTCPeerConnection({
          iceServers: message.iceServers ?? [],
          iceTransportPolicy: relay ? "relay" : "all",
        });
        peerRef.current = peer;
        peer.addEventListener("datachannel", ({ channel }) => {
          if (channel.label !== "voltura-terminal") {
            channel.close();
            return;
          }
          channel.binaryType = "arraybuffer";
          channelRef.current = channel;
          channel.addEventListener("open", () => {
            inputQueueRef.current?.connect(channel);
            setConnecting(false);
            setRunning(true);
            setInputReady(true);
            setStatus("Windows PowerShell is active.");
            terminalRef.current?.focus();
          });
          channel.addEventListener("close", () => {
            if (terminalIdRef.current && channelRef.current === channel) {
              channelRef.current = null;
              inputQueueRef.current?.disconnect(channel);
              setInputReady(false);
              setConnecting(false);
              setStatus("Terminal connection interrupted. Reconnecting…");
              attach();
            }
          });
          channel.addEventListener("message", (event) => {
            if (!(event.data instanceof ArrayBuffer)) {
              channel.close();
              return;
            }
            const record = parseTerminalOutput(event.data);
            if (!record || record.offset !== receivedRef.current) {
              channel.close();
              setStatus("Terminal output sequence was invalid.");
              return;
            }
            receivedRef.current += record.payload.length;
            const acknowledgedOffset = receivedRef.current;
            terminalRef.current?.write(record.payload, () => {
              if (acknowledgedOffset > acknowledgedRef.current) {
                acknowledgedRef.current = acknowledgedOffset;
              }
              if (channel.readyState === "open" && acknowledgedOffset === acknowledgedRef.current) {
                channel.send(createTerminalAcknowledgement(acknowledgedRef.current));
              }
            });
          });
        });
        await peer.setRemoteDescription({ type: "offer", sdp: message.offerSdp });
        const answer = await peer.createAnswer();
        await peer.setLocalDescription(answer);
        await waitForIceGathering(peer, relay);
        if (peerRef.current !== peer) {
          peer.close();
          return;
        }
        const answerSdp = peer.localDescription?.sdp;
        if (
          !answerSdp ||
          answerSdp.length > 32 * 1024 ||
          (relay && !hasOnlyRelayCandidates(answerSdp))
        ) {
          throw new Error("Invalid Terminal answer.");
        }
        const answerOperationId = localId();
        const signature = signClientPayload(
          clientId,
          activePc.id,
          terminalAnswerTranscript(
            clientId,
            activePc.hostIdentityPublicKey,
            message.operationId,
            answerOperationId,
            message.terminalId,
            offerHash,
            hashSessionDescription(answerSdp),
          ),
        );
        if (!signature) {
          throw new Error("Missing reconnect key.");
        }
        send({
          type: "terminal.answer",
          operationId: answerOperationId,
          offerOperationId: message.operationId,
          terminalId: message.terminalId,
          answerSdp,
          clientSignature: signature,
        });
        if (message.turnExpiresAt) {
          const renewIn = Math.max(
            30_000,
            Date.parse(message.turnExpiresAt) - Date.now() - 5 * 60_000,
          );
          renewalTimerRef.current = window.setTimeout(attach, renewIn);
        }
      } catch (error) {
        if (peer && peerRef.current !== peer) {
          peer.close();
          return;
        }
        closePeer();
        setConnecting(false);
        setStatus(error instanceof Error ? error.message : "Terminal connection failed.");
      }
    },
    [activePc, attach, clientId, closePeer, send],
  );

  useEffect(() => {
    transportFailureRef.current = () => {
      if (!terminalIdRef.current) {
        return;
      }
      closePeer();
      setConnecting(false);
      setStatus("Terminal connection interrupted. Reconnecting…");
      attach();
    };
    return () => {
      transportFailureRef.current = () => undefined;
    };
  }, [attach, closePeer]);

  useEffect(
    () =>
      subscribeTerminalResults((message) => {
        if (message.type === "terminal.start.result" || message.type === "terminal.attach.result") {
          if (!message.succeeded || !message.terminalId) {
            if (message.type === "terminal.attach.result") {
              pendingAttachRef.current = null;
            }
            setConnecting(false);
            setStatus(message.message);
            return;
          }
          terminalIdRef.current = message.terminalId;
          setCurrentTerminalId(message.terminalId);
          setRunning(true);
        } else if (message.type === "terminal.offer") {
          void acceptOffer(message);
        } else if (
          message.type === "terminal.ended" &&
          message.terminalId === terminalIdRef.current
        ) {
          closePeer();
          terminalIdRef.current = null;
          setCurrentTerminalId(null);
          acknowledgedRef.current = 0;
          receivedRef.current = 0;
          reconnectEpochRef.current = -1;
          pendingAttachRef.current = null;
          inputQueueRef.current?.clear();
          terminalRef.current?.clearSelection();
          setRunning(false);
          setConnecting(false);
          setStatus(`Terminal ended: ${message.reason}.`);
        } else if (
          message.type === "terminal.status" &&
          message.terminalId === terminalIdRef.current
        ) {
          setStatus(
            message.state === "active" ? "Windows PowerShell is active." : "Connecting Terminal…",
          );
        } else if (
          (message.type === "terminal.answer.result" || message.type === "terminal.stop.result") &&
          !message.succeeded
        ) {
          setStatus(message.message);
        }
      }),
    [acceptOffer, closePeer],
  );

  useEffect(() => {
    if (capability.active && capability.ownedByClient) {
      observedActiveRef.current = true;
    } else if (
      state === "paired" &&
      observedActiveRef.current &&
      !capability.active &&
      terminalIdRef.current
    ) {
      closePeer();
      terminalIdRef.current = null;
      setCurrentTerminalId(null);
      reconnectEpochRef.current = -1;
      pendingAttachRef.current = null;
      inputQueueRef.current?.clear();
      terminalRef.current?.clearSelection();
      setRunning(false);
      setConnecting(false);
      setStatus("Terminal ended on the PC.");
      observedActiveRef.current = false;
    }
    if (
      state === "paired" &&
      capability.active &&
      capability.ownedByClient &&
      capability.terminalId &&
      terminalIdRef.current &&
      reconnectEpochRef.current !== connectionEpoch &&
      channelRef.current?.readyState !== "open"
    ) {
      attach();
    }
  }, [attach, capability, closePeer, connectionEpoch, currentTerminalId, state]);

  const start = () => {
    if (!activePc.hostIdentityPublicKey) {
      return;
    }
    fitAndResizeRef.current();
    const operationId = localId();
    const { columns, rows } = dimensions();
    const signature = signClientPayload(
      clientId,
      activePc.id,
      terminalStartTranscript(clientId, activePc.hostIdentityPublicKey, operationId, columns, rows),
    );
    if (!signature) {
      setStatus("Scan the PC pairing QR again before using Terminal.");
      return;
    }
    closePeer();
    inputQueueRef.current?.clear();
    terminalRef.current?.clearSelection();
    terminalRef.current?.clear();
    acknowledgedRef.current = 0;
    receivedRef.current = 0;
    reconnectEpochRef.current = connectionEpoch;
    pendingAttachRef.current = null;
    terminalIdRef.current = null;
    setCurrentTerminalId(null);
    setConnecting(true);
    setStatus("Starting Windows PowerShell…");
    send({ type: "terminal.start", operationId, columns, rows, clientSignature: signature });
  };
  const stop = () => {
    const terminalId = terminalIdRef.current;
    if (terminalId) {
      send({ type: "terminal.stop", operationId: localId(), terminalId });
    }
  };
  const key = (value: string) => sendBytes(encoder.encode(value));
  const copySelection = async () => {
    const selectedText = terminalRef.current?.getSelection() ?? "";
    if (!selectedText) {
      return;
    }
    const selectionGeneration = selectionGenerationRef.current;
    const result = await copyTextToClipboard(selectedText);
    if (selectionGenerationRef.current !== selectionGeneration) {
      return;
    }
    if (result === "copied") {
      terminalRef.current?.clearSelection();
    } else {
      setSelectionCopyFailed(true);
    }
  };
  const clearSelection = () => {
    terminalRef.current?.clearSelection();
  };
  const toggleModifier = (next: TerminalModifier) => {
    const value = modifierRef.current === next ? null : next;
    modifierRef.current = value;
    setModifier(value);
    setStatus(
      value === null
        ? "Windows PowerShell is active."
        : `${value === "ctrl" ? "Ctrl" : "Alt"} armed — type one keyboard key.`,
    );
  };

  const unavailable = !capability.enabled
    ? "Terminal is unavailable on this PC."
    : capability.requiresRepair
      ? "Scan this PC's pairing QR again to trust Terminal."
      : !capability.permissionGranted
        ? "Allow Terminal for this device in the PC app."
        : capability.active && !capability.ownedByClient
          ? "Terminal is active on another paired device."
          : null;
  return (
    <section
      className={`terminal-workspace${active ? "" : " terminal-workspace-hidden"}`}
      aria-label="Terminal"
    >
      <div className="terminal-toolbar">
        <span>{status}</span>
        <div>
          {running || connecting ? (
            <button type="button" onClick={stop} disabled={!currentTerminalId}>
              Stop
            </button>
          ) : (
            <button
              type="button"
              onClick={start}
              disabled={state !== "paired" || !capability.canUse || unavailable !== null}
            >
              Start Terminal
            </button>
          )}
        </div>
      </div>
      {unavailable && !running && <div className="terminal-unavailable">{unavailable}</div>}
      <div
        className="terminal-screen"
        ref={hostRef}
        aria-label="Terminal output. Touch and hold, then drag, to select text."
      />
      <div className="terminal-keys" aria-label="Terminal keys">
        <div className="terminal-key-row terminal-key-row-modifiers">
          <button
            type="button"
            aria-label="Ctrl for next key"
            aria-pressed={modifier === "ctrl"}
            disabled={!inputReady}
            onClick={() => toggleModifier("ctrl")}
          >
            Ctrl
          </button>
          <button
            type="button"
            aria-label="Alt for next key"
            aria-pressed={modifier === "alt"}
            disabled={!inputReady}
            onClick={() => toggleModifier("alt")}
          >
            Alt
          </button>
          <button type="button" disabled={!inputReady} onClick={() => key("\u001b")}>
            Esc
          </button>
          <button type="button" disabled={!inputReady} onClick={() => key("\t")}>
            Tab
          </button>
          <button
            type="button"
            aria-label="Backspace"
            disabled={!inputReady}
            onClick={() => key("\u007f")}
          >
            ⌫
          </button>
        </div>
        <div className="terminal-key-row terminal-key-row-actions">
          <button
            type="button"
            aria-label="Left arrow"
            disabled={!inputReady}
            onClick={() => key("\u001b[D")}
          >
            ←
          </button>
          <button
            type="button"
            aria-label="Up arrow"
            disabled={!inputReady}
            onClick={() => key("\u001b[A")}
          >
            ↑
          </button>
          <button
            type="button"
            aria-label="Down arrow"
            disabled={!inputReady}
            onClick={() => key("\u001b[B")}
          >
            ↓
          </button>
          <button
            type="button"
            aria-label="Right arrow"
            disabled={!inputReady}
            onClick={() => key("\u001b[C")}
          >
            →
          </button>
          {hasSelection ? (
            <>
              <button type="button" onClick={() => void copySelection()}>
                {selectionCopyFailed ? "Retry copy" : "Copy"}
              </button>
              <button type="button" onClick={clearSelection}>
                Clear
              </button>
            </>
          ) : (
            <>
              <button type="button" disabled={!inputReady} onClick={() => key("\u0003")}>
                Ctrl+C
              </button>
              <button type="button" disabled={!inputReady} onClick={() => key("\r")}>
                Enter
              </button>
            </>
          )}
        </div>
      </div>
    </section>
  );
}

export default TerminalWorkspace;
