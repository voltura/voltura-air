import { useEffect, useEffectEvent, useRef, useState, type TouchEvent } from "react";
import { ChevronLeft, Keyboard, Maximize2, Minimize2, MonitorUp, MousePointer2, Play, Square } from "lucide-react";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { createLocalId } from "../../foundation/identity/localId";
import type { TrackpadSettings, TwoFingerMode } from "../../foundation/input/gestures";
import { usePointerInput } from "../../foundation/input/usePointerInput";
import type { ClientMessage, ScreenViewCapability, ScreenViewSource, ScreenViewStartResultMessage } from "../../foundation/protocol/messages";
import { subscribeScreenViewResults } from "../../foundation/connection/screenViewResultBus";
import { hashScreenSdp, verifyHostScreenSignature } from "./screenViewCrypto";
import { parseScreenPlaintextRecord, type ScreenCursorRecord } from "./screenViewRecords";
import { identityScreenViewTransform, touchPairGeometry, updateScreenViewPinch, type ScreenViewPinchStart, type ScreenViewTransform } from "./screenViewTransform";
import { useScreenViewFullscreen } from "./useScreenViewFullscreen";
import "./screen-view.css";

interface Props {
  activePc: PcProfile;
  capability: ScreenViewCapability;
  clientId: string;
  onBack: () => void;
  onOpenKeyboard: () => void;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
}

interface PendingOffer { operationId: string; displayId: string; }

const disconnectedRecoveryMs = 8_000;

class IceGatheringTimeoutError extends Error {}

