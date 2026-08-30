import { useCallback, useEffect, useRef, useState } from "react";
import {
  AppsPreviewAssembler,
  createAppsPreviewRequest,
  parseAppsPreviewRecord,
} from "../../foundation/apps/appsPreviewRecords";
import {
  appsPreviewAnswerTranscript,
  appsPreviewOfferTranscript,
} from "../../foundation/apps/appsPreviewTranscripts";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import type {
  AppsCapability,
  AppsPreviewOfferMessage,
  AppsWindowSummary,
  ClientMessage,
} from "../../foundation/protocol/messages";
import { hasOnlyRelayCandidates, waitForIceGathering } from "../../foundation/webrtc/iceGathering";
import {
  hashSessionDescription,
  verifyHostSessionSignature,
} from "../../foundation/webrtc/sessionCrypto";

interface Options {
  activePc: PcProfile;
  capability: AppsCapability;
  clientId: string;
  isListOperationPending: (operationId: string) => boolean;
  revision: string | null;
  selectedIndex: number;
  send: (message: ClientMessage) => void;
  setMessage: (message: string) => void;
  state: ConnectionState;
  windows: AppsWindowSummary[];
}

export type AppsPreviewState = "loading" | "unavailable";

const localId = () => crypto.randomUUID().replaceAll("_", "-");

