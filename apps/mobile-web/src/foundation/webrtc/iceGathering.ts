export class IceGatheringTimeoutError extends Error {}

export function waitForIceGathering(
  peer: RTCPeerConnection,
  allowRelayCandidatesAtDeadline = false,
): Promise<void> {
  if (peer.iceGatheringState === "complete") {
    return Promise.resolve();
  }
  return new Promise((resolve, reject) => {
    const cleanup = () => {
      window.clearTimeout(gatheringTimeout);
      peer.removeEventListener("icegatheringstatechange", onState);
    };
    const onState = () => {
      if (peer.iceGatheringState === "complete") {
        cleanup();
        resolve();
      }
    };
    peer.addEventListener("icegatheringstatechange", onState);
    const gatheringTimeout = window.setTimeout(() => {
      cleanup();
      // The signed answer is a one-shot snapshot: a quiet candidate interval
      // cannot stand in for completion, because later TURN routes are not sent.
      // Preserve the bounded fallback for browsers that never report complete.
      if (
        allowRelayCandidatesAtDeadline &&
        hasOnlyRelayCandidates(peer.localDescription?.sdp ?? "")
      ) {
        resolve();
        return;
      }
      reject(new IceGatheringTimeoutError("WebRTC candidate gathering timed out."));
    }, 10_000);
  });
}

export function isRelayCandidate(candidate: RTCIceCandidate | null): boolean {
  return candidate?.type === "relay" || /\styp\s+relay(?:\s|$)/.test(candidate?.candidate ?? "");
}

export function hasOnlyRelayCandidates(sdp: string): boolean {
  const candidates = sdp.split(/\r?\n/).filter((line) => line.startsWith("a=candidate:"));
  return candidates.length > 0 && candidates.every((line) => /\styp\s+relay(?:\s|$)/.test(line));
}
