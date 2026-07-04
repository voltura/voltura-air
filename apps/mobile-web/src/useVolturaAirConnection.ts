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
import type { AudioStateMessage, ClientMessage, PairAcceptedMessage, ServerCapabilities, ServerMessage } from "./protocol";

const clientIdKey = "voltura-air.clientId";
const clientIdQueryParam = "d";
const deviceNameKey = "voltura-air.deviceName";
const deviceNameQueryParam = "n";
const screenshotModeKey = "voltura-air.screenshotMode";
const connectionTimeoutMs = 3000;
const heartbeatIntervalMs = 2500;
const heartbeatTimeoutMs = 5500;
const retryDelayMs = 1200;

export type ConnectionState = "connecting" | "paired" | "needs-pairing" | "rejected" | "disconnected" | "unavailable";

export type { PcProfile } from "./pcProfiles";

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

  useEffect(() => {
    ensureClientMetadataInAddress(clientId, deviceName);
  }, [clientId, deviceName]);

  useEffect(() => {
    if (initialPairing) {
      clearPairTokenFromAddress();
    }
  }, [initialPairing]);

  const [activePcId, setActivePcId] = useState<string | null>(() => shouldUseAddressPc ? addressPcProfile.id : effectiveStoredActivePcId);
  const [state, setState] = useState<ConnectionState>("connecting");
  const [message, setMessage] = useState("Connecting to PC...");
  const [audioState, setAudioState] = useState<AudioStateMessage | null>(null);
  const [supportsSleep, setSupportsSleep] = useState(false);
  const [supportsVolumeControl, setSupportsVolumeControl] = useState(false);
  const [pairingAttempt, setPairingAttempt] = useState<{ token?: string; id: number }>(() => ({
    token: undefined,
    id: 0
  }));
  const socketRef = useRef<WebSocket | null>(null);
  const queueRef = useRef<ClientMessage[]>([]);
  const deviceNameRef = useRef(deviceName);
  const supportsVolumeControlRef = useRef(false);

  const activePc = useMemo(() => pairedPcs.find((pc) => pc.id === activePcId) ?? null, [activePcId, pairedPcs]);

  useEffect(() => {
    deviceNameRef.current = deviceName;
    localStorage.setItem(deviceNameKey, deviceName);
  }, [deviceName]);

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

  const send = useCallback((payload: ClientMessage) => {
    const socket = socketRef.current;
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(payload));
    }
  }, []);

  const updateCapabilities = useCallback((capabilities: ServerCapabilities | undefined, connected = true) => {
    const nextSupportsSleep = connected && hasSleepCapability(capabilities);
    const nextSupportsVolumeControl = connected && hasVolumeCapability(capabilities);
    setSupportsSleep(nextSupportsSleep);
    setSupportsVolumeControl(nextSupportsVolumeControl);
    supportsVolumeControlRef.current = nextSupportsVolumeControl;
    if (!nextSupportsVolumeControl) {
      setAudioState(null);
    }
  }, []);

  useEffect(() => {
    let disposed = false;
    let shouldRetry = true;
    let retryTimer: number | undefined;
    let connectionTimer: number | undefined;
    let heartbeatTimer: number | undefined;
    let heartbeatDeadlineTimer: number | undefined;
    let hasShownUnavailable = false;

    if (!activePc) {
      queueRef.current = [];
      socketRef.current?.close();
      setAudioState(null);
      setSupportsSleep(false);
      setSupportsVolumeControl(false);
      supportsVolumeControlRef.current = false;
      setState("needs-pairing");
      setMessage(pairedPcs.length > 0 ? "Choose a PC or scan a pairing QR." : "Scan the PC pairing QR to pair this app.");
      return () => {
        disposed = true;
        clearTimers();
      };
    }

    const pc = activePc;

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

    function markUnavailable(socket?: WebSocket) {
      if (disposed || !shouldRetry) {
        return;
      }

      hasShownUnavailable = true;
      queueRef.current = [];
      setAudioState(null);
      setSupportsSleep(false);
      setSupportsVolumeControl(false);
      supportsVolumeControlRef.current = false;
      window.clearTimeout(connectionTimer);
      connectionTimer = undefined;
      clearHeartbeat();
      setState("unavailable");
      setMessage(getPcUnavailableMessage(pc));

      if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
        socket.close();
      }

      scheduleRetry();
    }

    function startHeartbeat(socket: WebSocket) {
      clearHeartbeat();
      heartbeatTimer = window.setInterval(() => {
        if (socket !== socketRef.current || socket.readyState !== WebSocket.OPEN) {
          markUnavailable(socket);
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

      window.clearTimeout(retryTimer);
      retryTimer = undefined;
      window.clearTimeout(connectionTimer);
      clearHeartbeat();

      if (hasShownUnavailable) {
        setState("unavailable");
        setMessage(getPcUnavailableMessage(pc, screenshotMode));
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
        const hello: ClientMessage = {
          type: "pair.hello",
          clientId,
          deviceName: deviceNameRef.current.trim() || getDefaultDeviceName(),
          platform: getPlatformName(),
          browser: getBrowserName(),
          displayMode: getDisplayMode(),
          pairToken: pairingAttempt.token,
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
          handlePairAccepted(response, pc.id);
          updateCapabilities(response.capabilities);
          if (!screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName));
          }
          clearPairTokenFromAddress();
          window.clearTimeout(connectionTimer);
          connectionTimer = undefined;
          hasShownUnavailable = false;
          setState("paired");
          setMessage(`Connected to ${getDisplayPcName(pc, response.pcName, screenshotMode)}`);
          startHeartbeat(ws);
          flushQueue(ws, queueRef.current);
          return;
        }

        if (response.type === "pair.rejected") {
          shouldRetry = false;
          clearStoredSecret(clientId, pc.id);
          queueRef.current = [];
          setSupportsSleep(false);
          setSupportsVolumeControl(false);
          supportsVolumeControlRef.current = false;
          setState(response.reason === "missing-token" ? "needs-pairing" : "rejected");
          setMessage(`Pairing rejected: ${response.reason}`);
          ws.close();
          return;
        }

        if (response.type === "status") {
          window.clearTimeout(heartbeatDeadlineTimer);
          heartbeatDeadlineTimer = undefined;
          if (response.pcName && !screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName ?? ""));
          }

          updateCapabilities(response.capabilities, response.connected);
          setMessage(response.connected ? `Connected to ${getDisplayPcName(pc, response.pcName ?? "", screenshotMode)}` : (response.message ?? "Disconnected"));
          return;
        }

        if (response.type === "status.pong") {
          window.clearTimeout(heartbeatDeadlineTimer);
          heartbeatDeadlineTimer = undefined;
          if (!screenshotMode) {
            setPairedPcs((current) => applyPcNameFromHost(current, pc.id, response.pcName));
          }
          updateCapabilities(response.capabilities);
          setState("paired");
          return;
        }

        if (response.type === "audio.state") {
          if (supportsVolumeControlRef.current) {
            setAudioState(normalizeAudioState(response));
          }
        }
      });

      ws.addEventListener("close", () => {
        if (disposed) {
          return;
        }

        if (!shouldRetry) {
          return;
        }

        markUnavailable(ws);
      });

      ws.addEventListener("error", () => {
        markUnavailable(ws);
      });
    }

    connect();

    return () => {
      disposed = true;
      clearTimers();
      socketRef.current?.close();
    };
  }, [activePc?.id, activePc?.url, clientId, pairedPcs.length, pairingAttempt, screenshotMode, updateCapabilities]);

  const pairWithToken = useCallback((token: string, pcUrl = window.location.origin, requestedDeviceName?: string) => {
    const nextDeviceName = normalizeDeviceNameInput(requestedDeviceName ?? deviceNameRef.current) ?? getDefaultDeviceName();
    deviceNameRef.current = nextDeviceName;
    localStorage.setItem(deviceNameKey, nextDeviceName);
    setDeviceName(nextDeviceName);

    const profile = createPcProfile(pcUrl);
    queueRef.current = [];
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    supportsVolumeControlRef.current = false;
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
  }, [screenshotMode]);

  const selectPc = useCallback((pcId: string) => {
    queueRef.current = [];
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    supportsVolumeControlRef.current = false;
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setActivePcId(pcId);
  }, []);

  const addManualPc = useCallback((pcUrl: string) => {
    const profile = createPcProfile(pcUrl);
    queueRef.current = [];
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    supportsVolumeControlRef.current = false;
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
  }, [screenshotMode]);

  const connectManualPc = addManualPc;

  const disconnectActivePc = useCallback(() => {
    if (!activePcId) {
      return;
    }

    queueRef.current = [];
    socketRef.current?.close();
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("needs-pairing");
    setAudioState(null);
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    supportsVolumeControlRef.current = false;
    setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
  }, [activePcId]);

  const forgetPc = useCallback((pcId: string) => {
    const pc = pairedPcs.find((profile) => profile.id === pcId) ?? null;
    queueRef.current = [];
    revokePcPairing(pc, clientId, deviceNameRef.current, activePcId === pcId ? socketRef.current : null);
    clearStoredSecret(clientId, pcId);
    setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
    if (activePcId === pcId) {
      socketRef.current?.close();
      setActivePcId(null);
      setAudioState(null);
      setSupportsSleep(false);
      setSupportsVolumeControl(false);
      supportsVolumeControlRef.current = false;
      setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
      setState("needs-pairing");
      setMessage("Disconnected. Choose a PC or scan a pairing QR.");
    }
  }, [activePcId, clientId, pairedPcs]);

  const renamePc = useCallback((pcId: string, name: string) => {
    setPairedPcs((current) => renamePcProfile(current, pcId, name));
  }, []);

  const renameDevice = useCallback((name: string) => {
    setDeviceName(name);
    if (state === "paired") {
      send({ type: "device.rename", deviceName: name.trim() || getDefaultDeviceName() });
    }
  }, [send, state]);

  return { state, message, send, clientId, deviceName, activePc, pairedPcs, audioState, supportsSleep, supportsVolumeControl, pairWithToken, selectPc, addManualPc, connectManualPc, disconnectActivePc, forgetPc, renamePc, renameDevice };
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

function handlePairAccepted(message: PairAcceptedMessage, pcId: string): void {
  localStorage.setItem(secretKey(message.clientId, pcId), message.secret);
}

function getStoredSecret(clientId: string, pcId: string): string | null {
  return localStorage.getItem(secretKey(clientId, pcId));
}

function clearStoredSecret(clientId: string, pcId: string): void {
  localStorage.removeItem(secretKey(clientId, pcId));
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
