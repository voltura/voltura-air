import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { parsePairingLink, parsePcUrl } from "./pairingLink";
import { getPcDisplayName } from "./pcDisplayName";
import {
  applyPcNameFromHost,
  createPcProfile,
  forgetPcProfile,
  getEffectiveStoredActivePcId,
  getWebSocketUrl,
  loadActivePcId,
  loadPcProfiles,
  saveActivePcId,
  savePcProfiles,
  renamePcProfile,
  upsertPcProfile,
  type PcProfile
} from "./pcProfiles";
import type { AudioStateMessage, ClientMessage, HostStatusMetadata, PairAcceptedMessage, ServerCapabilities, ServerMessage } from "./protocol";
import { normalizeRemoteMode } from "./remoteSettings";

const clientIdKey = "voltura-air.clientId";
const clientIdQueryParam = "d";
const deviceNameKey = "voltura-air.deviceName";
const deviceNameQueryParam = "n";
const screenshotModeKey = "voltura-air.screenshotMode";
const connectionTimeoutMs = 3000;
const heartbeatIntervalMs = 2500;
const heartbeatTimeoutMs = 5500;
const retryDelayMs = 1200;
const staleConnectionMs = 10000;
const inputAckTimeoutMs = 3500;
const maxPendingInputAcks = 64;

export type ConnectionState = "connecting" | "paired" | "needs-pairing" | "rejected" | "disconnected" | "unavailable";

export type ConnectionError = {
  code: string;
  message: string;
};

export type { PcProfile } from "./pcProfiles";

type ClientInputMessage = Extract<ClientMessage, { type: "pointer.move" | "pointer.button" | "pointer.wheel" | "pointer.zoom" | "keyboard.text" | "keyboard.special" }>;

