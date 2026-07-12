import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "../clientEnvironment";
import { getWebSocketUrl, type PcProfile } from "../pcProfiles";
import type { PairAcceptedMessage } from "../protocol";
import { parseServerMessage } from "./connectionProtocol";

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
