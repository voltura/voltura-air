import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "../platform/clientEnvironment";
import { getWebSocketUrl, type PcProfile } from "./pcProfiles";
import type { PairAcceptedMessage } from "../protocol/messages";
import { parseServerMessage } from "./connectionProtocol";

const revocationTimeoutMs = 10_000;

export function handlePairAccepted(message: PairAcceptedMessage, pcId: string): void {
  localStorage.setItem(secretKey(message.clientId, pcId), message.secret);
}

export function getStoredSecret(clientId: string, pcId: string): string | null {
  return localStorage.getItem(secretKey(clientId, pcId));
}

export function clearStoredSecret(clientId: string, pcId: string): void {
  localStorage.removeItem(secretKey(clientId, pcId));
}

export function shouldClearStoredSecretForRejection(reason: string): boolean {
  return reason === "device-revoked" || reason === "secret-revoked";
}

export function revokePcPairing(pc: PcProfile | null, clientId: string, deviceName: string, activeSocket: WebSocket | null): void {
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
  let finished = false;
  const timeout = window.setTimeout(finish, revocationTimeoutMs);

  function finish() {
    if (finished) {
      return;
    }

    finished = true;
    window.clearTimeout(timeout);
    socket.removeEventListener("open", onOpen);
    socket.removeEventListener("message", onMessage);
    socket.removeEventListener("close", finish);
    socket.removeEventListener("error", finish);
    if (socket.readyState === WebSocket.CONNECTING || socket.readyState === WebSocket.OPEN) {
      socket.close();
    }
  }

  function onOpen() {
    if (finished) {
      return;
    }

    socket.send(JSON.stringify({
      type: "pair.hello",
      clientId,
      deviceName: deviceName.trim() || getDefaultDeviceName(),
      platform: getPlatformName(),
      browser: getBrowserName(),
      displayMode: getDisplayMode(),
      secret
    }));
  }

  function onMessage(event: MessageEvent) {
    const response = parseServerMessage(event.data);
    if (finished) {
      return;
    }

    if (response?.type === "pair.rejected") {
      finish();
      return;
    }

    if (response?.type !== "pair.accepted") {
      return;
    }

    socket.send(JSON.stringify({ type: "pair.disconnect" }));
    finish();
  }

  socket.addEventListener("open", onOpen);
  socket.addEventListener("message", onMessage);
  socket.addEventListener("close", finish);
  socket.addEventListener("error", finish);
}

function secretKey(clientId: string, pcId: string): string {
  return `voltura-air.secret.${clientId}.${pcId}`;
}
