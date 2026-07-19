import { useEffect, useEffectEvent, type Dispatch, type RefObject, type SetStateAction } from "react";
import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "../platform/clientEnvironment";
import type {
  AppLaunchResultMessage,
  AudioStateMessage,
  AwakeResultMessage,
  ClientMessage,
  ClipboardGetResultMessage,
  HostStatusMetadata,
  PresentationCommandResultMessage,
  ServerCapabilities,
  SystemPowerResultMessage,
  TextSendResultMessage,
  UrlOpenResultMessage
} from "../protocol/messages";
import { clearPairTokenFromAddress } from "./clientIdentity";
import { getNextHealthCheckDelay, hasExpiredInputAck, staleConnectionMs } from "./connectionHealthPolicy";
import {
  diagnosticCodeForPairingReason,
  getDisplayPcName,
  getInputAckTimeoutMessage,
  getInputErrorMessage,
  getPcDisconnectedMessage,
  getPcUnavailableMessage,
  normalizeAudioState,
  parseServerMessage
} from "./connectionProtocol";
import { requestHostState, trySendClientMessage } from "./connectionSocketMessages";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connectionTypes";
import { clearStoredSecret, getStoredSecret, handlePairAccepted, shouldClearStoredSecretForRejection } from "./pairingCredentials";
import {
  applyPcNameFromHost,
  getWebSocketUrl,
  saveActivePcId,
  savePcProfiles,
  upsertPcProfile,
  type PcProfile
} from "./pcProfiles";
import type { PendingMovementAck } from "./useConnectionSender";

const connectionTimeoutMs = 3000;
const healthCheckTimeoutMs = 6500;
const retryDelayMs = 1200;
const displayOffHealthCheckDelayMs = 1000;

interface ConnectionSocketLifecycleOptions {
  clearRuntimeStateFromSocket: () => void;
  clientId: string;
  completeAppLaunch: (result: AppLaunchResultMessage) => boolean;
  completeAwakeChange: (result: AwakeResultMessage) => boolean;
  completeClipboardRead: (result: ClipboardGetResultMessage) => boolean;
  completePowerAction: (result: SystemPowerResultMessage) => boolean;
  completePresentationCommand: (result: PresentationCommandResultMessage) => boolean;
  completeTextTransfer: (result: TextSendResultMessage) => boolean;
  completeUrlOpen: (result: UrlOpenResultMessage) => boolean;
  connectionPcId: string | null;
  connectionPcUrl: string | null;
  deviceNameRef: RefObject<string>;
  getLatestConnectionPc: (fallback: PcProfile) => PcProfile;
  lastHealthyAtRef: RefObject<number>;
  lastUserActivityAtRef: RefObject<number>;
  pairingAttemptId: number;
  pairingAttemptRef: RefObject<PairingAttempt>;
  pendingInputAcksRef: RefObject<Map<number, number>>;
  pendingManualPc: PcProfile | null;
  pendingMovementAckRef: RefObject<PendingMovementAck | null>;
  reconnectRef: RefObject<(() => void) | null>;
  rescheduleHealthCheckRef: RefObject<(() => void) | null>;
  screenshotMode: boolean;
  setActivePcId: Dispatch<SetStateAction<string | null>>;
  setAudioStateFromSocket: (state: AudioStateMessage) => void;
  setLastConnectionError: Dispatch<SetStateAction<ConnectionError | null>>;
  setMessage: Dispatch<SetStateAction<string>>;
  setPairedPcs: Dispatch<SetStateAction<PcProfile[]>>;
  setPairingAttempt: Dispatch<SetStateAction<PairingAttempt>>;
  setPendingManualPc: Dispatch<SetStateAction<PcProfile | null>>;
  setState: Dispatch<SetStateAction<ConnectionState>>;
  socketRef: RefObject<WebSocket | null>;
  supportsInputAckRef: RefObject<boolean>;
  supportsVolumeControlRef: RefObject<boolean>;
  updateCapabilitiesFromSocket: (capabilities: ServerCapabilities | undefined, connected?: boolean) => void;
  updateHostStatusFromSocket: (metadata: HostStatusMetadata | undefined) => void;
}

