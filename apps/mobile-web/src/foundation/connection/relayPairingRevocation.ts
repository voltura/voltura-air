import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "../platform/clientEnvironment";
import { parseServerMessage } from "./connectionProtocol";
import { hasStoredReconnectKey, signClientPayload, signReconnectChallenge } from "./pairingCredentials";
import { getWebSocketUrl, type PcProfile } from "./pcProfiles";
import {
  beginRelaySession,
  parseRelayKeyChallenge,
  verifyRelayHostAcceptance,
  type PendingRelaySession,
  type RelayEncryptedChannel,
  type RelayEncryptedSend
} from "./relaySessionCrypto";

const revocationTimeoutMs = 10_000;

export async function revokePcPairing(
  pc: PcProfile | null,
  clientId: string,
  deviceName: string,
  activeSocket: WebSocket | null,
  activeRelaySend: RelayEncryptedSend | null = null
): Promise<boolean> {
  if (!pc) {
    return false;
  }
  if (activeSocket?.readyState === WebSocket.OPEN) {
    return sendDisconnectAndAwaitClose(activeSocket, activeRelaySend);
  }
  if (!hasStoredReconnectKey(clientId, pc.id)) {
    return false;
  }

  return reconnectAndRevoke(pc, clientId, deviceName);
}

function reconnectAndRevoke(pc: PcProfile, clientId: string, deviceName: string): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = new WebSocket(getWebSocketUrl(pc));
    socket.binaryType = "arraybuffer";
    const rawSend = socket.send.bind(socket);
    let finished = false;
    let disconnectRequested = false;
    let disconnectSent = false;
    let closeObserved = false;
    let closeConfirmed = false;
    let pendingRelaySession: PendingRelaySession | null = null;
    let relayChannel: RelayEncryptedChannel | null = null;
    const timeout = window.setTimeout(() => { finish(false); }, revocationTimeoutMs);

    function finish(succeeded: boolean) {
      if (finished) {
        return;
      }
      finished = true;
      window.clearTimeout(timeout);
      socket.removeEventListener("open", onOpen);
      socket.removeEventListener("message", onMessage);
      socket.removeEventListener("close", onClose);
      socket.removeEventListener("error", onError);
      if (socket.readyState === WebSocket.CONNECTING || socket.readyState === WebSocket.OPEN) {
        socket.close();
      }
      resolve(succeeded);
    }

    function onOpen() {
      rawSend(JSON.stringify({
        type: "pair.hello",
        clientId,
        deviceName: deviceName.trim() || getDefaultDeviceName(),
        platform: getPlatformName(),
        browser: getBrowserName(),
        displayMode: getDisplayMode()
      }));
    }

    function onMessage(event: MessageEvent) {
      if (finished) {
        return;
      }
      if (relayChannel) {
        void relayChannel.decryptText(event.data).then((plaintext) => {
          if (plaintext === null) {
            finish(false);
          } else {
            handleAuthenticatedMessage(plaintext);
          }
        });
        return;
      }
      if (pc.transportMode === "relay" && typeof event.data === "string" && handleRelayHandshakeMessage(event.data)) {
        return;
      }
      handleAuthenticatedMessage(event.data);
    }

    function handleRelayHandshakeMessage(message: string): boolean {
      let value: unknown;
      try {
        value = JSON.parse(message);
      } catch {
        value = null;
      }
      const challenge = parseRelayKeyChallenge(value);
      if (challenge) {
        if (challenge.routeId !== pc.relayRouteId || challenge.clientId !== clientId || !pc.hostIdentityPublicKey) {
          finish(false);
          return true;
        }
        pendingRelaySession = beginRelaySession(
          challenge,
          pc.hostIdentityPublicKey,
          null,
          (transcript) => signClientPayload(clientId, pc.id, transcript));
        if (!pendingRelaySession) {
          finish(false);
          return true;
        }
        rawSend(JSON.stringify(pendingRelaySession.proof));
        return true;
      }
      if (pendingRelaySession && verifyRelayHostAcceptance(pendingRelaySession, value)) {
        relayChannel = pendingRelaySession.channel;
        pendingRelaySession = null;
        return true;
      }
      return false;
    }

    function handleAuthenticatedMessage(value: unknown) {
      const response = parseServerMessage(value);
      if (response?.type === "pair.challenge") {
        sendReconnectProof(response.challenge);
      } else if (response?.type === "pair.rejected") {
        finish(false);
      } else if (response?.type === "pair.accepted") {
        void requestDisconnect();
      }
    }

    function sendReconnectProof(challenge: string) {
      const signature = signReconnectChallenge(clientId, pc.id, challenge);
      if (!signature) {
        finish(false);
        return;
      }
      rawSend(JSON.stringify({ type: "pair.proof", clientId, signature }));
    }

    async function requestDisconnect() {
      if (disconnectRequested) {
        return;
      }
      disconnectRequested = true;
      try {
        const text = JSON.stringify({ type: "pair.disconnect" });
        if (relayChannel) {
          await relayChannel.send((encrypted) => { rawSend(encrypted); }, text);
        } else {
          rawSend(text);
        }
        disconnectSent = true;
        if (closeObserved || socket.readyState === WebSocket.CLOSED) {
          finish(closeConfirmed);
        }
      } catch {
        finish(false);
      }
    }

    function onClose(event: CloseEvent) {
      closeObserved = true;
      closeConfirmed = isConfirmedHostClose(event);
      if (disconnectSent) {
        finish(closeConfirmed);
      } else if (!disconnectRequested) {
        finish(false);
      }
    }

    function onError() {
      if (!disconnectRequested) {
        finish(false);
      }
    }

    socket.addEventListener("open", onOpen);
    socket.addEventListener("message", onMessage);
    socket.addEventListener("close", onClose);
    socket.addEventListener("error", onError);
  });
}

function sendDisconnectAndAwaitClose(socket: WebSocket, relaySend: RelayEncryptedSend | null): Promise<boolean> {
  return new Promise((resolve) => {
    let finished = false;
    let sent = false;
    let closeObserved = false;
    let closeConfirmed = false;
    const timeout = window.setTimeout(() => { finish(false); }, revocationTimeoutMs);
    const finish = (succeeded: boolean) => {
      if (finished) {
        return;
      }
      finished = true;
      window.clearTimeout(timeout);
      socket.removeEventListener("close", onClose);
      socket.removeEventListener("error", onError);
      resolve(succeeded);
    };
    const onClose = (event: CloseEvent) => {
      closeObserved = true;
      closeConfirmed = isConfirmedHostClose(event);
      if (sent) {
        finish(closeConfirmed);
      }
    };
    const onError = () => {
      if (!sent) {
        finish(false);
      }
    };
    socket.addEventListener("close", onClose);
    socket.addEventListener("error", onError);
    let send: Promise<void>;
    try {
      if (relaySend) {
        send = relaySend(JSON.stringify({ type: "pair.disconnect" }));
      } else {
        socket.send(JSON.stringify({ type: "pair.disconnect" }));
        send = Promise.resolve();
      }
    } catch (error) {
      send = Promise.reject(error instanceof Error ? error : new Error("Pairing revocation send failed."));
    }
    void send.then(() => {
      sent = true;
      if (closeObserved || socket.readyState === WebSocket.CLOSED) {
        finish(closeConfirmed);
      }
    }, () => { finish(false); });
  });
}

function isConfirmedHostClose(event: CloseEvent): boolean {
  return event.code === 1000;
}