export default function ScreenViewWorkspace({ activePc, capability, clientId, onBack, onOpenKeyboard, send, state, trackpadSettings }: Props) {
  const [sources, setSources] = useState<ScreenViewSource[]>([]);
  const [selected, setSelected] = useState("");
  const [status, setStatus] = useState(
    capability.requiresRepair
      ? "Scan this PC's pairing QR once to trust its screen identity."
      : !capability.enabled
        ? "Screen viewing is unavailable on this PC."
        : !capability.permissionGranted
          ? "Allow this device to view the PC screen in PC permissions."
          : "Choose a display to begin."
  );
  const [viewing, setViewing] = useState(false);
  const [streaming, setStreaming] = useState(false);
  const [playbackBlocked, setPlaybackBlocked] = useState(false);
  const [viewTransform, setViewTransform] = useState<ScreenViewTransform>(identityScreenViewTransform);
  const [twoFingerMode, setTwoFingerMode] = useState<TwoFingerMode>("scroll");
  const { workspaceRef, immersive, enterImmersive, exitImmersive } = useScreenViewFullscreen();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const stageRef = useRef<HTMLDivElement | null>(null);
  const cursorRef = useRef<HTMLImageElement | null>(null);
  const cursorStateRef = useRef<ScreenCursorRecord | null>(null);
  const cursorUrlRef = useRef<string | null>(null);
  const hasVisualFrameRef = useRef(false);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const eventsRef = useRef<RTCDataChannel | null>(null);
  const pendingOfferRef = useRef<PendingOffer | null>(null);
  const pendingAnswerRef = useRef<string | null>(null);
  const pendingStopRef = useRef<string | null>(null);
  const credentialRenewalRef = useRef<number | undefined>(undefined);
  const renewalRestartRef = useRef<number | undefined>(undefined);
  const disconnectedRecoveryRef = useRef<number | undefined>(undefined);
  const pointerInput = usePointerInput({ send, state, trackpadSettings, twoFingerMode: "scroll" });
  const viewTransformRef = useRef(viewTransform);
  const pinchRef = useRef<(ScreenViewPinchStart & { mode: "local" | "remote" }) | null>(null);
  const suppressPointerUntilClearRef = useRef(false);

  function applyViewTransform(next: ScreenViewTransform) {
    viewTransformRef.current = next;
    setViewTransform(next);
  }

  useEffect(() => {
    const stage = stageRef.current;
    if (!stage || typeof ResizeObserver === "undefined") {return;}
    let width = stage.clientWidth;
    let height = stage.clientHeight;
    const observer = new ResizeObserver(() => {
      const nextWidth = stage.clientWidth;
      const nextHeight = stage.clientHeight;
      if (Math.abs(nextWidth - width) > 1 || Math.abs(nextHeight - height) > 1) {
        width = nextWidth;
        height = nextHeight;
        applyViewTransform(identityScreenViewTransform);
        requestAnimationFrame(positionCursor);
      }
    });
    observer.observe(stage);
    return () => observer.disconnect();
  }, []);

  function onScreenTouchStart(event: TouchEvent<HTMLDivElement>) {
    if (!viewing) {event.preventDefault(); return;}
    if (suppressPointerUntilClearRef.current) {event.preventDefault(); return;}
    pointerInput.onTouchStart(event);
    if (event.targetTouches.length !== 2 || !stageRef.current) {return;}
    const bounds = stageRef.current.getBoundingClientRect();
    const geometry = touchPairGeometry(event.targetTouches, bounds.left, bounds.top);
    if (!geometry) {return;}
    const local = twoFingerMode === "zoom";
    pinchRef.current = { ...geometry, transform: viewTransformRef.current, mode: local ? "local" : "remote" };
    if (local) {pointerInput.onTouchCancel(event);}
  }

  function onScreenTouchMove(event: TouchEvent<HTMLDivElement>) {
    if (!viewing) {event.preventDefault(); return;}
    const pinch = pinchRef.current;
    if (!pinch || event.targetTouches.length < 2 || !stageRef.current) {
      if (!suppressPointerUntilClearRef.current) {pointerInput.onTouchMove(event);} else {event.preventDefault();}
      return;
    }
    const bounds = stageRef.current.getBoundingClientRect();
    const geometry = touchPairGeometry(event.targetTouches, bounds.left, bounds.top);
    if (!geometry) {return;}
    if (pinch.mode === "remote") {pointerInput.onTouchMove(event); return;}
    event.preventDefault();
    applyViewTransform(updateScreenViewPinch(
      pinch,
      geometry.distance,
      geometry.midpointX,
      geometry.midpointY,
      stageRef.current.clientWidth,
      stageRef.current.clientHeight
    ));
  }

  function onScreenTouchEnd(event: TouchEvent<HTMLDivElement>) {
    if (!viewing) {
      event.preventDefault();
      cancelScreenGesture();
      return;
    }
    const pinch = pinchRef.current;
    if (pinch?.mode === "local") {
      event.preventDefault();
      pinchRef.current = null;
      suppressPointerUntilClearRef.current = event.targetTouches.length > 0;
      return;
    }
    pinchRef.current = null;
    if (suppressPointerUntilClearRef.current) {
      event.preventDefault();
      suppressPointerUntilClearRef.current = event.targetTouches.length > 0;
      return;
    }
    pointerInput.onTouchEnd(event);
  }

  function onScreenTouchCancel(event: TouchEvent<HTMLDivElement>) {
    event.preventDefault();
    cancelScreenGesture();
  }

  function cancelScreenGesture() {
    pinchRef.current = null;
    suppressPointerUntilClearRef.current = false;
    pointerInput.cancel();
  }

  function start(displayId = selected) {
    if (!displayId || !activePc.hostIdentityPublicKey || capability.requiresRepair || !capability.canView || pendingOfferRef.current || peerRef.current) {return;}
    if (typeof RTCPeerConnection === "undefined") {setStatus("This browser does not provide WebRTC screen playback."); return;}
    const operationId = createLocalId();
    const transcript = `VolturaAir screen-view:start:v2:${clientId}:${operationId}:${displayId}`;
    const signature = signClientPayload(clientId, activePc.id, transcript);
    if (!signature) {setStatus("The reconnect key is unavailable. Pair this device again."); return;}
    pendingOfferRef.current = { operationId, displayId };
    setStreaming(true);
    setStatus("Preparing encrypted WebRTC mirror...");
    send({ type: "screen.view.start", operationId, displayId, clientSignature: signature });
  }

  function selectSource(displayId: string) {
    setSelected(displayId);
    if (streaming) {
      setStatus("Switching display...");
      send({ type: "screen.view.source.set", operationId: createLocalId(), displayId });
    }
  }

  function stop() {
    const operationId = createLocalId();
    pendingStopRef.current = operationId;
    send({ type: "screen.view.stop", operationId });
    closeStream();
    setStatus("Screen viewing stopped.");
  }

  async function playVideo() {
    const video = videoRef.current;
    const source = video?.srcObject;
    if (!video || !source) {
      setPlaybackBlocked(false);
      setStatus("The WebRTC mirror is no longer connected. Tap Start to try again.");
      return;
    }
    try {
      await video.play();
      if (videoRef.current !== video || video.srcObject !== source || !peerRef.current) {return;}
      setPlaybackBlocked(false);
    } catch (error) {
      if (videoRef.current !== video || video.srcObject !== source || !peerRef.current) {return;}
      if (error instanceof DOMException && error.name === "NotAllowedError") {
        setPlaybackBlocked(true);
        setStatus("Video is ready. Tap Show video to allow playback.");
      } else {
        setPlaybackBlocked(false);
        setStatus("The connected WebRTC video could not begin playback.");
      }
    }
  }

  async function acceptStart(message: ScreenViewStartResultMessage) {
    const pending = pendingOfferRef.current;
    if (pending?.operationId !== message.operationId) {return;}
    pendingOfferRef.current = null;
    if (!message.succeeded || !message.offerSdp || !message.hostSignature || !activePc.hostIdentityPublicKey) {
      setStatus(message.message);
      setStreaming(false);
      return;
    }
    const offerHash = hashScreenSdp(message.offerSdp);
    const hostTranscript = `VolturaAir screen-view:offer:v2:${clientId}:${message.operationId}:${pending.displayId}:${offerHash}`;
    if (!verifyHostScreenSignature(activePc.hostIdentityPublicKey, message.hostSignature, hostTranscript)) {
      setStatus("The PC identity signature was invalid. No pixels were rendered.");
      setStreaming(false);
      return;
    }

    const relayMode = activePc.transportMode === "relay";
    if (relayMode && (!message.iceServers || message.iceServers.length === 0)) {
      setStatus("TURN credentials were unavailable. Commands remain connected.");
      setStreaming(false);
      return;
    }
    const peer = new RTCPeerConnection({
      iceServers: message.iceServers ?? [],
      iceTransportPolicy: relayMode ? "relay" : "all",
      bundlePolicy: "max-bundle",
      rtcpMuxPolicy: "require"
    });
    let relayCandidateCount = 0;
    let lastIceErrorCode: number | null = null;
    peerRef.current = peer;
    scheduleCredentialRenewal(message.turnExpiresAt, pending.displayId);
    peer.addEventListener("icecandidate", (event) => {
      if (isRelayCandidate(event.candidate)) {
        relayCandidateCount += 1;
      }
    });
    peer.addEventListener("icecandidateerror", (event) => {
      lastIceErrorCode = event.errorCode;
    });
    peer.addEventListener("track", (event) => {
      if (event.track.kind !== "video" || !videoRef.current) {return;}
      videoRef.current.srcObject = event.streams[0] ?? new MediaStream([event.track]);
      void playVideo();
    });
    peer.addEventListener("datachannel", (event) => {
      if (event.channel.label !== "screen-events") {event.channel.close(); return;}
      eventsRef.current = event.channel;
      event.channel.binaryType = "arraybuffer";
      event.channel.addEventListener("message", handleScreenEvent);
    });
    peer.addEventListener("connectionstatechange", () => {
      if (peer.connectionState === "connected") {
        window.clearTimeout(disconnectedRecoveryRef.current);
        disconnectedRecoveryRef.current = undefined;
        if (hasVisualFrameRef.current) {setViewing(true);}
        setStatus("Live - Encrypted WebRTC");
      }
      if (peer.connectionState === "disconnected") {
        window.clearTimeout(disconnectedRecoveryRef.current);
        setViewing(false);
        setStatus("Screen video interrupted. Reconnecting for up to 8 seconds...");
        disconnectedRecoveryRef.current = window.setTimeout(() => {
          if (peerRef.current === peer && peer.connectionState === "disconnected") {
            closeStream();
            setStatus("Screen video connection was lost. Tap Start to reconnect.");
          }
        }, disconnectedRecoveryMs);
      }
      if (peer.connectionState === "failed" || peer.connectionState === "closed") {
        if (peerRef.current === peer) {
          closeStream();
          setStatus("Screen video connection was lost. Tap Start to reconnect.");
        }
      }
    });

    try {
      await peer.setRemoteDescription({ type: "offer", sdp: message.offerSdp });
      const answer = await peer.createAnswer();
      await peer.setLocalDescription(answer);
      await waitForIceGathering(peer, relayMode);
      const answerSdp = peer.localDescription?.sdp;
      if (!answerSdp || answerSdp.length > 32 * 1024) {throw new Error("Invalid WebRTC answer.");}
      if (relayMode && !hasOnlyRelayCandidates(answerSdp)) {throw new Error("The WebRTC answer did not contain relay-only candidates.");}
      const answerHash = hashScreenSdp(answerSdp);
      const answerTranscript = `VolturaAir screen-view:answer:v2:${clientId}:${message.operationId}:${pending.displayId}:${offerHash}:${answerHash}`;
      const clientSignature = signClientPayload(clientId, activePc.id, answerTranscript);
      if (!clientSignature) {throw new Error("The reconnect key is unavailable.");}
      pendingAnswerRef.current = message.operationId;
      send({ type: "screen.view.answer", operationId: message.operationId, answerSdp, clientSignature });
      setStatus("Connecting encrypted WebRTC mirror...");
    } catch (error) {
      closeStream();
      if (error instanceof IceGatheringTimeoutError) {
        setStatus(`Relay candidate gathering timed out (relay candidates: ${relayCandidateCount}, ICE error: ${lastIceErrorCode ?? "none"}).`);
      } else {
        setStatus("This browser could not negotiate the PC's H.264 WebRTC stream.");
      }
    }
  }

  function handleScreenEvent(event: MessageEvent) {
    if (!(event.data instanceof ArrayBuffer)) {closeStream(); setStatus("The screen event channel sent invalid data."); return;}
    try {
      const record = parseScreenPlaintextRecord(new Uint8Array(event.data));
      if (record.type === "cursor") {updateCursor(record);}
      else if (record.type === "status") {closeStream(); setStatus(record.message);}
      else {throw new Error("Unexpected screen event.");}
    } catch {
      closeStream();
      setStatus("The screen event channel sent invalid data.");
    }
  }

  function updateCursor(cursor: ScreenCursorRecord) {
    cursorStateRef.current = cursor;
    if (cursor.pngBytes) {
      if (cursorUrlRef.current) {URL.revokeObjectURL(cursorUrlRef.current);}
      cursorUrlRef.current = URL.createObjectURL(new Blob([Uint8Array.from(cursor.pngBytes).buffer], { type: "image/png" }));
      if (cursorRef.current) {cursorRef.current.src = cursorUrlRef.current;}
    }
    positionCursor();
  }

  function positionCursor() {
    const cursor = cursorStateRef.current;
    const image = cursorRef.current;
    const video = videoRef.current;
    const stage = stageRef.current;
    if (!hasVisualFrameRef.current || !cursor || !image || !video || !stage || !cursor.visible || !image.src || video.videoWidth === 0 || video.videoHeight === 0) {
      if (image) {image.hidden = true;}
      return;
    }
    const scale = Math.min(stage.clientWidth / video.videoWidth, stage.clientHeight / video.videoHeight);
    const renderedWidth = video.videoWidth * scale;
    const renderedHeight = video.videoHeight * scale;
    const renderedLeft = (stage.clientWidth - renderedWidth) / 2;
    const renderedTop = (stage.clientHeight - renderedHeight) / 2;
    image.hidden = false;
    image.style.left = `${renderedLeft + (cursor.x - cursor.hotSpotX) * scale}px`;
    image.style.top = `${renderedTop + (cursor.y - cursor.hotSpotY) * scale}px`;
    image.style.width = `${cursor.width * scale}px`;
    image.style.height = `${cursor.height * scale}px`;
  }

  function closeStream() {
    cancelScreenGesture();
    applyViewTransform(identityScreenViewTransform);
    setTwoFingerMode("scroll");
    window.clearTimeout(credentialRenewalRef.current);
    credentialRenewalRef.current = undefined;
    window.clearTimeout(renewalRestartRef.current);
    renewalRestartRef.current = undefined;
    window.clearTimeout(disconnectedRecoveryRef.current);
    disconnectedRecoveryRef.current = undefined;
    void exitImmersive();
    pendingOfferRef.current = null;
    pendingAnswerRef.current = null;
    eventsRef.current?.close();
    eventsRef.current = null;
    const peer = peerRef.current;
    peerRef.current = null;
    peer?.close();
    if (videoRef.current) {videoRef.current.srcObject = null;}
    setStreaming(false);
    setViewing(false);
    setPlaybackBlocked(false);
    hasVisualFrameRef.current = false;
    cursorStateRef.current = null;
    if (cursorUrlRef.current) {URL.revokeObjectURL(cursorUrlRef.current); cursorUrlRef.current = null;}
    if (cursorRef.current) {cursorRef.current.hidden = true;}
  }

  function scheduleCredentialRenewal(expiresAt: string | null | undefined, displayId: string) {
    window.clearTimeout(credentialRenewalRef.current);
    credentialRenewalRef.current = undefined;
    if (activePc.transportMode !== "relay" || !expiresAt) {return;}
    const expires = Date.parse(expiresAt);
    if (!Number.isFinite(expires)) {return;}
    const delay = Math.max(5_000, expires - Date.now() - 60_000);
    credentialRenewalRef.current = window.setTimeout(() => {
      send({ type: "screen.view.stop", operationId: createLocalId() });
      closeStream();
      setStatus("Renewing secure relay credentials...");
      renewalRestartRef.current = window.setTimeout(() => start(displayId), 250);
    }, delay);
  }

  const onControlResult = useEffectEvent((message: Parameters<Parameters<typeof subscribeScreenViewResults>[0]>[0]) => {
    if (message.type === "screen.view.sources.result") {
      if (!message.succeeded) {setStatus(message.message); return;}
      setSources(message.sources);
      const preferredSource = message.sources.find((source) => source.isPrimary) ?? message.sources[0];
      setSelected((current) => current.length > 0 ? current : (preferredSource?.id ?? ""));
      if (message.sources.length === 0) {setStatus("No displays are available to mirror.");}
      else if (message.sources.length === 1) {start(message.sources[0]!.id);}
      else {setStatus("Choose the display you want to mirror.");}
    } else if (message.type === "screen.view.start.result") {
      void acceptStart(message);
    } else if (message.type === "screen.view.answer.result") {
      if (pendingAnswerRef.current !== message.operationId) {return;}
      pendingAnswerRef.current = null;
      if (!message.succeeded) {closeStream(); setStatus(message.message);}
    } else if (message.type === "screen.view.source.result") {
      setStatus(message.message);
    } else if (message.type === "screen.view.stop.result") {
      if (pendingStopRef.current === message.operationId) {
        pendingStopRef.current = null;
      }
    } else if (message.type === "screen.view.ended") {
      closeStream();
      setStatus(message.message);
    }
  });
  const stopLocalStream = useEffectEvent(closeStream);

  useEffect(() => {
    if (capability.canView) {send({ type: "screen.view.sources.get", operationId: createLocalId() });}
    return subscribeScreenViewResults(onControlResult);
  }, [capability.canView, send]);

  useEffect(() => () => {
    send({ type: "screen.view.stop", operationId: createLocalId() });
    stopLocalStream();
  }, [send]);

  return <section ref={workspaceRef} className={`screen-view-workspace${immersive ? " is-immersive" : ""}`}>
    <header className="screen-view-header">
      <button type="button" className="screen-view-icon-button" onClick={onBack} aria-label="Back"><ChevronLeft /></button>
      <div><span className="screen-view-eyebrow">SCREEN</span><strong>Live mirror</strong></div>
      <span className={`screen-view-live-pill${viewing ? " active" : ""}`}>{viewing ? "LIVE" : streaming ? "WAITING" : "READY"}</span>
    </header>
    <div ref={stageRef} className="screen-view-stage" onTouchStart={onScreenTouchStart} onTouchMove={onScreenTouchMove} onTouchEnd={onScreenTouchEnd} onTouchCancel={onScreenTouchCancel}>
      {viewing && <button
        type="button"
        className="screen-view-fullscreen-toggle"
        onTouchStart={stopScreenGesture}
        onTouchMove={stopScreenGesture}
        onTouchEnd={stopScreenGesture}
        onTouchCancel={stopScreenGesture}
        onClick={() => {if (immersive) {void exitImmersive();} else {void enterImmersive();}}}
        aria-label={immersive ? "Exit full screen" : "View PC screen full screen"}
        title={immersive ? "Exit full screen" : "View full screen"}
      >{immersive ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}</button>}
      {viewing && <button
        type="button"
        className="screen-view-two-finger-mode"
        onTouchStart={stopScreenGesture}
        onTouchMove={stopScreenGesture}
        onTouchEnd={stopScreenGesture}
        onTouchCancel={stopScreenGesture}
        onClick={() => {
          setTwoFingerMode(twoFingerMode === "zoom" ? "scroll" : "zoom");
        }}
        aria-label={`Two-finger mode: ${twoFingerMode === "scroll" ? "Scroll" : "Zoom"}. Switch to ${twoFingerMode === "scroll" ? "Zoom" : "Scroll"}`}
      >{twoFingerMode === "scroll" ? "Scroll" : "Zoom"}</button>}
      <div className={`screen-view-content${viewTransform.scale > 1.01 ? " zoomed" : ""}`} style={viewTransform.scale > 1.01 ? { transform: `translate3d(${viewTransform.x}px, ${viewTransform.y}px, 0) scale(${viewTransform.scale})` } : undefined}>
        <video
          ref={videoRef}
          className="screen-view-video"
          aria-label="Mirrored PC display video"
          autoPlay
          muted
          playsInline
          onLoadedData={() => {hasVisualFrameRef.current = true; setViewing(true); setPlaybackBlocked(false); setStatus("Live - Encrypted WebRTC"); requestAnimationFrame(positionCursor);}}
          onPlaying={() => {hasVisualFrameRef.current = true; setViewing(true); setPlaybackBlocked(false); setStatus("Live - Encrypted WebRTC");}}
        />
        <img ref={cursorRef} className="screen-view-cursor" alt="" hidden />
        {!viewing && !playbackBlocked && <div className="screen-view-placeholder"><MonitorUp /><strong>Your PC display appears here</strong><span>Video only. Touch gestures remain relative.</span></div>}
      </div>
      {playbackBlocked && <button
        type="button"
        className="screen-view-playback-button"
        onTouchStart={stopScreenGesture}
        onTouchMove={stopScreenGesture}
        onTouchEnd={stopScreenGesture}
        onTouchCancel={stopScreenGesture}
        onClick={() => {void playVideo();}}
      ><Play aria-hidden="true" /> Show video</button>}
      {viewTransform.scale > 1.01 && <button
        type="button"
        className="screen-view-zoom-reset"
        onTouchStart={stopScreenGesture}
        onTouchMove={stopScreenGesture}
        onTouchEnd={stopScreenGesture}
        onTouchCancel={stopScreenGesture}
        onClick={() => {applyViewTransform(identityScreenViewTransform); setTwoFingerMode("scroll");}}
        aria-label="Reset screen zoom"
      >{viewTransform.scale.toFixed(1)}×</button>}
    </div>
    <div className="screen-view-controls">
      {sources.length > 1 && <label>Display<select value={selected} onChange={(event) => selectSource(event.target.value)}>{sources.map((source) => <option key={source.id} value={source.id}>{source.label} - {source.width}x{source.height}</option>)}</select></label>}
      <p role="status">{status}</p>
      <div className="screen-view-actions">
        <button type="button" disabled={!viewing} onClick={() => send({ type: "pointer.button", button: "left", action: "click" })}><MousePointer2 /> Click</button>
        <button type="button" disabled={!viewing} onClick={onOpenKeyboard}><Keyboard /> Keys</button>
        {streaming ? <button type="button" className="danger" onClick={stop}><Square /> Stop</button> : <button type="button" className="primary" disabled={!selected || !capability.canView || capability.requiresRepair} onClick={() => start()}><MonitorUp /> Start</button>}
      </div>
    </div>
  </section>;
}