export function useConnectionSocketLifecycle(options: ConnectionSocketLifecycleOptions): void {
  const {
    clearRuntimeStateFromSocket: clearRuntimeState,
    clientId,
    completeAppLaunch: completeAppLaunchState,
    completeAwakeChange: completeAwakeChangeState,
    completeClipboardRead: completeClipboardReadState,
    completePowerAction: completePowerActionState,
    completePresentationCommand: completePresentationCommandState,
    completeTextTransfer: completeTextTransferState,
    completeUrlOpen: completeUrlOpenState,
    connectionPcId,
    connectionPcUrl,
    deviceNameRef,
    getLatestConnectionPc: getLatestConnectionPcState,
    lastHealthyAtRef,
    lastUserActivityAtRef,
    pairingAttemptId,
    pairingAttemptRef,
    pendingInputAcksRef,
    pendingManualPc,
    pendingMovementAckRef,
    reconnectRef,
    rescheduleHealthCheckRef,
    screenshotMode,
    setActivePcId,
    setAudioStateFromSocket: setAudioState,
    setLastConnectionError,
    setMessage,
    setPairedPcs,
    setPairingAttempt,
    setPendingManualPc,
    setState,
    socketRef,
    supportsInputAckRef,
    supportsVolumeControlRef,
    updateCapabilitiesFromSocket: updateCapabilities,
    updateHostStatusFromSocket: updateHostStatus
  } = options;

  const clearRuntimeStateFromSocket = useEffectEvent(clearRuntimeState);
  const completeAppLaunch = useEffectEvent(completeAppLaunchState);
  const completeAwakeChange = useEffectEvent(completeAwakeChangeState);
  const completeClipboardRead = useEffectEvent(completeClipboardReadState);
  const completePowerAction = useEffectEvent(completePowerActionState);
  const completePresentationCommand = useEffectEvent(completePresentationCommandState);
  const completeTextTransfer = useEffectEvent(completeTextTransferState);
  const completeUrlOpen = useEffectEvent(completeUrlOpenState);
  const getLatestConnectionPc = useEffectEvent(getLatestConnectionPcState);
  const setAudioStateFromSocket = useEffectEvent(setAudioState);
  const updateCapabilitiesFromSocket = useEffectEvent(updateCapabilities);
  const updateHostStatusFromSocket = useEffectEvent(updateHostStatus);

  useEffect(() => {
    let disposed = false;
    let shouldRetry = true;
    let retryTimer: number | undefined;
    let connectionTimer: number | undefined;
    let healthCheckTimer: number | undefined;
    let healthDeadlineTimer: number | undefined;
    let removeSocketListeners: (() => void) | undefined;
    let backgroundSuspended = document.visibilityState === "hidden";
    let hasShownUnavailable = false;
  
    if (!connectionPcId || !connectionPcUrl) {
      clearRuntimeStateFromSocket();
      socketRef.current?.close();
      return () => {
        disposed = true;
        clearTimers();
      };
    }
  
    const pc: PcProfile = {
      customName: false,
      id: connectionPcId,
      name: "PC",
      url: connectionPcUrl
    };
    const currentPc = () => getLatestConnectionPc(pc);
    let commitManualPcOnAcceptance = pendingManualPc?.id === pc.id && pendingManualPc.url === pc.url;
  
    function touchHealthy() {
      lastHealthyAtRef.current = Date.now();
    }
  
    function clearHealthCheck() {
      window.clearTimeout(healthCheckTimer);
      window.clearTimeout(healthDeadlineTimer);
      healthCheckTimer = undefined;
      healthDeadlineTimer = undefined;
    }
  
    function clearTimers() {
      window.clearTimeout(retryTimer);
      window.clearTimeout(connectionTimer);
      clearHealthCheck();
      retryTimer = undefined;
      connectionTimer = undefined;
    }
  
    function releaseSocketListeners() {
      removeSocketListeners?.();
      removeSocketListeners = undefined;
    }
  
    function scheduleRetry() {
      if (retryTimer !== undefined || disposed || !shouldRetry) {
        return;
      }
  
      retryTimer = window.setTimeout(connect, retryDelayMs);
    }
  
    function markUnavailable(socket?: WebSocket, reason?: string, code = "VAIR-PAIR-HOST-UNREACHABLE") {
      if (disposed || !shouldRetry) {
        return;
      }
  
      if (socket && socket !== socketRef.current) {
        return;
      }
  
      hasShownUnavailable = true;
      clearRuntimeStateFromSocket();
      window.clearTimeout(connectionTimer);
      connectionTimer = undefined;
      clearHealthCheck();
      const unavailableMessage = reason ?? getPcUnavailableMessage(currentPc(), screenshotMode);
      setLastConnectionError({ code, message: unavailableMessage });
      setState("unavailable");
      setMessage(unavailableMessage);
  
      if (commitManualPcOnAcceptance) {
        commitManualPcOnAcceptance = false;
        shouldRetry = false;
        setPendingManualPc((current) => current?.id === pc.id && current.url === pc.url ? null : current);
      }
  
      if (socket) {
        releaseSocketListeners();
      }
      if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
        socket.close();
      }
  
      if (shouldRetry) {
        scheduleRetry();
      }
    }
  
    function reconnectIfStale() {
      if (backgroundSuspended) {
        return;
      }
  
      const socket = socketRef.current;
      if (!socket || socket.readyState === WebSocket.CLOSED || socket.readyState === WebSocket.CLOSING) {
        connect();
        return;
      }
  
      if (socket.readyState !== WebSocket.OPEN) {
        return;
      }
  
      if (hasExpiredInputAck(pendingInputAcksRef.current.values(), supportsInputAckRef.current)) {
        markUnavailable(socket, getInputAckTimeoutMessage(currentPc(), screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
        return;
      }
  
      if (lastHealthyAtRef.current > 0 && Date.now() - lastHealthyAtRef.current > staleConnectionMs) {
        markUnavailable(socket, getPcUnavailableMessage(currentPc(), screenshotMode), "VAIR-PAIR-STALE-CONNECTION");
        return;
      }
  
      if (!requestHostState(socket, supportsVolumeControlRef.current)) {
        markUnavailable(socket);
        return;
      }
      scheduleHealthCheck(socket);
    }
  
    function scheduleHealthCheck(socket: WebSocket) {
      window.clearTimeout(healthCheckTimer);
      healthCheckTimer = undefined;
      if (disposed || backgroundSuspended || socket !== socketRef.current || socket.readyState !== WebSocket.OPEN) {
        return;
      }
  
      healthCheckTimer = window.setTimeout(
        () => { sendHealthCheck(socket); },
        getNextHealthCheckDelay(
          pendingInputAcksRef.current.size,
          lastUserActivityAtRef.current,
          lastHealthyAtRef.current
        )
      );
    }
  
    function sendHealthCheck(socket: WebSocket) {
      healthCheckTimer = undefined;
      if (disposed || backgroundSuspended) {
        return;
      }
  
      if (socket !== socketRef.current || socket.readyState !== WebSocket.OPEN) {
        return;
      }
  
      if (hasExpiredInputAck(pendingInputAcksRef.current.values(), supportsInputAckRef.current)) {
        markUnavailable(socket, getInputAckTimeoutMessage(currentPc(), screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
        return;
      }
  
      if (!trySendClientMessage(socket, { type: "health.ping" })) {
        markUnavailable(socket);
        return;
      }
  
      window.clearTimeout(healthDeadlineTimer);
      healthDeadlineTimer = window.setTimeout(() => { markUnavailable(socket); }, healthCheckTimeoutMs);
    }
  
    function connect() {
      if (disposed) {
        return;
      }
  
      if (document.visibilityState === "hidden") {
        backgroundSuspended = true;
        clearTimers();
        return;
      }
  
      backgroundSuspended = false;
      pendingInputAcksRef.current.clear();
      pendingMovementAckRef.current = null;
      const previousSocket = socketRef.current;
      releaseSocketListeners();
      if (previousSocket?.readyState === WebSocket.OPEN || previousSocket?.readyState === WebSocket.CONNECTING) {
        previousSocket.close();
      }
      window.clearTimeout(retryTimer);
      retryTimer = undefined;
      window.clearTimeout(connectionTimer);
      clearHealthCheck();
  
      if (hasShownUnavailable) {
        const unavailableMessage = getPcUnavailableMessage(currentPc(), screenshotMode);
        setLastConnectionError({ code: "VAIR-PAIR-HOST-UNREACHABLE", message: unavailableMessage });
        setState("unavailable");
        setMessage(unavailableMessage);
      } else {
        setState("connecting");
        setMessage(`Connecting to ${getDisplayPcName(currentPc(), "", screenshotMode)}...`);
      }
  
      const ws = new WebSocket(getWebSocketUrl(pc));
      socketRef.current = ws;
      connectionTimer = window.setTimeout(() => { markUnavailable(ws); }, connectionTimeoutMs);
  
      function onSocketOpen() {
        if (disposed || ws !== socketRef.current) {
          return;
        }
  
        setState("connecting");
        setMessage(`Connecting to ${getDisplayPcName(currentPc(), "", screenshotMode)}...`);
        const currentPairingAttempt = pairingAttemptRef.current;
        const hello: ClientMessage = {
          type: "pair.hello",
          clientId,
          deviceName: deviceNameRef.current.trim() || getDefaultDeviceName(),
          platform: getPlatformName(),
          browser: getBrowserName(),
          displayMode: getDisplayMode(),
          pairToken: currentPairingAttempt.token,
          secret: getStoredSecret(clientId, pc.id) ?? undefined
        };
        ws.send(JSON.stringify(hello));
      }
  
      function onSocketMessage(event: MessageEvent) {
        if (disposed || ws !== socketRef.current) {
          return;
        }
  
        const response = parseServerMessage(event.data);
        if (!response) {
          return;
        }
  
        if (response.type === "pair.accepted") {
          touchHealthy();
          if (commitManualPcOnAcceptance) {
            commitManualPcOnAcceptance = false;
            const acceptedPc = currentPc();
            setPairedPcs((current) => {
              const next = upsertPcProfile(current, acceptedPc);
              savePcProfiles(next);
              return next;
            });
            saveActivePcId(pc.id);
            setActivePcId(pc.id);
            setPendingManualPc((current) => current?.id === pc.id && current.url === pc.url ? null : current);
          }
          handlePairAccepted(response, pc.id);
          updateCapabilitiesFromSocket(response.capabilities);
          updateHostStatusFromSocket(response.host);
          if (!screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName));
          }
          clearPairTokenFromAddress();
          if (pairingAttemptRef.current.token !== undefined) {
            pairingAttemptRef.current = { ...pairingAttemptRef.current, token: undefined };
            setPairingAttempt((current) => current.token === undefined ? current : { ...current, token: undefined });
          }
          window.clearTimeout(connectionTimer);
          connectionTimer = undefined;
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          setMessage(`Connected to ${getDisplayPcName(currentPc(), response.pcName, screenshotMode)}`);
          if (!requestHostState(ws, supportsVolumeControlRef.current)) {
            markUnavailable(ws);
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "pair.rejected") {
          shouldRetry = false;
          if (commitManualPcOnAcceptance) {
            commitManualPcOnAcceptance = false;
            clearRuntimeStateFromSocket();
            const rejectedMessage = `Pairing rejected: ${response.reason}`;
            setLastConnectionError({ code: diagnosticCodeForPairingReason(response.reason), message: rejectedMessage });
            setState("rejected");
            setMessage(rejectedMessage);
            setPendingManualPc((current) => current?.id === pc.id && current.url === pc.url ? null : current);
            releaseSocketListeners();
            ws.close();
            return;
          }
  
          const wasStoredReconnectRejected = shouldClearStoredSecretForRejection(response.reason) && pairingAttemptRef.current.token === undefined;
          if (shouldClearStoredSecretForRejection(response.reason)) {
            clearStoredSecret(clientId, pc.id);
          }
          clearRuntimeStateFromSocket();
          const rejectedMessage = `Pairing rejected: ${response.reason}`;
          setLastConnectionError({ code: diagnosticCodeForPairingReason(response.reason), message: rejectedMessage });
          setState(response.reason === "missing-token" || wasStoredReconnectRejected ? "needs-pairing" : "rejected");
          setMessage(wasStoredReconnectRejected ? "Saved pairing was removed on the PC. Scan a fresh QR code to pair again." : rejectedMessage);
          releaseSocketListeners();
          ws.close();
          return;
        }
  
        if (response.type === "status") {
          touchHealthy();
          window.clearTimeout(healthDeadlineTimer);
          healthDeadlineTimer = undefined;
          if (response.pcName && !screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName ?? ""));
          }
  
          updateHostStatusFromSocket(response.host);
          if (!response.connected) {
            updateCapabilitiesFromSocket(response.capabilities, false);
            markUnavailable(ws, getPcDisconnectedMessage(currentPc(), response.message, screenshotMode), "VAIR-PAIR-HOST-DISCONNECTED");
            return;
          }
  
          updateCapabilitiesFromSocket(response.capabilities, true);
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          setMessage(`Connected to ${getDisplayPcName(currentPc(), response.pcName ?? "", screenshotMode)}`);
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "health.pong") {
          touchHealthy();
          window.clearTimeout(healthDeadlineTimer);
          healthDeadlineTimer = undefined;
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "input.ack") {
          touchHealthy();
          if (typeof response.seq === "number") {
            pendingInputAcksRef.current.delete(response.seq);
            if (pendingMovementAckRef.current?.sequence === response.seq) {
              pendingMovementAckRef.current = null;
            }
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "input.error") {
          touchHealthy();
          if (typeof response.seq === "number") {
            pendingInputAcksRef.current.delete(response.seq);
            if (pendingMovementAckRef.current?.sequence === response.seq) {
              pendingMovementAckRef.current = null;
            }
          }
          const inputErrorMessage = getInputErrorMessage(response.message, currentPc(), screenshotMode);
          setLastConnectionError({ code: response.code ?? "VAIR-PAIR-INPUT-FAILED", message: inputErrorMessage });
          setMessage(inputErrorMessage);
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "system.power.result") {
          touchHealthy();
          const matched = completePowerAction(response);
          if (matched) {
            setMessage(response.message);
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-POWER-EXECUTION-FAILED", message: response.message });
          }
          if (response.action === "displayOff" && response.succeeded) {
            window.clearTimeout(healthCheckTimer);
            healthCheckTimer = window.setTimeout(() => { sendHealthCheck(ws); }, displayOffHealthCheckDelayMs);
          } else {
            scheduleHealthCheck(ws);
          }
          return;
        }
  
        if (response.type === "presentation.command.result") {
          touchHealthy();
          if (completePresentationCommand(response)) {
            setMessage(response.message);
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-PRESENTATION-COMMAND-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "awake.result") {
          touchHealthy();
          if (completeAwakeChange(response)) {
            setMessage(response.message);
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-AWAKE-EXECUTION-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "app.launch.result") {
          touchHealthy();
          if (completeAppLaunch(response)) {
            setMessage(response.message);
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-APP-LAUNCH-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "url.open.result") {
          touchHealthy();
          if (completeUrlOpen(response)) {
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-URL-OPEN-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "text.send.result") {
          touchHealthy();
          if (completeTextTransfer(response)) {
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-TEXT-DELIVERY-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "clipboard.get.result") {
          touchHealthy();
          if (completeClipboardRead(response)) {
            setLastConnectionError(response.succeeded
              ? null
              : { code: response.code ?? "VAIR-CLIPBOARD-READ-FAILED", message: response.message });
          }
          scheduleHealthCheck(ws);
          return;
        }
  
        if (response.type === "audio.state") {
          touchHealthy();
          if (supportsVolumeControlRef.current) {
            setAudioStateFromSocket(normalizeAudioState(response));
          }
          scheduleHealthCheck(ws);
        }
      }
  
      function onSocketClose() {
        if (disposed || !shouldRetry || backgroundSuspended || ws !== socketRef.current) {
          return;
        }
  
        markUnavailable(ws, getPcUnavailableMessage(currentPc(), screenshotMode), "VAIR-PAIR-SOCKET-CLOSED");
      }
  
      function onSocketError() {
        if (disposed || backgroundSuspended || ws !== socketRef.current) {
          return;
        }
  
        markUnavailable(ws);
      }
  
      removeSocketListeners = registerWebSocketListeners(ws, {
        close: onSocketClose,
        error: onSocketError,
        message: onSocketMessage,
        open: onSocketOpen
      });
    }
  
    reconnectRef.current = () => { markUnavailable(socketRef.current ?? undefined, getPcUnavailableMessage(currentPc(), screenshotMode), "VAIR-PAIR-CLIENT-SEND-FAILED"); };
    rescheduleHealthCheckRef.current = () => {
      const socket = socketRef.current;
      if (socket?.readyState === WebSocket.OPEN) {
        scheduleHealthCheck(socket);
      }
    };
  
    function suspendForBackground() {
      backgroundSuspended = true;
      clearTimers();
      pendingInputAcksRef.current.clear();
      pendingMovementAckRef.current = null;
      const socket = socketRef.current;
      releaseSocketListeners();
      if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
        socket.close();
      }
    }
  
    function resumeFromBackground() {
      if (document.visibilityState === "hidden") {
        suspendForBackground();
        return;
      }
  
      backgroundSuspended = false;
      lastUserActivityAtRef.current = Date.now();
      reconnectIfStale();
    }
  
    window.addEventListener("focus", reconnectIfStale);
    window.addEventListener("pagehide", suspendForBackground);
    window.addEventListener("pageshow", resumeFromBackground);
    document.addEventListener("visibilitychange", resumeFromBackground);
    if (backgroundSuspended) {
      suspendForBackground();
    } else {
      connect();
    }
  
    return () => {
      disposed = true;
      reconnectRef.current = null;
      rescheduleHealthCheckRef.current = null;
      window.removeEventListener("focus", reconnectIfStale);
      window.removeEventListener("pagehide", suspendForBackground);
      window.removeEventListener("pageshow", resumeFromBackground);
      document.removeEventListener("visibilitychange", resumeFromBackground);
      clearTimers();
      releaseSocketListeners();
      socketRef.current?.close();
    };
  }, [
    clientId,
    connectionPcId,
    connectionPcUrl,
    deviceNameRef,
    lastHealthyAtRef,
    lastUserActivityAtRef,
    pairingAttemptId,
    pairingAttemptRef,
    pendingInputAcksRef,
    pendingManualPc?.id,
    pendingManualPc?.url,
    pendingMovementAckRef,
    reconnectRef,
    rescheduleHealthCheckRef,
    screenshotMode,
    setActivePcId,
    setLastConnectionError,
    setMessage,
    setPairedPcs,
    setPairingAttempt,
    setPendingManualPc,
    setState,
    socketRef,
    supportsInputAckRef,
    supportsVolumeControlRef
  ]);
}

interface WebSocketListeners {
  close: () => void;
  error: () => void;
  message: (event: MessageEvent) => void;
  open: () => void;
}

function registerWebSocketListeners(socket: WebSocket, listeners: WebSocketListeners): () => void {
  socket.addEventListener("open", listeners.open);
  socket.addEventListener("message", listeners.message);
  socket.addEventListener("close", listeners.close);
  socket.addEventListener("error", listeners.error);

  return () => {
    socket.removeEventListener("open", listeners.open);
    socket.removeEventListener("message", listeners.message);
    socket.removeEventListener("close", listeners.close);
    socket.removeEventListener("error", listeners.error);
  };
}