export function useVolturaAirConnection() {
  const screenshotMode = useMemo(() => isScreenshotMode(window.location.href), []);
  const addressClientId = useMemo(() => getClientIdFromAddress(window.location.href), []);
  const addressPcUrl = useMemo(() => parsePcUrl(window.location.href, window.location.origin), []);
  const initialPairing = useMemo(() => parsePairingLink(window.location.href, window.location.origin), []);
  const addressHasPcUrl = useMemo(() => hasPcUrlInAddress(window.location.href), []);
  const addressPcProfile = useMemo(() => createPcProfile(initialPairing?.pcUrl ?? addressPcUrl), [addressPcUrl, initialPairing?.pcUrl]);
  const storedPcProfiles = useMemo(() => loadPcProfiles(), []);
  const storedActivePcId = useMemo(() => loadActivePcId(), []);
  const effectiveStoredActivePcId = useMemo(
    () => getEffectiveStoredActivePcId(storedActivePcId, storedPcProfiles, addressPcProfile.id, window.location.href),
    [addressPcProfile.id, storedActivePcId, storedPcProfiles]
  );
  const shouldUseAddressPc = initialPairing !== null || addressHasPcUrl || (addressClientId !== null && storedActivePcId === null && storedPcProfiles.length === 0);
  const clientId = useMemo(() => getOrCreateClientId(window.location.href), []);
  const [deviceName, setDeviceName] = useState(() => loadDeviceName(window.location.href));
  const [pairedPcs, setPairedPcs] = useState<PcProfile[]>(() => {
    return shouldUseAddressPc ? upsertPcProfile(storedPcProfiles, addressPcProfile) : storedPcProfiles;
  });

  const [activePcId, setActivePcId] = useState<string | null>(() => shouldUseAddressPc ? addressPcProfile.id : effectiveStoredActivePcId);
  const [state, setState] = useState<ConnectionState>("connecting");
  const [message, setMessage] = useState("Connecting to PC...");
  const [audioState, setAudioState] = useState<AudioStateMessage | null>(null);
  const [supportsGestureDebug, setSupportsGestureDebug] = useState(false);
  const [supportsSleep, setSupportsSleep] = useState(false);
  const [supportsVolumeControl, setSupportsVolumeControl] = useState(false);
  const [lastConnectionError, setLastConnectionError] = useState<ConnectionError | null>(null);
  const [hostStatus, setHostStatus] = useState<HostStatusMetadata | null>(null);
  const [pairingAttempt, setPairingAttempt] = useState<{ token?: string; id: number }>(() => ({
    token: undefined,
    id: 0
  }));
  const socketRef = useRef<WebSocket | null>(null);
  const queueRef = useRef<ClientMessage[]>([]);
  const deviceNameRef = useRef(deviceName);
  const pairingAttemptRef = useRef(pairingAttempt);
  const supportsVolumeControlRef = useRef(false);
  const supportsInputAckRef = useRef(false);
  const lastHealthyAtRef = useRef(0);
  const reconnectRef = useRef<(() => void) | null>(null);
  const nextInputSequenceRef = useRef(1);
  const pendingInputAcksRef = useRef<Map<number, number>>(new Map());

  const activePc = useMemo(() => pairedPcs.find((pc) => pc.id === activePcId) ?? null, [activePcId, pairedPcs]);

  useEffect(() => {
    ensureClientMetadataInAddress(clientId, deviceName);
  }, [clientId, deviceName]);

  useEffect(() => {
    if (initialPairing) {
      clearPairTokenFromAddress();
    }
  }, [initialPairing]);

  useEffect(() => {
    deviceNameRef.current = deviceName;
    localStorage.setItem(deviceNameKey, deviceName);
  }, [deviceName]);

  useEffect(() => {
    pairingAttemptRef.current = pairingAttempt;
  }, [pairingAttempt]);

  useEffect(() => {
    savePcProfiles(pairedPcs);
  }, [pairedPcs]);

  useEffect(() => {
    saveActivePcId(activePcId);
  }, [activePcId]);

  useEffect(() => {
    if (state === "paired" && activePc) {
      setMessage(`Connected to ${getDisplayPcName(activePc, "", screenshotMode)}`);
    }
  }, [activePc, screenshotMode, state]);

  const clearRuntimeState = useCallback(() => {
    queueRef.current = [];
    pendingInputAcksRef.current.clear();
    setAudioState(null);
    setSupportsGestureDebug(false);
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    supportsVolumeControlRef.current = false;
    supportsInputAckRef.current = false;
  }, []);

  const send = useCallback((payload: ClientMessage) => {
    const socket = socketRef.current;
    if (socket?.readyState !== WebSocket.OPEN) {
      return;
    }

    let sequence: number | undefined;
    let payloadToSend: ClientMessage = payload;
    if (supportsInputAckRef.current && shouldTrackInputAck(payload)) {
      sequence = nextInputSequenceRef.current;
      nextInputSequenceRef.current = sequence >= Number.MAX_SAFE_INTEGER ? 1 : sequence + 1;
      payloadToSend = { ...payload, seq: sequence };
      pendingInputAcksRef.current.set(sequence, Date.now());
      trimPendingInputAcks(pendingInputAcksRef.current);
    }

    try {
      socket.send(JSON.stringify(payloadToSend));
    } catch {
      if (sequence !== undefined) {
        pendingInputAcksRef.current.delete(sequence);
      }
      reconnectRef.current?.();
    }
  }, []);

  const updateCapabilities = useCallback((capabilities: ServerCapabilities | undefined, connected = true) => {
    const nextSupportsSleep = connected && hasSleepCapability(capabilities);
    const nextSupportsVolumeControl = connected && hasVolumeCapability(capabilities);
    const nextSupportsInputAck = connected && hasInputAckCapability(capabilities);
    setSupportsGestureDebug(connected && hasGestureDebugCapability(capabilities));
    setSupportsSleep(nextSupportsSleep);
    setSupportsVolumeControl(nextSupportsVolumeControl);
    supportsVolumeControlRef.current = nextSupportsVolumeControl;
    supportsInputAckRef.current = nextSupportsInputAck;
    if (!nextSupportsVolumeControl) {
      setAudioState(null);
    }
    if (!nextSupportsInputAck) {
      pendingInputAcksRef.current.clear();
    }
  }, []);

  function updateHostStatus(metadata: HostStatusMetadata | undefined) {
    const normalized = normalizeHostStatus(metadata);
    if (normalized) {
      setHostStatus(normalized);
    }
  }

  useEffect(() => {
    let disposed = false;
    let shouldRetry = true;
    let retryTimer: number | undefined;
    let connectionTimer: number | undefined;
    let heartbeatTimer: number | undefined;
    let heartbeatDeadlineTimer: number | undefined;
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

    function hasExpiredInputAck() {
      if (!supportsInputAckRef.current || pendingInputAcksRef.current.size === 0) {
        return false;
      }

      const now = Date.now();
      for (const sentAt of pendingInputAcksRef.current.values()) {
        if (now - sentAt > inputAckTimeoutMs) {
          return true;
        }
      }

      return false;
    }

    function clearHeartbeat() {
      window.clearInterval(heartbeatTimer);
      window.clearTimeout(heartbeatDeadlineTimer);
      heartbeatTimer = undefined;
      heartbeatDeadlineTimer = undefined;
    }

    function clearTimers() {
      window.clearTimeout(retryTimer);
      window.clearTimeout(connectionTimer);
      clearHeartbeat();
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

      hasShownUnavailable = true;
      clearRuntimeState();
      window.clearTimeout(connectionTimer);
      connectionTimer = undefined;
      clearHeartbeat();
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
      const socket = socketRef.current;
      if (socket?.readyState !== WebSocket.OPEN) {
        return;
      }

      if (hasExpiredInputAck()) {
        markUnavailable(socket, getInputAckTimeoutMessage(pc, screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
        return;
      }

      if (lastHealthyAtRef.current > 0 && Date.now() - lastHealthyAtRef.current > staleConnectionMs) {
        markUnavailable(socket, getPcUnavailableMessage(pc, screenshotMode), "VAIR-PAIR-STALE-CONNECTION");
      }
    }

    function startHeartbeat(socket: WebSocket) {
      clearHeartbeat();
      heartbeatTimer = window.setInterval(() => {
        if (socket !== socketRef.current || socket.readyState !== WebSocket.OPEN) {
          markUnavailable(socket);
          return;
        }

        if (hasExpiredInputAck()) {
          markUnavailable(socket, getInputAckTimeoutMessage(pc, screenshotMode), "VAIR-PAIR-INPUT-ACK-TIMEOUT");
          return;
        }

        try {
          socket.send(JSON.stringify({ type: "status.ping" }));
        } catch {
          markUnavailable(socket);
          return;
        }

        window.clearTimeout(heartbeatDeadlineTimer);
        heartbeatDeadlineTimer = window.setTimeout(() => markUnavailable(socket), heartbeatTimeoutMs);
      }, heartbeatIntervalMs);
    }

    function connect() {
      if (disposed) {
        return;
      }

      pendingInputAcksRef.current.clear();
      window.clearTimeout(retryTimer);
      retryTimer = undefined;
      window.clearTimeout(connectionTimer);
      clearHeartbeat();

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
          clearPairingAttemptToken();
          window.clearTimeout(connectionTimer);
          connectionTimer = undefined;
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          setMessage(`Connected to ${getDisplayPcName(pc, response.pcName, screenshotMode)}`);
          startHeartbeat(ws);
          flushQueue(ws, queueRef.current);
          return;
        }

        if (response.type === "pair.rejected") {
          shouldRetry = false;
          if (shouldClearStoredSecretForRejection(response.reason)) {
            clearStoredSecret(clientId, pc.id);
          }
          clearRuntimeState();
          const rejectedMessage = `Pairing rejected: ${response.reason}`;
          setLastConnectionError({ code: diagnosticCodeForPairingReason(response.reason), message: rejectedMessage });
          setState(response.reason === "missing-token" ? "needs-pairing" : "rejected");
          setMessage(rejectedMessage);
          ws.close();
          return;
        }

        if (response.type === "status") {
          touchHealthy();
          window.clearTimeout(heartbeatDeadlineTimer);
          heartbeatDeadlineTimer = undefined;
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
          return;
        }

        if (response.type === "status.pong") {
          touchHealthy();
          window.clearTimeout(heartbeatDeadlineTimer);
          heartbeatDeadlineTimer = undefined;
          if (!screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName));
          }
          updateCapabilities(response.capabilities);
          updateHostStatus(response.host);
          hasShownUnavailable = false;
          setLastConnectionError(null);
          setState("paired");
          return;
        }

        if (response.type === "input.ack") {
          touchHealthy();
          if (typeof response.seq === "number") {
            pendingInputAcksRef.current.delete(response.seq);
          }
          return;
        }

        if (response.type === "input.error") {
          if (typeof response.seq === "number") {
            pendingInputAcksRef.current.delete(response.seq);
          }
          markUnavailable(ws, getInputErrorMessage(response.message, pc, screenshotMode), response.code ?? "VAIR-PAIR-INPUT-FAILED");
          return;
        }

        if (response.type === "audio.state") {
          if (supportsVolumeControlRef.current) {
            setAudioState(normalizeAudioState(response));
          }
        }
      });

      ws.addEventListener("close", () => {
        if (disposed || !shouldRetry) {
          return;
        }

        markUnavailable(ws);
      });

      ws.addEventListener("error", () => {
        markUnavailable(ws);
      });
    }

    reconnectRef.current = () => markUnavailable(socketRef.current ?? undefined, getPcUnavailableMessage(pc, screenshotMode), "VAIR-PAIR-CLIENT-SEND-FAILED");
    window.addEventListener("focus", reconnectIfStale);
    document.addEventListener("visibilitychange", reconnectIfStale);
    connect();

    return () => {
      disposed = true;
      reconnectRef.current = null;
      window.removeEventListener("focus", reconnectIfStale);
      document.removeEventListener("visibilitychange", reconnectIfStale);
      clearTimers();
      socketRef.current?.close();
    };
  }, [activePc?.id, activePc?.url, clientId, clearRuntimeState, pairedPcs.length, pairingAttempt.id, screenshotMode, updateCapabilities]);

  function clearPairingAttemptToken() {
    if (pairingAttemptRef.current.token === undefined) {
      return;
    }

    pairingAttemptRef.current = { ...pairingAttemptRef.current, token: undefined };
    setPairingAttempt((current) => current.token === undefined ? current : { ...current, token: undefined });
  }

  const pairWithToken = useCallback((token: string, pcUrl = window.location.origin, requestedDeviceName?: string) => {
    const nextDeviceName = normalizeDeviceNameInput(requestedDeviceName ?? deviceNameRef.current) ?? getDefaultDeviceName();
    deviceNameRef.current = nextDeviceName;
    localStorage.setItem(deviceNameKey, nextDeviceName);
    setDeviceName(nextDeviceName);

    const profile = createPcProfile(pcUrl);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPairedPcs((current) => {
      const next = upsertPcProfile(current, profile);
      savePcProfiles(next);
      return next;
    });
    saveActivePcId(profile.id);
    setActivePcId(profile.id);
    setState("connecting");
    setMessage(`Pairing with ${getDisplayPcName(profile, "", screenshotMode)}...`);
    setPairingAttempt((current) => ({ token, id: current.id + 1 }));
  }, [clearRuntimeState, screenshotMode]);

  const selectPc = useCallback((pcId: string) => {
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setActivePcId(pcId);
  }, [clearRuntimeState]);

  const addManualPc = useCallback((pcUrl: string) => {
    const profile = createPcProfile(pcUrl);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPairedPcs((current) => {
      const next = upsertPcProfile(current, profile);
      savePcProfiles(next);
      return next;
    });
    saveActivePcId(profile.id);
    setActivePcId(profile.id);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("connecting");
    setMessage(`Connecting to ${getDisplayPcName(profile, "", screenshotMode)}...`);
  }, [clearRuntimeState, screenshotMode]);

  const connectManualPc = addManualPc;

  const disconnectActivePc = useCallback(() => {
    if (!activePcId) {
      return;
    }

    clearRuntimeState();
    setLastConnectionError(null);
    socketRef.current?.close();
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("needs-pairing");
    setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
  }, [activePcId, clearRuntimeState]);

  const forgetPc = useCallback((pcId: string) => {
    const pc = pairedPcs.find((profile) => profile.id === pcId) ?? null;
    clearRuntimeState();
    revokePcPairing(pc, clientId, deviceNameRef.current, activePcId === pcId ? socketRef.current : null);
    clearStoredSecret(clientId, pcId);
    setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
    if (activePcId === pcId) {
      setLastConnectionError(null);
      socketRef.current?.close();
      setActivePcId(null);
      setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
      setState("needs-pairing");
      setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
    }
  }, [activePcId, clearRuntimeState, clientId, pairedPcs]);

  const renamePc = useCallback((pcId: string, name: string) => {
    setPairedPcs((current) => renamePcProfile(current, pcId, name));
  }, []);

  const renameDevice = useCallback((name: string) => {
    setDeviceName(name);
    if (state === "paired") {
      send({ type: "device.rename", deviceName: name.trim() || getDefaultDeviceName() });
    }
  }, [send, state]);

  return { state, message, send, clientId, deviceName, activePc, pairedPcs, audioState, supportsGestureDebug, supportsSleep, supportsVolumeControl, lastConnectionError, hostStatus, pairWithToken, selectPc, addManualPc, connectManualPc, disconnectActivePc, forgetPc, renamePc, renameDevice };
}

