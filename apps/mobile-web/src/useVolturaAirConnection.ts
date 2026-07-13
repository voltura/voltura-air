import { useEffect, useMemo, useRef, useState } from "react";
import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "./clientEnvironment";
import { applyPcNameFromHost, getWebSocketUrl, upsertPcProfile, type PcProfile } from "./pcProfiles";
import type { ClientMessage } from "./protocol";
import { clearPairTokenFromAddress, loadDeviceName } from "./connection/clientIdentity";
import {
  diagnosticCodeForPairingReason,
  getDisplayPcName,
  getInputAckTimeoutMessage,
  getInputErrorMessage,
  getPcDisconnectedMessage,
  getPcUnavailableMessage,
  normalizeAudioState,
  parseServerMessage
} from "./connection/connectionProtocol";
import { clearStoredSecret, getStoredSecret, handlePairAccepted, shouldClearStoredSecretForRejection } from "./connection/pairingCredentials";
import { useConnectionRuntimeState } from "./connection/useConnectionRuntimeState";
import { usePairedPcActions } from "./connection/usePairedPcActions";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connection/connectionTypes";
import { useConnectionSender } from "./connection/useConnectionSender";
import { useConnectionPersistence } from "./connection/useConnectionPersistence";
import { useInitialConnectionProfileState } from "./connection/useInitialConnectionProfileState";
import { useAwakeControl } from "./connection/useAwakeControl";
import { usePowerControl } from "./connection/usePowerControl";
import { useAppLaunch } from "./connection/useAppLaunch";
import { requestHostState, trySendClientMessage } from "./connection/connectionSocketMessages";
import { getNextHealthCheckDelay, hasExpiredInputAck, staleConnectionMs } from "./connection/connectionHealthPolicy";

const connectionTimeoutMs = 3000;
const healthCheckTimeoutMs = 6500;
const retryDelayMs = 1200;
const displayOffHealthCheckDelayMs = 1000;

export type { PcProfile } from "./pcProfiles";
export type { ConnectionError, ConnectionState } from "./connection/connectionTypes";

