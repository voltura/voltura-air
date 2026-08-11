import { getBrowserName, getDefaultDeviceName, getDisplayMode, getPlatformName } from "../platform/clientEnvironment";
import { parseServerMessage } from "./connectionProtocol";
import { hasStoredReconnectKey, isExpectedHostIdentity, signClientPayload, signReconnectChallenge } from "./pairingCredentials";
import { getWebSocketUrl, type PcProfile } from "./pcProfiles";
import {
  beginRelaySession,
  parseRelayKeyChallenge,
  verifyRelayHostAcceptance,
  type PendingRelaySession,
  type RelayEncryptedChannel,
  type RelayEncryptedSend
} from "./relaySessionCrypto";
import { isControllerSocketOpen, type ControllerSocket } from "./controllerSocket";
import { connectSecureDirect } from "./secureDirect";

const revocationTimeoutMs = 10_000;

export async function revokePcPairing(
  pc: PcProfile | null,
  clientId: string,
  deviceName: string,
  activeSocket: ControllerSocket | null,
  activeRelaySend: RelayEncryptedSend | null = null
): Promise<boolean> {
  if (!pc) {
    return false;
  }
  if (isControllerSocketOpen(activeSocket)) {
    return sendDisconnectAndAwaitClose(activeSocket, activeRelaySend);
  }
  if (!hasStoredReconnectKey(clientId, pc.id)) {
    return false;
  }

  if (pc.transportMode === "secure-direct") {
    return reconnectSecureDirectAndRevoke(pc, clientId, deviceName);
  }

  return reconnectAndRevoke(pc, clientId, deviceName);
}

async function reconnectSecureDirectAndRevoke(pc: PcProfile, clientId: string, deviceName: string): Promise<boolean> {
  if (!pc.relayRouteId || !pc.hostIdentityFingerprint) {return false;}
  const abort = new AbortController();
  let connection: Awaited<ReturnType<typeof connectSecureDirect>>;
  try { connection = await connectSecureDirect(pc.relayRouteId, abort.signal); }
  catch { return false; }
  const { channel } = connection;
  return new Promise((resolve) => {
    let finished = false;
    const timeout = window.setTimeout(() => finish(false), revocationTimeoutMs);
    const finish = (result: boolean) => {
      if (finished) {return;}
      finished = true;
      window.clearTimeout(timeout);
      channel.removeEventListener("message", onMessage);
      channel.removeEventListener("close", onClose);
      channel.removeEventListener("error", onError);
      connection.cleanup();
      resolve(result);
    };
    const onMessage = (event: MessageEvent) => {
      const response = parseServerMessage(event.data);
      if (response?.type === "pair.challenge") {
        const signature = signReconnectChallenge(clientId, pc.id, response.challenge);
        if (!signature) {finish(false); return;}
        channel.send(JSON.stringify({ type: "pair.proof", clientId, signature }));
      } else if (response?.type === "pair.accepted") {
        if (!isExpectedHostIdentity(response, pc.hostIdentityFingerprint)) {finish(false); return;}
        try { channel.send(JSON.stringify({ type: "pair.disconnect" })); }
        catch { finish(false); }
      } else if (response?.type === "pair.disconnect.accepted") {
        finish(true);
      } else if (response?.type === "pair.rejected") {finish(false);}
    };
    const onClose = () => finish(false);
    const onError = () => finish(false);
    channel.addEventListener("message", onMessage);
    channel.addEventListener("close", onClose);
    channel.addEventListener("error", onError);
    channel.send(JSON.stringify({
      type: "pair.hello",
      clientId,
      deviceName: deviceName.trim() || getDefaultDeviceName(),
      platform: getPlatformName(),
      browser: getBrowserName(),
      displayMode: getDisplayMode()
    }));
  });
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

function sendDisconnectAndAwaitClose(socket: ControllerSocket, relaySend: RelayEncryptedSend | null): Promise<boolean> {
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
      socket.removeEventListener("message", onMessage);
      socket.removeEventListener("close", onClose);
      socket.removeEventListener("error", onError);
      resolve(succeeded);
    };
    const onMessage: EventListener = (event) => {
      if (parseServerMessage((event as MessageEvent).data)?.type === "pair.disconnect.accepted") {
        finish(true);
      }
    };
    const onClose = (event: Event) => {
      closeObserved = true;
      closeConfirmed = typeof socket.readyState === "number" && isConfirmedHostClose(event as CloseEvent);
      if (sent) {
        finish(closeConfirmed);
      }
    };
    const onError = () => {
      if (!sent) {
        finish(false);
      }
    };
    socket.addEventListener("message", onMessage);
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