function getOrCreateClientId(source: string): string {
  const existing = localStorage.getItem(clientIdKey);
  if (existing) {
    return existing;
  }

  const created = getClientIdFromAddress(source) ?? createClientId();
  localStorage.setItem(clientIdKey, created);
  return created;
}

function getClientIdFromAddress(source: string): string | null {
  try {
    const url = new URL(source);
    return normalizeClientId(url.searchParams.get(clientIdQueryParam));
  } catch {
    return normalizeClientId(new URLSearchParams(source).get(clientIdQueryParam));
  }
}

function hasPcUrlInAddress(source: string): boolean {
  try {
    const url = new URL(source);
    return url.searchParams.has("h");
  } catch {
    return new URLSearchParams(source).has("h");
  }
}

function normalizeClientId(value: string | null): string | null {
  const trimmed = value?.trim();
  if (!trimmed || trimmed.length < 8 || trimmed.length > 128 || !/^[a-zA-Z0-9._:-]+$/.test(trimmed)) {
    return null;
  }

  return trimmed;
}

function getDeviceNameFromAddress(source: string): string | null {
  try {
    const url = new URL(source);
    return normalizeDeviceNameInput(url.searchParams.get(deviceNameQueryParam));
  } catch {
    return normalizeDeviceNameInput(new URLSearchParams(source).get(deviceNameQueryParam));
  }
}