export function useAppsPreviews({
  activePc,
  capability,
  clientId,
  isListOperationPending,
  revision,
  selectedIndex,
  send,
  setMessage,
  state,
  windows,
}: Options) {
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const channelRef = useRef<RTCDataChannel | null>(null);
  const previewIdRef = useRef<string | null>(null);
  const assemblerRef = useRef(new AppsPreviewAssembler());
  const previewUrlsRef = useRef(new Map<string, string>());
  const [previewUrls, setPreviewUrls] = useState<Map<string, string>>(new Map());
  const retiringPreviewUrlsRef = useRef(new Set<string>());
  const previewStatesRef = useRef(new Map<string, AppsPreviewState>());
  const [previewStates, setPreviewStates] = useState<Map<string, AppsPreviewState>>(new Map());
  const lastPreviewRequestRef = useRef("");
  const requestedWindowIdsRef = useRef(new Set<string>());
  const refreshWindowIdsRef = useRef(new Set<string>());
  const [previewRefreshVersion, setPreviewRefreshVersion] = useState(0);
  const pairedRef = useRef(state === "paired");
  const [previewChannel, setPreviewChannel] = useState<RTCDataChannel | null>(null);

  useEffect(() => {
    pairedRef.current = state === "paired";
  }, [state]);

  const retirePreviewUrl = useCallback((url: string) => {
    retiringPreviewUrlsRef.current.add(url);
    window.setTimeout(() => {
      if (retiringPreviewUrlsRef.current.delete(url)) {
        URL.revokeObjectURL(url);
      }
    }, 5_000);
  }, []);

  const clearPreviewUrls = useCallback(() => {
    for (const url of previewUrlsRef.current.values()) {
      URL.revokeObjectURL(url);
    }
    for (const url of retiringPreviewUrlsRef.current) {
      URL.revokeObjectURL(url);
    }
    retiringPreviewUrlsRef.current.clear();
    previewUrlsRef.current = new Map();
    setPreviewUrls(new Map());
    previewStatesRef.current = new Map();
    setPreviewStates(new Map());
    assemblerRef.current.clear();
    lastPreviewRequestRef.current = "";
    requestedWindowIdsRef.current.clear();
    refreshWindowIdsRef.current.clear();
  }, []);

  const reconcilePreviewUrls = useCallback(
    (
      nextWindowIds: Iterable<string>,
      remap?: { sourceWindowId: string; targetWindowId: string },
    ) => {
      const allowedWindowIds = new Set(nextWindowIds);
      const nextUrls = new Map<string, string>();
      for (const [windowId, url] of previewUrlsRef.current) {
        if (allowedWindowIds.has(windowId)) {
          nextUrls.set(windowId, url);
        }
      }
      if (
        remap &&
        allowedWindowIds.has(remap.targetWindowId) &&
        !nextUrls.has(remap.targetWindowId)
      ) {
        const remappedUrl = previewUrlsRef.current.get(remap.sourceWindowId);
        if (remappedUrl) {
          nextUrls.set(remap.targetWindowId, remappedUrl);
        }
      }
      const retainedUrls = new Set(nextUrls.values());
      for (const url of previewUrlsRef.current.values()) {
        if (!retainedUrls.has(url)) {
          URL.revokeObjectURL(url);
        }
      }
      previewUrlsRef.current = nextUrls;
      setPreviewUrls(nextUrls);
      previewStatesRef.current = new Map();
      setPreviewStates(new Map());
      assemblerRef.current.clear();
      lastPreviewRequestRef.current = "";
      requestedWindowIdsRef.current.clear();
      refreshWindowIdsRef.current = new Set(
        [...refreshWindowIdsRef.current].filter((windowId) => allowedWindowIds.has(windowId)),
      );
    },
    [],
  );

  const refreshPreview = useCallback((windowId: string) => {
    refreshWindowIdsRef.current.add(windowId);
    lastPreviewRequestRef.current = "";
    setPreviewRefreshVersion((version) => version + 1);
  }, []);

  const updatePreviewState = useCallback(
    (windowIds: Iterable<string>, state: AppsPreviewState | null) => {
      const next = new Map(previewStatesRef.current);
      let changed = false;
      for (const windowId of windowIds) {
        if (state === null) {
          changed = next.delete(windowId) || changed;
        } else if (next.get(windowId) !== state) {
          next.set(windowId, state);
          changed = true;
        }
      }
      if (changed) {
        previewStatesRef.current = next;
        setPreviewStates(next);
      }
    },
    [],
  );

  const closePreview = useCallback(
    (notifyHost = true) => {
      const previewId = previewIdRef.current;
      previewIdRef.current = null;
      channelRef.current?.close();
      channelRef.current = null;
      setPreviewChannel(null);
      peerRef.current?.close();
      peerRef.current = null;
      assemblerRef.current.clear();
      lastPreviewRequestRef.current = "";
      requestedWindowIdsRef.current.clear();
      refreshWindowIdsRef.current.clear();
      if (notifyHost && previewId && pairedRef.current) {
        send({ type: "apps.preview.stop", operationId: localId(), previewId });
      }
    },
    [send],
  );

  const requestVisiblePreviews = useCallback(() => {
    if (!capability.previewAvailable || !revision || previewChannel?.readyState !== "open") {
      return;
    }
    const ids = [selectedIndex - 1, selectedIndex, selectedIndex + 1]
      .map((index) => windows[index])
      .filter((window): window is AppsWindowSummary =>
        Boolean(
          window?.previewSupported &&
          (!previewUrlsRef.current.has(window.windowId) ||
            refreshWindowIdsRef.current.has(window.windowId)),
        ),
      )
      .map((window) => window.windowId);
    if (ids.length === 0) {
      requestedWindowIdsRef.current.clear();
      assemblerRef.current.clear();
      lastPreviewRequestRef.current = "";
      return;
    }
    const requestKey = `${revision}:${previewRefreshVersion}:${ids.join(",")}`;
    if (lastPreviewRequestRef.current === requestKey) {
      return;
    }
    try {
      requestedWindowIdsRef.current = new Set(ids);
      previewChannel.send(createAppsPreviewRequest(revision, ids));
      lastPreviewRequestRef.current = requestKey;
      updatePreviewState(ids, "loading");
    } catch {
      closePreview();
    }
  }, [
    capability.previewAvailable,
    closePreview,
    previewChannel,
    previewRefreshVersion,
    revision,
    selectedIndex,
    updatePreviewState,
    windows,
  ]);

  const acceptPreviewOffer = useCallback(
    async (offer: AppsPreviewOfferMessage) => {
      if (
        !capability.previewAvailable ||
        state !== "paired" ||
        !activePc.hostIdentityPublicKey ||
        isListOperationPending(offer.operationId)
      ) {
        return;
      }

      const hostPublicKey = activePc.hostIdentityPublicKey;
      const offerHash = hashSessionDescription(offer.offerSdp);
      if (
        !verifyHostSessionSignature(
          hostPublicKey,
          offer.hostSignature,
          appsPreviewOfferTranscript(
            clientId,
            hostPublicKey,
            offer.operationId,
            offer.previewId,
            offerHash,
          ),
        )
      ) {
        setMessage("The Apps preview identity signature was invalid.");
        return;
      }

      closePreview();
      let peer: RTCPeerConnection | null = null;
      try {
        const relay = activePc.transportMode === "relay";
        if (relay && !offer.iceServers?.length) {
          throw new Error("Relay preview credentials were unavailable.");
        }
        peer = new RTCPeerConnection({
          iceServers: offer.iceServers ?? [],
          iceTransportPolicy: relay ? "relay" : "all",
        });
        peerRef.current = peer;
        previewIdRef.current = offer.previewId;
        peer.addEventListener("datachannel", ({ channel }) => {
          if (channel.label !== "voltura-apps-preview") {
            channel.close();
            return;
          }
          channel.binaryType = "arraybuffer";
          channelRef.current = channel;
          setPreviewChannel(null);
          channel.addEventListener("open", () => {
            if (channelRef.current === channel) {
              setPreviewChannel(channel);
            }
          });
          channel.addEventListener("close", () => {
            if (channelRef.current === channel) {
              channelRef.current = null;
              setPreviewChannel(null);
            }
          });
          channel.addEventListener("message", (event) => {
            if (!(event.data instanceof ArrayBuffer)) {
              closePreview();
              return;
            }
            const record = parseAppsPreviewRecord(event.data);
            if (!record) {
              closePreview();
              return;
            }
            if (!requestedWindowIdsRef.current.has(record.windowId)) {
              return;
            }
            const assembled = assemblerRef.current.accept(record);
            if (assembled === null) {
              closePreview();
              return;
            }
            if (!assembled) {
              return;
            }
            if (assembled.kind === "unavailable") {
              refreshWindowIdsRef.current.delete(assembled.windowId);
              updatePreviewState([assembled.windowId], "unavailable");
              return;
            }
            refreshWindowIdsRef.current.delete(assembled.windowId);
            updatePreviewState([assembled.windowId], null);
            const nextUrl = URL.createObjectURL(assembled.blob);
            const previousUrl = previewUrlsRef.current.get(assembled.windowId);
            if (previousUrl) {
              retirePreviewUrl(previousUrl);
            }
            previewUrlsRef.current.set(assembled.windowId, nextUrl);
            setPreviewUrls(new Map(previewUrlsRef.current));
          });
        });
        await peer.setRemoteDescription({ type: "offer", sdp: offer.offerSdp });
        await peer.setLocalDescription(await peer.createAnswer());
        await waitForIceGathering(peer, relay);
        if (peerRef.current !== peer) {
          peer.close();
          return;
        }
        const answerSdp = peer.localDescription?.sdp;
        if (
          !answerSdp ||
          answerSdp.length > 32 * 1024 ||
          (relay && !hasOnlyRelayCandidates(answerSdp))
        ) {
          throw new Error("Invalid Apps preview answer.");
        }
        const operationId = localId();
        const signature = signClientPayload(
          clientId,
          activePc.id,
          appsPreviewAnswerTranscript(
            clientId,
            hostPublicKey,
            offer.operationId,
            operationId,
            offer.previewId,
            offerHash,
            hashSessionDescription(answerSdp),
          ),
        );
        if (!signature) {
          throw new Error("Scan the PC pairing QR again before using Apps previews.");
        }
        send({
          type: "apps.preview.answer",
          operationId,
          offerOperationId: offer.operationId,
          previewId: offer.previewId,
          answerSdp,
          clientSignature: signature,
        });
      } catch (error) {
        if (peer && peerRef.current !== peer) {
          peer.close();
          return;
        }
        closePreview();
        setMessage(error instanceof Error ? error.message : "Apps previews are unavailable.");
      }
    },
    [
      closePreview,
      activePc,
      capability.previewAvailable,
      clientId,
      isListOperationPending,
      send,
      setMessage,
      state,
      retirePreviewUrl,
      updatePreviewState,
    ],
  );

  const ownsPreview = useCallback((previewId: string) => previewIdRef.current === previewId, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- the open external data channel must be synchronized with the latest visible-window state
    requestVisiblePreviews();
  }, [requestVisiblePreviews]);

  useEffect(() => {
    if (state === "paired" && capability.canUse) {
      return;
    }
    // oxlint-disable-next-line react/set-state-in-effect -- permission loss must close the external peer and clear its rendered state together
    closePreview();
    clearPreviewUrls();
  }, [capability.canUse, clearPreviewUrls, closePreview, state]);

  useEffect(() => {
    const visibilityChange = () => {
      if (document.visibilityState === "hidden") {
        closePreview();
        clearPreviewUrls();
      }
    };
    document.addEventListener("visibilitychange", visibilityChange);
    return () => document.removeEventListener("visibilitychange", visibilityChange);
  }, [clearPreviewUrls, closePreview]);

  useEffect(
    () => () => {
      closePreview();
      for (const url of previewUrlsRef.current.values()) {
        URL.revokeObjectURL(url);
      }
      for (const url of retiringPreviewUrlsRef.current) {
        URL.revokeObjectURL(url);
      }
      retiringPreviewUrlsRef.current.clear();
      previewUrlsRef.current.clear();
      assemblerRef.current.clear();
      requestedWindowIdsRef.current.clear();
    },
    [closePreview],
  );

  return {
    acceptPreviewOffer,
    clearPreviewUrls,
    closePreview,
    ownsPreview,
    previewStates,
    previewUrls,
    reconcilePreviewUrls,
    refreshPreview,
  };
}