function stopScreenGesture(event: TouchEvent<HTMLElement>) {
  event.stopPropagation();
}

function waitForIceGathering(peer: RTCPeerConnection, allowSettledRelayCandidates = false): Promise<void> {
  if (peer.iceGatheringState === "complete") {return Promise.resolve();}
  return new Promise((resolve, reject) => {
    let relaySettleTimeout: number | undefined;
    const cleanup = () => {
      window.clearTimeout(gatheringTimeout);
      window.clearTimeout(relaySettleTimeout);
      peer.removeEventListener("icegatheringstatechange", onState);
      peer.removeEventListener("icecandidate", onCandidate);
    };
    const finishWithRelayCandidates = () => {
      const sdp = peer.localDescription?.sdp ?? "";
      if (!hasOnlyRelayCandidates(sdp)) {return;}
      cleanup();
      resolve();
    };
    const scheduleRelaySettle = () => {
      if (!allowSettledRelayCandidates) {return;}
      window.clearTimeout(relaySettleTimeout);
      relaySettleTimeout = window.setTimeout(finishWithRelayCandidates, 350);
    };
    const onState = () => {
      if (peer.iceGatheringState === "complete") {cleanup(); resolve();}
    };
    const onCandidate = (event: RTCPeerConnectionIceEvent) => {
      if (isRelayCandidate(event.candidate)) {scheduleRelaySettle();}
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
    if (hasOnlyRelayCandidates(peer.localDescription?.sdp ?? "")) {scheduleRelaySettle();}
  });
}

function isRelayCandidate(candidate: RTCIceCandidate | null): boolean {
  return candidate?.type === "relay" || /\styp\s+relay(?:\s|$)/.test(candidate?.candidate ?? "");
}

function hasOnlyRelayCandidates(sdp: string): boolean {
  const candidates = sdp.split(/\r?\n/).filter((line) => line.startsWith("a=candidate:"));
  return candidates.length > 0 && candidates.every((line) => /\styp\s+relay(?:\s|$)/.test(line));
}