function normalizeDeviceNameInput(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed && trimmed.length <= 80 ? trimmed : null;
}

function ensureClientMetadataInAddress(clientId: string, deviceName: string): void {
  try {
    const url = new URL(window.location.href);
    const normalizedDeviceName = deviceName.trim() || getDefaultDeviceName();
    if (url.searchParams.get(clientIdQueryParam) === clientId && url.searchParams.get(deviceNameQueryParam) === normalizedDeviceName) {
      return;
    }

    url.searchParams.set(clientIdQueryParam, clientId);
    url.searchParams.set(deviceNameQueryParam, normalizedDeviceName);
    window.history.replaceState(null, "", url);
  } catch {
  }
}

function createClientId(): string {
  if (crypto.randomUUID) {
    return crypto.randomUUID();
  }

  if (crypto.getRandomValues) {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  return `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function loadDeviceName(source: string): string {
  const existing = normalizeDeviceNameInput(localStorage.getItem(deviceNameKey));
  if (existing) {
    return existing;
  }

  const fromAddress = getDeviceNameFromAddress(source);
  if (fromAddress) {
    localStorage.setItem(deviceNameKey, fromAddress);
    return fromAddress;
  }

  return getDefaultDeviceName();
}

function getDisplayPcName(pc: PcProfile, hostName: string, screenshotMode = false): string {
  if (screenshotMode) {
    return "PC";
  }

  const trimmedHostName = hostName.trim();
  return pc.customName || trimmedHostName.length === 0 ? getPcDisplayName(pc) : trimmedHostName;
}

function getPcUnavailableMessage(pc: PcProfile, screenshotMode = false): string {
  return `${getDisplayPcName(pc, "", screenshotMode)} is currently not available. Check that Voltura Air is running on the PC. Retrying...`;
}

function getPcDisconnectedMessage(pc: PcProfile, reason: string | undefined, screenshotMode = false): string {
  const baseMessage = reason?.trim() || `${getDisplayPcName(pc, "", screenshotMode)} disconnected.`;
  return /retrying/i.test(baseMessage) ? baseMessage : `${baseMessage} Retrying...`;
}

function getInputAckTimeoutMessage(pc: PcProfile, screenshotMode = false): string {
  return `${getDisplayPcName(pc, "", screenshotMode)} stopped confirming input events. Retrying...`;
}

function getInputErrorMessage(reason: string | undefined, pc: PcProfile, screenshotMode = false): string {
  const baseMessage = reason?.trim() || `${getDisplayPcName(pc, "", screenshotMode)} could not process input.`;
  return /retrying/i.test(baseMessage) ? baseMessage : `${baseMessage} Retrying...`;
}

function diagnosticCodeForPairingReason(reason: string): string {
  const normalized = reason.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toUpperCase();
  return `VAIR-PAIR-${normalized || "UNKNOWN"}`;
}

function normalizeHostStatus(metadata: HostStatusMetadata | undefined): HostStatusMetadata | null {
  if (!metadata) {
    return null;
  }

  const normalized: HostStatusMetadata = {
    defaultRemoteMode: metadata.defaultRemoteMode === undefined ? undefined : normalizeRemoteMode(metadata.defaultRemoteMode),
    developerMode: metadata.developerMode === true ? true : undefined,
    developerSessionId: normalizeOptionalString(metadata.developerSessionId),
    hostVersion: normalizeOptionalString(metadata.hostVersion),
    pcName: normalizeOptionalString(metadata.pcName),
    selectedAdapterName: normalizeOptionalString(metadata.selectedAdapterName),
    selectedIp: normalizeOptionalString(metadata.selectedIp),
    selectedPort: Number.isFinite(metadata.selectedPort) ? metadata.selectedPort : undefined,
    webSocketUrl: normalizeOptionalString(metadata.webSocketUrl)
  };

  return Object.values(normalized).some((value) => value !== undefined) ? normalized : null;
}

function normalizeOptionalString(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

function parseServerMessage(data: unknown): ServerMessage | null {
  if (typeof data !== "string") {
    return null;
  }

  try {
    return JSON.parse(data) as ServerMessage;
  } catch {
    return null;
  }
}

function normalizeAudioState(message: AudioStateMessage): AudioStateMessage {
  return {
    type: "audio.state",
    volume: Math.max(0, Math.min(100, Math.round(message.volume))),
    muted: message.muted === true
  };
}

function hasSleepCapability(capabilities: ServerCapabilities | undefined): boolean {
  return capabilities?.sleep === true;
}

function hasVolumeCapability(capabilities: ServerCapabilities | undefined): boolean {
  return capabilities?.volume === true;
}

function hasInputAckCapability(capabilities: ServerCapabilities | undefined): boolean {
  return capabilities?.inputAck === true;
}

function hasGestureDebugCapability(capabilities: ServerCapabilities | undefined): boolean {
  return capabilities?.gestureDebug === true;
}

function shouldTrackInputAck(payload: ClientMessage): payload is ClientInputMessage {
  return payload.type === "pointer.move" ||
    payload.type === "pointer.button" ||
    payload.type === "pointer.wheel" ||
    payload.type === "pointer.zoom" ||
    payload.type === "keyboard.text" ||
    payload.type === "keyboard.special";
}

function trimPendingInputAcks(pending: Map<number, number>): void {
  while (pending.size > maxPendingInputAcks) {
    const oldestSequence = pending.keys().next().value as number | undefined;
    if (oldestSequence === undefined) {
      return;
    }

    pending.delete(oldestSequence);
  }
}

function handlePairAccepted(message: PairAcceptedMessage, pcId: string): void {
  localStorage.setItem(secretKey(message.clientId, pcId), message.secret);
}

function getStoredSecret(clientId: string, pcId: string): string | null {
  return localStorage.getItem(secretKey(clientId, pcId));
}

function clearStoredSecret(clientId: string, pcId: string): void {
  localStorage.removeItem(secretKey(clientId, pcId));
}

export function shouldClearStoredSecretForRejection(reason: string): boolean {
  return reason === "device-revoked" || reason === "secret-revoked";
}

function revokePcPairing(pc: PcProfile | null, clientId: string, deviceName: string, activeSocket: WebSocket | null): void {
  if (!pc) {
    return;
  }

  if (activeSocket?.readyState === WebSocket.OPEN) {
    activeSocket.send(JSON.stringify({ type: "pair.disconnect" }));
    return;
  }

  const secret = getStoredSecret(clientId, pc.id);
  if (!secret) {
    return;
  }

  const socket = new WebSocket(getWebSocketUrl(pc));
  let authenticated = false;

  socket.addEventListener("open", () => {
    socket.send(JSON.stringify({
      type: "pair.hello",
      clientId,
      deviceName: deviceName.trim() || getDefaultDeviceName(),
      platform: getPlatformName(),
      browser: getBrowserName(),
      displayMode: getDisplayMode(),
      secret
    }));
  });

  socket.addEventListener("message", (event) => {
    const response = parseServerMessage(event.data);
    if (response?.type !== "pair.accepted" || authenticated) {
      return;
    }

    authenticated = true;
    socket.send(JSON.stringify({ type: "pair.disconnect" }));
    socket.close();
  });

  socket.addEventListener("error", () => {
    socket.close();
  });
}

function secretKey(clientId: string, pcId: string): string {
  return `voltura-air.secret.${clientId}.${pcId}`;
}

function clearPairTokenFromAddress(): void {
  const url = new URL(window.location.href);
  if (!url.searchParams.has("t")) {
    return;
  }

  url.searchParams.delete("t");
  window.history.replaceState(null, "", url);
}

function isScreenshotMode(source: string): boolean {
  try {
    const url = new URL(source);
    const value = url.searchParams.get("screenshot") ?? url.searchParams.get("screenshotMode");
    if (value) {
      return ["1", "true", "yes"].includes(value.toLowerCase());
    }
  } catch {
  }

  return localStorage.getItem(screenshotModeKey) === "true";
}

function flushQueue(socket: WebSocket, queue: ClientMessage[]): void {
  while (queue.length > 0) {
    const event = queue.shift();
    if (event) {
      socket.send(JSON.stringify(event));
    }
  }
}

function getDefaultDeviceName(): string {
  if (navigator.userAgent.includes("iPad")) {
    return "iPad";
  }

  if (navigator.userAgent.includes("iPhone")) {
    return "iPhone";
  }

  if (/Android/i.test(navigator.userAgent)) {
    return /Tablet|SM-T|Nexus 7|Nexus 10/i.test(navigator.userAgent) ? "Android tablet" : "Android phone";
  }

  return "Mobile device";
}

function getDisplayMode(): "browser" | "installed" | "unknown" {
  if (window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true) {
    return "installed";
  }

  if (window.matchMedia("(display-mode: browser)").matches) {
    return "browser";
  }

  return "unknown";
}

function getPlatformName(): string {
  const userAgent = navigator.userAgent;
  if (/iPad/i.test(userAgent)) {
    return "iPadOS";
  }

  if (/iPhone/i.test(userAgent)) {
    return "iOS";
  }

  if (/Android/i.test(userAgent)) {
    return "Android";
  }

  if (/Windows/i.test(userAgent)) {
    return "Windows";
  }

  if (/Mac OS X/i.test(userAgent)) {
    return "macOS";
  }

  return "Unknown platform";
}

function getBrowserName(): string {
  const userAgent = navigator.userAgent;
  if (/SamsungBrowser/i.test(userAgent)) {
    return "Samsung Internet";
  }

  if (/Edg\//i.test(userAgent)) {
    return "Edge";
  }

  if (/CriOS|Chrome/i.test(userAgent) && !/Edg\//i.test(userAgent)) {
    return "Chrome";
  }

  if (/FxiOS|Firefox/i.test(userAgent)) {
    return "Firefox";
  }

  if (/Safari/i.test(userAgent)) {
    return "Safari";
  }

  return "Unknown browser";
}