export function useVolturaAirConnection() {
  const {
    addressPcProfile,
    clientId,
    effectiveStoredActivePcId,
    initialPairing,
    screenshotMode,
    shouldActivateAddressPc,
    shouldStoreAddressPc,
    storedPcProfiles
  } = useInitialConnectionProfileState();
  const [deviceName, setDeviceName] = useState(() => loadDeviceName(window.location.href));
  const [pairedPcs, setPairedPcs] = useState<PcProfile[]>(() =>
    shouldStoreAddressPc ? upsertPcProfile(storedPcProfiles, addressPcProfile) : storedPcProfiles);

  const [activePcId, setActivePcId] = useState<string | null>(() => initialPairing !== null ? null : shouldActivateAddressPc ? addressPcProfile.id : effectiveStoredActivePcId);
  const [state, setState] = useState<ConnectionState>("connecting");
  const [message, setMessage] = useState("Connecting to PC...");
  const [lastConnectionError, setLastConnectionError] = useState<ConnectionError | null>(null);
  const [pairingAttempt, setPairingAttempt] = useState<PairingAttempt>(() => ({ token: undefined, id: 0 }));
  const socketRef = useRef<WebSocket | null>(null);
  const deviceNameRef = useRef(deviceName);
  const pairingAttemptRef = useRef(pairingAttempt);
  const lastHealthyAtRef = useRef(0);
  const lastUserActivityAtRef = useRef(Date.now());
  const lastMovementAckAtRef = useRef(0);
  const reconnectRef = useRef<(() => void) | null>(null);
  const rescheduleHealthCheckRef = useRef<(() => void) | null>(null);
  const nextInputSequenceRef = useRef(1);
  const pendingInputAcksRef = useRef<Map<number, number>>(new Map());
  const {
    audioState, awakeCapability, clearRuntimeState, hostStatus, powerCapabilities, setAudioState,
    setHostStatus, supportsGestureDebug, supportsInputAckRef, supportsRemoteLaunch, supportsSleep,
    supportsVolumeControl, supportsVolumeControlRef, updateCapabilities, updateHostStatus
  } = useConnectionRuntimeState(pendingInputAcksRef);
  const { requestAudioState, send } = useConnectionSender({
    lastMovementAckAtRef, lastUserActivityAtRef, nextInputSequenceRef, pendingInputAcksRef,
    reconnectRef, rescheduleHealthCheckRef, socketRef, supportsInputAckRef, supportsVolumeControlRef
  });
  const { awakeResult, completeAwakeChange, pendingAwakeChange, requestAwakeChange } = useAwakeControl(state, send);
  const { completePowerAction, pendingPowerAction, powerActionResult, requestPowerAction } = usePowerControl(state, send);
  const { appLaunchResult, completeAppLaunch, pendingAppLaunchId, requestAppLaunch } = useAppLaunch(state, send);
  useConnectionPersistence({
    activePcId,
    clientId,
    deviceName,
    deviceNameRef,
    hasInitialPairing: initialPairing !== null,
    pairedPcs,
    pairingAttempt,
    pairingAttemptRef
  });

  const activePc = useMemo(() => pairedPcs.find((pc) => pc.id === activePcId) ?? null, [activePcId, pairedPcs]);

  useEffect(() => {
    if (state === "paired" && activePc) {
      setMessage(`Connected to ${getDisplayPcName(activePc, "", screenshotMode)}`);
    }
  }, [activePc, screenshotMode, state]);

  useEffect(() => {
    let disposed = false;
    let shouldRetry = true;
    let retryTimer: number | undefined;
    let connectionTimer: number | undefined;
    let healthCheckTimer: number | undefined;
    let healthDeadlineTimer: number | undefined;
    let backgroundSuspended = document.visibilityState === "hidden";
    let hasShownUnavailable = false;

    if (!activePc) {
      clearRuntimeState();
      socketRef.current?.close();
      setHostStatus(null);
      setState("needs-pairing");
      setMessage(pairedPcs.length > 0 ? "Choose a PC or scan a pairing QR." : "Scan the PC pairing QR to pair this app.");
      return () => {
        disposed = true;
        clearTimers();
      };
    }

    const pc = activePc;

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
      clearRuntimeState();
      window.clearTimeout(connectionTimer);
      connectionTimer = undefined;
      clearHealthCheck();
      const unavailableMessage = reason ?? getPcUnavailableMessage(pc, screenshotMode);
      setLastConnectionError({ code, message: unavailableMessage });
      setState("unavailable");
      setMessage(unavailableMessage);

      if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
        socket.close();
      }

      scheduleRetry();
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
        markUnavailable(socket, getInputAckTimeoutMessage(pc, screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
        return;
      }

      if (lastHealthyAtRef.current > 0 && Date.now() - lastHealthyAtRef.current > staleConnectionMs) {
        markUnavailable(socket, getPcUnavailableMessage(pc, screenshotMode), "VAIR-PAIR-STALE-CONNECTION");
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
        () => sendHealthCheck(socket),
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
        markUnavailable(socket, getInputAckTimeoutMessage(pc, screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
        return;
      }

      if (!trySendClientMessage(socket, { type: "health.ping" })) {
        markUnavailable(socket);
        return;
      }

      window.clearTimeout(healthDeadlineTimer);
      healthDeadlineTimer = window.setTimeout(() => markUnavailable(socket), healthCheckTimeoutMs);
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
      const previousSocket = socketRef.current;
      if (previousSocket?.readyState === WebSocket.OPEN || previousSocket?.readyState === WebSocket.CONNECTING) {
        previousSocket.close();
      }
      window.clearTimeout(retryTimer);
      retryTimer = undefined;
      window.clearTimeout(connectionTimer);
      clearHealthCheck();

      if (hasShownUnavailable) {
        const unavailableMessage = getPcUnavailableMessage(pc, screenshotMode);
        setLastConnectionError({ code: "VAIR-PAIR-HOST-UNREACHABLE", message: unavailableMessage });
        setState("unavailable");
        setMessage(unavailableMessage);
      } else {
        setState("connecting");
        setMessage(`Connecting to ${getDisplayPcName(pc, "", screenshotMode)}...`);
      }

      const ws = new WebSocket(getWebSocketUrl(pc));
      socketRef.current = ws;
      connectionTimer = window.setTimeout(() => markUnavailable(ws), connectionTimeoutMs);

      ws.addEventListener("open", () => {
        if (disposed || ws !== socketRef.current) {
          return;
        }

        setState("connecting");
        setMessage(`Connecting to ${getDisplayPcName(pc, "", screenshotMode)}...`);
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
      });

      ws.addEventListener("message", (event) => {
        if (disposed || ws !== socketRef.current) {
          return;
        }

        const response = parseServerMessage(event.data);
        if (!response) {
          return;
        }

        if (response.type === "pair.accepted") {
          touchHealthy();
          handlePairAccepted(response, pc.id);
          updateCapabilities(response.capabilities);
          updateHostStatus(response.host);
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
          setMessage(`Connected to ${getDisplayPcName(pc, response.pcName, screenshotMode)}`);
          if (!requestHostState(ws, supportsVolumeControlRef.current)) {
            markUnavailable(ws);
          }
          scheduleHealthCheck(ws);
          return;
        }

        if (response.type === "pair.rejected") {
          shouldRetry = false;
          const wasStoredReconnectRejected = shouldClearStoredSecretForRejection(response.reason) && pairingAttemptRef.current.token === undefined;
          if (shouldClearStoredSecretForRejection(response.reason)) {
            clearStoredSecret(clientId, pc.id);
          }
          clearRuntimeState();
          const rejectedMessage = `Pairing rejected: ${response.reason}`;
          setLastConnectionError({ code: diagnosticCodeForPairingReason(response.reason), message: rejectedMessage });
          setState(response.reason === "missing-token" || wasStoredReconnectRejected ? "needs-pairing" : "rejected");
          setMessage(wasStoredReconnectRejected ? "Saved pairing was removed on the PC. Scan a fresh QR code to pair again." : rejectedMessage);
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

          updateHostStatus(response.host);
          if (!response.connected) {
            updateCapabilities(response.capabilities, false);
            markUnavailable(ws, getPcDisconnectedMessage(pc, response.message, screenshotMode), "VAIR-PAIR-HOST-DISCONNECTED");
            return;
          }

          updateCapabilities(response.capabilities, true);
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          setMessage(`Connected to ${getDisplayPcName(pc, response.pcName ?? "", screenshotMode)}`);
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
          }
          scheduleHealthCheck(ws);
          return;
        }

        if (response.type === "input.error") {
          touchHealthy();
          if (typeof response.seq === "number") {
            pendingInputAcksRef.current.delete(response.seq);
          }
          const inputErrorMessage = getInputErrorMessage(response.message, pc, screenshotMode);
          setLastConnectionError({ code: response.code ?? "VAIR-PAIR-INPUT-FAILED", message: inputErrorMessage });
          setMessage(inputErrorMessage);
          scheduleHealthCheck(ws);
          return;
        }

        if (response.type === "system.power.result") {
          touchHealthy();
          completePowerAction(response);
          setMessage(response.message);
          setLastConnectionError(response.succeeded
            ? null
            : { code: response.code ?? "VAIR-POWER-EXECUTION-FAILED", message: response.message });
          if (response.action === "displayOff" && response.succeeded) {
            window.clearTimeout(healthCheckTimer);
            healthCheckTimer = window.setTimeout(() => sendHealthCheck(ws), displayOffHealthCheckDelayMs);
          } else {
            scheduleHealthCheck(ws);
          }
          return;
        }

        if (response.type === "awake.result") {
          touchHealthy();
          completeAwakeChange(response);
          setMessage(response.message);
          setLastConnectionError(response.succeeded
            ? null
            : { code: response.code ?? "VAIR-AWAKE-EXECUTION-FAILED", message: response.message });
          scheduleHealthCheck(ws);
          return;
        }

        if (response.type === "app.launch.result") {
          touchHealthy();
          completeAppLaunch(response);
          setMessage(response.message);
          setLastConnectionError(response.succeeded
            ? null
            : { code: response.code ?? "VAIR-APP-LAUNCH-FAILED", message: response.message });
          scheduleHealthCheck(ws);
          return;
        }

        if (response.type === "audio.state") {
          touchHealthy();
          if (supportsVolumeControlRef.current) {
            setAudioState(normalizeAudioState(response));
          }
          scheduleHealthCheck(ws);
        }
      });

      ws.addEventListener("close", () => {
        if (disposed || !shouldRetry || backgroundSuspended || ws !== socketRef.current) {
          return;
        }

        markUnavailable(ws, getPcUnavailableMessage(pc, screenshotMode), "VAIR-PAIR-SOCKET-CLOSED");
      });

      ws.addEventListener("error", () => {
        if (disposed || backgroundSuspended || ws !== socketRef.current) {
          return;
        }

        markUnavailable(ws);
      });
    }

    reconnectRef.current = () => markUnavailable(socketRef.current ?? undefined, getPcUnavailableMessage(pc, screenshotMode), "VAIR-PAIR-CLIENT-SEND-FAILED");
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
      const socket = socketRef.current;
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
      socketRef.current?.close();
    };
  }, [activePc?.id, activePc?.url, clientId, clearRuntimeState, pairedPcs.length, pairingAttempt.id, screenshotMode, updateCapabilities]);

  const {
    addManualPc, beginNewPairing, connectManualPc, disconnectActivePc, forgetPc,
    pairWithToken, renameDevice, renamePc, selectPc, setHostPointerSpeed
  } = usePairedPcActions({
    activePcId, clearRuntimeState, clientId, deviceNameRef, pairedPcs, screenshotMode, send,
    setActivePcId, setDeviceName, setHostStatus, setLastConnectionError, setMessage, setPairedPcs,
    setPairingAttempt, setState, socketRef, state
  });

  return { state, message, send, requestAudioState, requestPowerAction, requestAwakeChange, requestAppLaunch, pendingAppLaunchId, appLaunchResult, pendingPowerAction, powerActionResult, pendingAwakeChange, awakeResult, clientId, deviceName, activePc, pairedPcs, audioState, awakeCapability, powerCapabilities, supportsGestureDebug, supportsSleep, supportsVolumeControl, supportsRemoteLaunch, lastConnectionError, hostStatus, pairWithToken, selectPc, addManualPc, beginNewPairing, connectManualPc, disconnectActivePc, forgetPc, renamePc, renameDevice, setHostPointerSpeed };
}


export { shouldClearStoredSecretForRejection } from "./connection/pairingCredentials";
