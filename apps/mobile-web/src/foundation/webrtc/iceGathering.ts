export class IceGatheringTimeoutError extends Error {}

export function waitForIceGathering(
  peer: RTCPeerConnection,
  allowSettledRelayCandidates = false,
): Promise<void> {
  if (peer.iceGatheringState === "complete") {
    return Promise.resolve();
  }
  return new Promise((resolve, reject) => {
    let relaySettleTimeout: number | undefined;
    const cleanup = () => {
      window.clearTimeout(gatheringTimeout);
      window.clearTimeout(relaySettleTimeout);
      peer.removeEventListener("icegatheringstatechange", onState);
      peer.removeEventListener("icecandidate", onCandidate);
    };
    const finishWithRelayCandidates = () => {
      if (!hasOnlyRelayCandidates(peer.localDescription?.sdp ?? "")) {
        return;
      }
      cleanup();
      resolve();
    };
    const scheduleRelaySettle = () => {
      if (!allowSettledRelayCandidates) {
        return;
      }
      window.clearTimeout(relaySettleTimeout);
      relaySettleTimeout = window.setTimeout(finishWithRelayCandidates, 350);
    };
    const onState = () => {
      if (peer.iceGatheringState === "complete") {
        cleanup();
        resolve();
      }
    };
    const onCandidate = (event: RTCPeerConnectionIceEvent) => {
      if (isRelayCandidate(event.candidate)) {
        scheduleRelaySettle();
      }
    };
    peer.addEventListener("icegatheringstatechange", onState);
    peer.addEventListener("icecandidate", onCandidate);
    const gatheringTimeout = window.setTimeout(() => {
      if (allowSettledRelayCandidates && hasOnlyRelayCandidates(peer.localDescription?.sdp ?? "")) {
        finishWithRelayCandidates();
        return;
      }
      cleanup();
      reject(new IceGatheringTimeoutError("WebRTC candidate gathering timed out."));
    }, 10_000);
    if (hasOnlyRelayCandidates(peer.localDescription?.sdp ?? "")) {
      scheduleRelaySettle();
    }
  });
}

export function isRelayCandidate(candidate: RTCIceCandidate | null): boolean {
  return candidate?.type === "relay" || /\styp\s+relay(?:\s|$)/.test(candidate?.candidate ?? "");
}

export function hasOnlyRelayCandidates(sdp: string): boolean {
  const candidates = sdp.split(/\r?\n/).filter((line) => line.startsWith("a=candidate:"));
  return candidates.length > 0 && candidates.every((line) => /\styp\s+relay(?:\s|$)/.test(line));
}
