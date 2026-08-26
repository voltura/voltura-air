const maximumSdpBytes = 32 * 1024;
const maximumSignalingBytes = 64 * 1024;
const setupTimeoutMs = 20_000;

export async function connectSecureDirect(
  routeId: string,
  signal?: AbortSignal,
): Promise<{ channel: RTCDataChannel; cleanup: () => void }> {
  if (!/^[A-Za-z0-9_-]{22}$/u.test(routeId)) {
    throw new TypeError("Invalid Secure Direct route.");
  }
  signal?.throwIfAborted();
  const endpoint = new URL(__RELAY_HTTPS_BASE__);
  endpoint.protocol = "wss:";
  endpoint.pathname = `/v1/secure/device/${routeId}`;
  endpoint.search = "";
  endpoint.hash = "";

  const signaling = new WebSocket(endpoint);
  let peer: RTCPeerConnection;
  try {
    peer = new RTCPeerConnection({ iceServers: [] });
  } catch (error) {
    signaling.close();
    throw error;
  }
  let channel: RTCDataChannel | null = null;
  let answerSent = false;
  let cleaned = false;
  let timeout: number | undefined;
  let cancelSetup: ((error: Error) => void) | undefined;
  const cancelled = new Promise<never>((_, reject) => {
    cancelSetup = reject;
  });
  const onAbort = () => {
    cleanup();
    cancelSetup?.(new Error("Secure Direct setup was cancelled."));
  };
  const onPeerConnectionStateChange = () => {
    if (peer.connectionState === "failed" || peer.connectionState === "closed") {
      channel?.close();
    }
  };
  peer.addEventListener("connectionstatechange", onPeerConnectionStateChange);

  const cleanup = () => {
    if (cleaned) {
      return;
    }
    cleaned = true;
    window.clearTimeout(timeout);
    peer.removeEventListener("connectionstatechange", onPeerConnectionStateChange);
    signaling.close();
    channel?.close();
    peer.close();
    signal?.removeEventListener("abort", onAbort);
  };
  signal?.addEventListener("abort", onAbort, { once: true });

  try {
    timeout = window.setTimeout(() => {
      cleanup();
      cancelSetup?.(new Error("Secure Direct signaling timed out."));
    }, setupTimeoutMs);
    const offer = await Promise.race([
      new Promise<string>((resolve, reject) => {
        signaling.addEventListener(
          "message",
          (event) => {
            if (
              typeof event.data !== "string" ||
              new TextEncoder().encode(event.data).length > maximumSignalingBytes
            ) {
              reject(new Error("Secure Direct offer was invalid."));
              return;
            }
            try {
              const value: unknown = JSON.parse(event.data);
              if (!isDescription(value, "secure.offer")) {
                throw new Error();
              }
              resolve(value.sdp);
            } catch {
              reject(new Error("Secure Direct offer was invalid."));
            }
          },
          { once: true },
        );
        signaling.addEventListener(
          "close",
          () => {
            if (!answerSent) {
              reject(new Error("Secure Direct signaling closed."));
            }
          },
          { once: true },
        );
        signaling.addEventListener(
          "error",
          () => {
            if (!answerSent) {
              reject(new Error("Secure Direct signaling failed."));
            }
          },
          { once: true },
        );
      }),
      cancelled,
    ]);

    const channelPromise = new Promise<RTCDataChannel>((resolve, reject) => {
      peer.addEventListener("datachannel", (event) => {
        if (channel || event.channel.label !== "voltura-control") {
          event.channel.close();
          cleanup();
          reject(new Error("Secure Direct DataChannel was invalid."));
          return;
        }
        channel = event.channel;
        channel.binaryType = "arraybuffer";
        if (channel.readyState === "open") {
          resolve(channel);
        } else {
          const removeSetupListeners = () => {
            channel?.removeEventListener("open", onOpen);
            channel?.removeEventListener("close", onClose);
            channel?.removeEventListener("error", onError);
          };
          const onOpen = () => {
            removeSetupListeners();
            resolve(channel!);
          };
          const onClose = () => {
            removeSetupListeners();
            reject(new Error("Secure Direct DataChannel closed."));
          };
          const onError = () => {
            removeSetupListeners();
            reject(new Error("Secure Direct DataChannel failed."));
          };
          channel.addEventListener("open", onOpen);
          channel.addEventListener("close", onClose);
          channel.addEventListener("error", onError);
        }
      });
    });

    await Promise.race([peer.setRemoteDescription({ type: "offer", sdp: offer }), cancelled]);
    const createdAnswer = await Promise.race([peer.createAnswer(), cancelled]);
    await Promise.race([peer.setLocalDescription(createdAnswer), cancelled]);
    await Promise.race([waitForIceGathering(peer), cancelled]);
    const sdp = peer.localDescription?.sdp;
    if (
      !sdp ||
      new TextEncoder().encode(sdp).length > maximumSdpBytes ||
      signaling.readyState !== WebSocket.OPEN
    ) {
      throw new Error("Secure Direct answer was unavailable.");
    }
    const answer = JSON.stringify({ type: "secure.answer", sdp });
    if (new TextEncoder().encode(answer).length > maximumSignalingBytes) {
      throw new Error("Secure Direct answer was too large.");
    }
    answerSent = true;
    signaling.send(answer);
    const opened = await Promise.race([channelPromise, cancelled]);
    window.clearTimeout(timeout);
    return { channel: opened, cleanup };
  } catch (error) {
    cleanup();
    throw error;
  }
}

function isDescription(
  value: unknown,
  type: "secure.offer",
): value is { type: "secure.offer"; sdp: string } {
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value) &&
    Object.keys(value).length === 2 &&
    (value as { type?: unknown }).type === type &&
    typeof (value as { sdp?: unknown }).sdp === "string" &&
    (value as { sdp: string }).sdp.length > 0 &&
    new TextEncoder().encode((value as { sdp: string }).sdp).length <= maximumSdpBytes
  );
}

function waitForIceGathering(peer: RTCPeerConnection): Promise<void> {
  if (peer.iceGatheringState === "complete") {
    return Promise.resolve();
  }
  return new Promise((resolve) => {
    const onChange = () => {
      if (peer.iceGatheringState !== "complete") {
        return;
      }
      peer.removeEventListener("icegatheringstatechange", onChange);
      resolve();
    };
    peer.addEventListener("icegatheringstatechange", onChange);
  });
}
