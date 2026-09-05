import {
  useEffect,
  useEffectEvent,
  useRef,
  useState,
  type MouseEvent as ReactMouseEvent,
  type TouchEvent,
  type WheelEvent as ReactWheelEvent,
} from "react";
import {
  ChevronLeft,
  Camera,
  Circle,
  Keyboard,
  Maximize2,
  Minimize2,
  MonitorUp,
  Mouse,
  MousePointer2,
  Play,
  Square,
  Share2,
  Volume2,
  VolumeX,
  X,
} from "lucide-react";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { createLocalId } from "../../foundation/identity/localId";
import type { TrackpadSettings, TwoFingerMode } from "../../foundation/input/gestures";
import { usePointerInput } from "../../foundation/input/usePointerInput";
import type {
  ClientMessage,
  ScreenViewCapability,
  ScreenViewSource,
  ScreenViewStartResultMessage,
} from "../../foundation/protocol/messages";
import { AnchoredHint } from "../../ui/guidance";
import { subscribeScreenViewResults } from "../../foundation/connection/screenViewResultBus";
import { hashScreenSdp, verifyHostScreenSignature } from "./screenViewCrypto";
import {
  hasOnlyRelayCandidates,
  IceGatheringTimeoutError,
  isRelayCandidate,
  waitForIceGathering,
} from "../../foundation/webrtc/iceGathering";
import { parseScreenPlaintextRecord, type ScreenCursorRecord } from "./screenViewRecords";
import { screenKeyboardMessage } from "./screenKeyboardInput";
import {
  identityScreenViewTransform,
  normalizedScreenPoint,
  screenCursorImagePosition,
  touchPairGeometry,
  updateScreenViewPinch,
  type NormalizedScreenPoint,
  type ScreenViewPinchStart,
  type ScreenViewTransform,
} from "./screenViewTransform";
import { useScreenViewFullscreen } from "./useScreenViewFullscreen";
import { supportsDeviceFileStorage } from "../../foundation/file-transfer/fileTransferDeviceStorage";
import { useFileTransfer } from "../../foundation/file-transfer/useFileTransfer";
import {
  screenViewQualityFromStats,
  startScreenViewQualityMonitor,
  type ScreenViewQualitySample,
} from "./screenViewQuality";
import { hasExpectedScreenMedia } from "./screenViewSdp";
import { ScreenViewRecordingPanel } from "./ScreenViewRecordingPanel";
import { useScreenViewRecording } from "./useScreenViewRecording";
import { screenViewRecordingMaximumDurationMs } from "./screenViewRecording";
import "./screen-view.css";

interface Props {
  activePc: PcProfile;
  browserPreviewState?: "inactive" | "active" | "permission-blocked";
  capability: ScreenViewCapability;
  clientId: string;
  onBack: () => void;
  onOpenKeyboard: () => void;
  onTransferNotice?: (message: string, tone: "success" | "error" | "neutral") => void;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
}

interface PendingOffer {
  operationId: string;
  displayId: string;
  renewalOf?: string;
}
interface PendingSource {
  operationId: string;
  displayId: string;
  previousDisplayId: string;
}

const disconnectedRecoveryMs = 8_000;
const directStartResponseTimeoutMs = 15_000;
const relayStartResponseTimeoutMs = 25_000;

export default function ScreenViewWorkspace({
  activePc,
  browserPreviewState,
  capability,
  clientId,
  onBack,
  onOpenKeyboard,
  onTransferNotice,
  send,
  state,
  trackpadSettings,
}: Props) {
  const [sources, setSources] = useState<ScreenViewSource[]>([]);
  const [selected, setSelected] = useState("");
  const [status, setStatus] = useState(
    browserPreviewState
      ? "Live - Encrypted WebRTC"
      : capability.requiresRepair
        ? "Scan this PC's pairing QR once to trust its screen identity."
        : !capability.enabled
          ? "Screen viewing is unavailable on this PC."
          : !capability.permissionGranted
            ? "Allow this device to view the PC screen in PC permissions."
            : "Choose a display to begin.",
  );
  const [viewing, setViewing] = useState(browserPreviewState !== undefined);
  const [streaming, setStreaming] = useState(browserPreviewState !== undefined);
  const [playbackBlocked, setPlaybackBlocked] = useState(false);
  const [qualityText, setQualityText] = useState("");
  const [soundOn, setSoundOn] = useState(false);
  const [audioAvailable, setAudioAvailable] = useState(browserPreviewState !== undefined);
  const [audioTrackReady, setAudioTrackReady] = useState(browserPreviewState !== undefined);
  const [audioNotice, setAudioNotice] = useState("");
  const [viewTransform, setViewTransform] = useState<ScreenViewTransform>(
    identityScreenViewTransform,
  );
  const [twoFingerMode, setTwoFingerMode] = useState<TwoFingerMode>("zoom");
  const [directPointerActive, setDirectPointerActive] = useState(browserPreviewState === "active");
  const [directPointerGuidance, setDirectPointerGuidance] = useState(
    browserPreviewState === "active",
  );
  const hasFinePointer = useFineHoverPointer();
  const { workspaceRef, immersive, enterImmersive, exitImmersive } = useScreenViewFullscreen();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const stageRef = useRef<HTMLDivElement | null>(null);
  const cursorRef = useRef<HTMLImageElement | null>(null);
  const directPointerSurfaceRef = useRef<HTMLDivElement | null>(null);
  const directPointerButtonRef = useRef<HTMLButtonElement | null>(null);
  const cursorStateRef = useRef<ScreenCursorRecord | null>(null);
  const cursorUrlRef = useRef<string | null>(null);
  const hasVisualFrameRef = useRef(false);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const remoteStreamRef = useRef<MediaStream | null>(null);
  const eventsRef = useRef<RTCDataChannel | null>(null);
  const pendingOfferRef = useRef<PendingOffer | null>(null);
  const activeOperationRef = useRef<string | null>(
    browserPreviewState ? "browser-preview-operation" : null,
  );
  const sourcesRequestRef = useRef<string | null>(null);
  const pendingSourceRef = useRef<PendingSource | null>(null);
  const pendingAnswerRef = useRef<string | null>(null);
  const pendingStopRef = useRef<string | null>(null);
  const blockingStopRef = useRef<string | null>(null);
  const startResponseTimeoutRef = useRef<number | undefined>(undefined);
  const negotiationGenerationRef = useRef(0);
  const credentialRenewalRef = useRef<number | undefined>(undefined);
  const renewalRestartRef = useRef<number | undefined>(undefined);
  const renewalRef = useRef<{ operationId: string; peer: RTCPeerConnection | null } | null>(null);
  const [credentialExpires, setCredentialExpires] = useState(0);
  const recordAfterRenewalRef = useRef(false);
  const disconnectedRecoveryRef = useRef<number | undefined>(undefined);
  const stopQualityMonitorRef = useRef<(() => void) | null>(null);
  const qualitySampleRef = useRef<ScreenViewQualitySample | null>(null);
  const pointerInput = usePointerInput({
    send,
    state,
    trackpadSettings,
    twoFingerMode: "scroll",
    inputContext: "screen-view",
  });
  const viewTransformRef = useRef(viewTransform);
  const pinchRef = useRef<(ScreenViewPinchStart & { mode: "local" | "remote" }) | null>(null);
  const suppressPointerUntilClearRef = useRef(false);
  const directPointerActiveRef = useRef(browserPreviewState === "active");
  const heldDirectButtonsRef = useRef<Set<"left" | "right">>(new Set());
  const lastDirectPointRef = useRef<NormalizedScreenPoint>({ x: 0.5, y: 0.5 });
  const pendingDirectMoveRef = useRef<NormalizedScreenPoint | null>(null);
  const directMoveFrameRef = useRef<number | undefined>(undefined);
  const directGuidanceTimeoutRef = useRef<number | undefined>(undefined);
  const directWheelRemainderRef = useRef({ dx: 0, dy: 0 });
  const screenshotTransfer = useFileTransfer(
    activePc,
    clientId,
    state === "paired" &&
      capability.canView &&
      capability.permissionGranted &&
      capability.screenshot?.transferPermissionGranted === true,
    send,
    undefined,
    onTransferNotice,
  );
  const recording = useScreenViewRecording(onTransferNotice);
  const supportsScreenshotStorage = supportsDeviceFileStorage();
  const screenshotBusy =
    screenshotTransfer.presentation.active || screenshotTransfer.presentation.readyToSave;

  function traceScreenView(event: string, detail?: string) {
    if (import.meta.env.DEV) console.debug(`[screen_view] ${event}`, detail ?? "");
  }

  function applyViewTransform(next: ScreenViewTransform) {
    viewTransformRef.current = next;
    setViewTransform(next);
    requestAnimationFrame(positionCursor);
  }

  function sendDirectButton(
    button: "left" | "right",
    action: "down" | "up",
    point: NormalizedScreenPoint,
  ) {
    if (selected) {
      send({
        type: "screen.pointer.button",
        inputContext: "screen-view",
        displayId: selected,
        ...point,
        button,
        action,
      });
    }
  }

  function releaseDirectButtons(point = lastDirectPointRef.current) {
    for (const button of heldDirectButtonsRef.current) {
      sendDirectButton(button, "up", point);
    }
    heldDirectButtonsRef.current.clear();
  }

  function disableDirectPointer() {
    releaseDirectButtons();
    directPointerActiveRef.current = false;
    pendingDirectMoveRef.current = null;
    window.cancelAnimationFrame(directMoveFrameRef.current ?? 0);
    directMoveFrameRef.current = undefined;
    window.clearTimeout(directGuidanceTimeoutRef.current);
    directGuidanceTimeoutRef.current = undefined;
    setDirectPointerGuidance(false);
    setDirectPointerActive(false);
  }

  function enableDirectPointer() {
    if (!viewing || capability.directPointer?.permissionGranted !== true) {
      return;
    }
    directPointerActiveRef.current = true;
    setDirectPointerActive(true);
    setDirectPointerGuidance(true);
    stageRef.current?.focus({ preventScroll: true });
    window.clearTimeout(directGuidanceTimeoutRef.current);
    directGuidanceTimeoutRef.current = window.setTimeout(
      () => setDirectPointerGuidance(false),
      4_000,
    );
  }

  function pointFromDirectSurface(clientX: number, clientY: number, clamp = false) {
    const surface = directPointerSurfaceRef.current;
    return surface
      ? normalizedScreenPoint(clientX, clientY, surface.getBoundingClientRect(), clamp)
      : null;
  }

  function onDirectMouseMove(event: ReactMouseEvent<HTMLDivElement>) {
    const point = pointFromDirectSurface(event.clientX, event.clientY);
    if (!point) {
      releaseDirectButtons(
        pointFromDirectSurface(event.clientX, event.clientY, true) ?? lastDirectPointRef.current,
      );
      return;
    }
    lastDirectPointRef.current = point;
    pendingDirectMoveRef.current = point;
    if (directMoveFrameRef.current !== undefined) {
      return;
    }
    directMoveFrameRef.current = requestAnimationFrame(() => {
      directMoveFrameRef.current = undefined;
      const next = pendingDirectMoveRef.current;
      pendingDirectMoveRef.current = null;
      if (next && directPointerActiveRef.current && selected) {
        send({
          type: "screen.pointer.move",
          inputContext: "screen-view",
          displayId: selected,
          ...next,
        });
      }
    });
  }

  function onDirectMouseDown(event: ReactMouseEvent<HTMLDivElement>) {
    const button = directMouseButton(event.button);
    const point = pointFromDirectSurface(event.clientX, event.clientY);
    if (!button || !point) {
      return;
    }
    event.preventDefault();
    lastDirectPointRef.current = point;
    if (!heldDirectButtonsRef.current.has(button)) {
      sendDirectButton(button, "down", point);
      heldDirectButtonsRef.current.add(button);
    }
  }

  function onDirectMouseUp(event: ReactMouseEvent<HTMLDivElement>) {
    const button = directMouseButton(event.button);
    if (!button || !heldDirectButtonsRef.current.has(button)) {
      return;
    }
    event.preventDefault();
    const point =
      pointFromDirectSurface(event.clientX, event.clientY, true) ?? lastDirectPointRef.current;
    lastDirectPointRef.current = point;
    sendDirectButton(button, "up", point);
    heldDirectButtonsRef.current.delete(button);
  }

  function onDirectWheel(event: ReactWheelEvent<HTMLDivElement>) {
    const point = pointFromDirectSurface(event.clientX, event.clientY);
    if (!point || !selected) {
      return;
    }
    event.preventDefault();
    lastDirectPointRef.current = point;
    const scale = event.deltaMode === 0 ? 1 / 12 : event.deltaMode === 1 ? 3 : 20;
    const accumulatedX = directWheelRemainderRef.current.dx + event.deltaX * scale;
    const accumulatedY = directWheelRemainderRef.current.dy + event.deltaY * scale;
    const dx = Math.trunc(accumulatedX);
    const dy = Math.trunc(accumulatedY);
    directWheelRemainderRef.current = { dx: accumulatedX - dx, dy: accumulatedY - dy };
    if (dx !== 0 || dy !== 0) {
      send({
        type: "screen.pointer.wheel",
        inputContext: "screen-view",
        displayId: selected,
        ...point,
        dx,
        dy,
      });
    }
  }

  function onDirectContextMenu(event: ReactMouseEvent<HTMLDivElement>) {
    if (!directPointerActiveRef.current) {
      return;
    }
    event.preventDefault();
    if (event.button === 2) {
      return;
    }
    const point = pointFromDirectSurface(event.clientX, event.clientY);
    if (!point) {
      return;
    }
    lastDirectPointRef.current = point;
    sendDirectButton("right", "down", point);
    sendDirectButton("right", "up", point);
  }

  const resetViewTransform = useEffectEvent(() => applyViewTransform(identityScreenViewTransform));
  const disableDirectPointerEffect = useEffectEvent(disableDirectPointer);
  const sendDirectButtonEffect = useEffectEvent(sendDirectButton);

  useEffect(() => {
    const stage = stageRef.current;
    if (!stage || typeof ResizeObserver === "undefined") {
      return;
    }
    let width = stage.clientWidth;
    let height = stage.clientHeight;
    const observer = new ResizeObserver(() => {
      const nextWidth = stage.clientWidth;
      const nextHeight = stage.clientHeight;
      if (Math.abs(nextWidth - width) > 1 || Math.abs(nextHeight - height) > 1) {
        width = nextWidth;
        height = nextHeight;
        resetViewTransform();
        requestAnimationFrame(positionCursor);
      }
    });
    observer.observe(stage);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (hasFinePointer && capability.directPointer?.permissionGranted === true && viewing) {
      return;
    }
    const frame = requestAnimationFrame(disableDirectPointerEffect);
    return () => cancelAnimationFrame(frame);
  }, [capability.directPointer?.permissionGranted, hasFinePointer, viewing]);

  useEffect(() => {
    if (!directPointerActive) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      const message = screenKeyboardMessage(event);
      if (!message) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      send({ ...message, inputContext: "screen-view" });
    };
    const onMouseUp = (event: MouseEvent) => {
      const button = directMouseButton(event.button);
      if (!button || !heldDirectButtonsRef.current.has(button)) {
        return;
      }
      const point =
        pointFromDirectSurface(event.clientX, event.clientY, true) ?? lastDirectPointRef.current;
      sendDirectButtonEffect(button, "up", point);
      heldDirectButtonsRef.current.delete(button);
    };
    window.addEventListener("keydown", onKeyDown, true);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("keydown", onKeyDown, true);
      window.removeEventListener("mouseup", onMouseUp);
    };
  }, [directPointerActive, selected, send]);

  function onScreenTouchStart(event: TouchEvent<HTMLDivElement>) {
    if (!viewing) {
      event.preventDefault();
      return;
    }
    if (suppressPointerUntilClearRef.current) {
      event.preventDefault();
      return;
    }
    pointerInput.onTouchStart(event);
    if (event.targetTouches.length !== 2 || !stageRef.current) {
      return;
    }
    const bounds = stageRef.current.getBoundingClientRect();
    const geometry = touchPairGeometry(event.targetTouches, bounds.left, bounds.top);
    if (!geometry) {
      return;
    }
    const local = twoFingerMode === "zoom";
    pinchRef.current = {
      ...geometry,
      transform: viewTransformRef.current,
      mode: local ? "local" : "remote",
    };
    if (local) {
      pointerInput.onTouchCancel(event);
    }
  }

  function onScreenTouchMove(event: TouchEvent<HTMLDivElement>) {
    if (!viewing) {
      event.preventDefault();
      return;
    }
    const pinch = pinchRef.current;
    if (!pinch || event.targetTouches.length < 2 || !stageRef.current) {
      if (!suppressPointerUntilClearRef.current) {
        pointerInput.onTouchMove(event);
      } else {
        event.preventDefault();
      }
      return;
    }
    const bounds = stageRef.current.getBoundingClientRect();
    const geometry = touchPairGeometry(event.targetTouches, bounds.left, bounds.top);
    if (!geometry) {
      return;
    }
    if (pinch.mode === "remote") {
      pointerInput.onTouchMove(event);
      return;
    }
    event.preventDefault();
    applyViewTransform(
      updateScreenViewPinch(
        pinch,
        geometry.distance,
        geometry.midpointX,
        geometry.midpointY,
        stageRef.current.clientWidth,
        stageRef.current.clientHeight,
      ),
    );
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

  function start(displayId = selected, renewalOf?: string) {
    if (
      !displayId ||
      !activePc.hostIdentityPublicKey ||
      capability.requiresRepair ||
      !capability.canView ||
      pendingOfferRef.current ||
      blockingStopRef.current ||
      (peerRef.current && !renewalOf) ||
      renewalRef.current
    ) {
      return;
    }
    if (typeof RTCPeerConnection === "undefined") {
      setStatus("This browser does not provide WebRTC screen playback.");
      return;
    }
    const operationId = createLocalId();
    traceScreenView(renewalOf ? "renewal_requested" : "start_requested");
    const transcript = `VolturaAir screen-view:start:v2:${clientId}:${operationId}:${displayId}${renewalOf ? `:renew:${renewalOf}` : ""}`;
    const signature = signClientPayload(clientId, activePc.id, transcript);
    if (!signature) {
      setStatus("The reconnect key is unavailable. Pair this device again.");
      return;
    }
    pendingOfferRef.current = { operationId, displayId, ...(renewalOf ? { renewalOf } : {}) };
    if (renewalOf) {
      renewalRef.current = { operationId, peer: null };
    } else {
      setStreaming(true);
      setStatus("Preparing encrypted WebRTC mirror...");
    }
    send({
      type: "screen.view.start",
      operationId,
      displayId,
      clientSignature: signature,
      ...(renewalOf ? { renewalOf } : {}),
    });
    window.clearTimeout(startResponseTimeoutRef.current);
    startResponseTimeoutRef.current = window.setTimeout(
      () => {
        if (pendingOfferRef.current?.operationId !== operationId) {
          return;
        }
        pendingOfferRef.current = null;
        if (renewalOf) {
          abandonRenewal();
          return;
        }
        cancelHostCapture(
          "The PC did not respond to the screen-view request. Canceling the pending capture...",
        );
      },
      activePc.transportMode === "relay"
        ? relayStartResponseTimeoutMs
        : directStartResponseTimeoutMs,
    );
  }

  function cancelHostCapture(message: string) {
    const operationId = createLocalId();
    pendingStopRef.current = operationId;
    blockingStopRef.current = operationId;
    send({ type: "screen.view.stop", operationId });
    closeStream();
    setStatus(message);
  }

  function selectSource(displayId: string) {
    disableDirectPointer();
    if (!streaming) {
      setSelected(displayId);
      return;
    }
    const operationId = createLocalId();
    pendingSourceRef.current = { operationId, displayId, previousDisplayId: selected };
    setStatus("Switching display...");
    send({ type: "screen.view.source.set", operationId, displayId });
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
      traceScreenView("playback_started");
      if (videoRef.current !== video || video.srcObject !== source || !peerRef.current) {
        return;
      }
      setPlaybackBlocked(false);
    } catch (error) {
      traceScreenView("playback_failed", error instanceof Error ? error.name : "unknown");
      if (videoRef.current !== video || video.srcObject !== source || !peerRef.current) {
        return;
      }
      if (error instanceof DOMException && error.name === "NotAllowedError") {
        setPlaybackBlocked(true);
        setStatus("Video is ready. Tap Show video to allow playback.");
      } else {
        setPlaybackBlocked(false);
        setStatus("The connected WebRTC video could not begin playback.");
      }
    }
  }

  async function toggleSound() {
    if (recording.lockSound) {
      setAudioNotice("Sound is fixed until this recording stops.");
      return;
    }
    const video = videoRef.current;
    if (!video || !audioAvailable || !audioTrackReady) {
      setAudioNotice("PC sound is unavailable. Video is still available.");
      return;
    }
    if (soundOn) {
      video.muted = true;
      setSoundOn(false);
      return;
    }
    try {
      video.muted = false;
      await video.play();
      setSoundOn(true);
      setAudioNotice("");
    } catch {
      video.muted = true;
      setSoundOn(false);
      setAudioNotice("Tap Sound again to allow PC audio playback.");
    }
  }

  async function acceptStart(message: ScreenViewStartResultMessage) {
    const pending = pendingOfferRef.current;
    if (pending?.operationId !== message.operationId) {
      return;
    }
    pendingOfferRef.current = null;
    window.clearTimeout(startResponseTimeoutRef.current);
    startResponseTimeoutRef.current = undefined;
    const renewing = pending.renewalOf !== undefined;
    const failStart = (text: string) => {
      if (renewing) {
        abandonRenewal();
      } else {
        cancelHostCapture(text);
      }
    };
    if (message.displayId !== pending.displayId) {
      if (message.succeeded) {
        failStart(
          "The PC returned a screen offer for the wrong display. Canceling the PC capture...",
        );
      } else {
        if (renewing) {
          abandonRenewal();
          return;
        }
        setStatus("The PC returned a mismatched screen-view response.");
        setStreaming(false);
      }
      return;
    }
    if (
      !message.succeeded ||
      !message.offerSdp ||
      !message.hostSignature ||
      !activePc.hostIdentityPublicKey
    ) {
      if (message.succeeded) {
        failStart(message.message);
      } else {
        if (renewing) {
          abandonRenewal();
          return;
        }
        setStatus(message.message);
        setStreaming(false);
      }
      return;
    }
    if (!hasExpectedScreenMedia(message.offerSdp, "sendonly")) {
      failStart(
        "The PC did not offer the expected H.264 video and Opus audio connection. Canceling the PC capture...",
      );
      return;
    }
    const offerHash = hashScreenSdp(message.offerSdp);
    const hostTranscript = `VolturaAir screen-view:offer:v2:${clientId}:${message.operationId}:${pending.displayId}:${offerHash}${pending.renewalOf ? `:renew:${pending.renewalOf}` : ""}`;
    if (
      !verifyHostScreenSignature(
        activePc.hostIdentityPublicKey,
        message.hostSignature,
        hostTranscript,
      )
    ) {
      failStart(
        "The PC identity signature was invalid. Canceling the PC capture; no pixels were rendered.",
      );
      return;
    }
    if (!renewing) {
      activeOperationRef.current = message.operationId;
      setSoundOn(false);
      setAudioAvailable(false);
      setAudioTrackReady(false);
      setAudioNotice("");
      if (videoRef.current) {
        videoRef.current.muted = true;
      }
    }

    const relayMode = activePc.transportMode === "relay";
    if (relayMode && (!message.iceServers || message.iceServers.length === 0)) {
      failStart(
        "TURN credentials were unavailable. Canceling the PC capture; commands remain connected.",
      );
      return;
    }
    let peer: RTCPeerConnection;
    try {
      peer = new RTCPeerConnection({
        iceServers: message.iceServers ?? [],
        iceTransportPolicy: relayMode ? "relay" : "all",
        bundlePolicy: "max-bundle",
        rtcpMuxPolicy: "require",
      });
      traceScreenView("peer_created", relayMode ? "relay" : "direct");
    } catch {
      failStart(
        "This browser could not create the encrypted screen connection. Canceling the PC capture...",
      );
      return;
    }
    const negotiationGeneration = renewing
      ? negotiationGenerationRef.current
      : ++negotiationGenerationRef.current;
    const isCurrentNegotiation = () =>
      (peerRef.current === peer || renewalRef.current?.peer === peer) &&
      negotiationGenerationRef.current === negotiationGeneration;
    let relayCandidateCount = 0;
    let lastIceErrorCode: number | null = null;
    const stream = new MediaStream();
    let events: RTCDataChannel | null = null;
    if (renewing) {
      renewalRef.current = { operationId: message.operationId, peer };
      startResponseTimeoutRef.current = window.setTimeout(() => {
        if (renewalRef.current?.peer === peer) {
          abandonRenewal();
        }
      }, 30_000);
    } else {
      peerRef.current = peer;
      startQualityMonitor(peer);
    }
    const promoteRenewal = () => {
      if (
        renewalRef.current?.peer !== peer ||
        peer.connectionState !== "connected" ||
        !stream.getVideoTracks().some((track) => !track.muted)
      ) {
        return;
      }
      const retired = peerRef.current;
      const retiredEvents = eventsRef.current;
      peerRef.current = peer;
      eventsRef.current = events;
      remoteStreamRef.current = stream;
      renewalRef.current = null;
      window.clearTimeout(startResponseTimeoutRef.current);
      startResponseTimeoutRef.current = undefined;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
      }
      retiredEvents?.close();
      retired?.close();
      startQualityMonitor(peer);
      scheduleCredentialRenewal(message.turnExpiresAt);
      setAudioTrackReady(stream.getAudioTracks().length > 0);
      void playVideo();
      if (recordAfterRenewalRef.current) {
        recordAfterRenewalRef.current = false;
        void recording.start(stream, videoRef.current?.muted === false);
      }
    };
    peer.addEventListener("icecandidate", (event) => {
      if (!isCurrentNegotiation()) {
        return;
      }
      if (isRelayCandidate(event.candidate)) {
        relayCandidateCount += 1;
      }
    });
    peer.addEventListener("icecandidateerror", (event) => {
      if (!isCurrentNegotiation()) {
        return;
      }
      lastIceErrorCode = event.errorCode;
    });
    peer.addEventListener("track", (event) => {
      if (!isCurrentNegotiation()) {
        return;
      }
      if ((event.track.kind !== "video" && event.track.kind !== "audio") || !videoRef.current) {
        return;
      }
      traceScreenView("remote_track_received", event.track.kind);
      if (stream.getTracks().some((track) => track.kind === event.track.kind)) {
        if (event.track.kind === "video") {
          void playVideo();
        }
        return;
      }
      stream.addTrack(event.track);
      if (renewalRef.current?.peer === peer) {
        event.track.addEventListener("unmute", promoteRenewal, { once: true });
        promoteRenewal();
        return;
      }
      remoteStreamRef.current = stream;
      videoRef.current.srcObject = stream;
      if (event.track.kind === "audio") {
        setAudioTrackReady(true);
      } else {
        void playVideo();
      }
    });
    peer.addEventListener("datachannel", (event) => {
      if (!isCurrentNegotiation()) {
        event.channel.close();
        return;
      }
      if (event.channel.label !== "screen-events") {
        event.channel.close();
        return;
      }
      const channel = event.channel;
      if (events) {
        channel.close();
        return;
      }
      events = channel;
      if (peerRef.current === peer) {
        eventsRef.current = channel;
      }
      channel.binaryType = "arraybuffer";
      channel.addEventListener("message", (messageEvent) => {
        if (!isCurrentNegotiation() || eventsRef.current !== channel) {
          return;
        }
        handleScreenEvent(messageEvent);
      });
    });
    peer.addEventListener("connectionstatechange", () => {
      if (!isCurrentNegotiation()) {
        return;
      }
      traceScreenView("connection_state", peer.connectionState);
      if (renewalRef.current?.peer === peer) {
        promoteRenewal();
        if (peer.connectionState === "failed" || peer.connectionState === "closed") {
          abandonRenewal();
        }
        return;
      }
      if (renewalRef.current) {
        return;
      }
      if (peer.connectionState === "connected") {
        window.clearTimeout(disconnectedRecoveryRef.current);
        disconnectedRecoveryRef.current = undefined;
        if (hasVisualFrameRef.current) {
          setViewing(true);
        }
        setStatus("Live - Encrypted WebRTC");
      }
      if (peer.connectionState === "disconnected") {
        traceScreenView("reconnect_started");
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
        traceScreenView("connection_lost", peer.connectionState);
        if (peerRef.current === peer) {
          closeStream();
          setStatus("Screen video connection was lost. Tap Start to reconnect.");
        }
      }
    });

    try {
      await peer.setRemoteDescription({ type: "offer", sdp: message.offerSdp });
      if (!isCurrentNegotiation()) {
        peer.close();
        return;
      }
      const answer = await peer.createAnswer();
      if (!isCurrentNegotiation()) {
        peer.close();
        return;
      }
      await peer.setLocalDescription(answer);
      if (!isCurrentNegotiation()) {
        peer.close();
        return;
      }
      await waitForIceGathering(peer, relayMode);
      if (!isCurrentNegotiation()) {
        peer.close();
        return;
      }
      const answerSdp = peer.localDescription?.sdp;
      if (
        !answerSdp ||
        answerSdp.length > 32 * 1024 ||
        !hasExpectedScreenMedia(answerSdp, "recvonly")
      ) {
        throw new Error("Invalid WebRTC answer.");
      }
      if (relayMode && !hasOnlyRelayCandidates(answerSdp)) {
        throw new Error("The WebRTC answer did not contain relay-only candidates.");
      }
      const answerHash = hashScreenSdp(answerSdp);
      const answerTranscript = `VolturaAir screen-view:answer:v2:${clientId}:${message.operationId}:${pending.displayId}:${offerHash}:${answerHash}`;
      const clientSignature = signClientPayload(clientId, activePc.id, answerTranscript);
      if (!clientSignature) {
        throw new Error("The reconnect key is unavailable.");
      }
      pendingAnswerRef.current = message.operationId;
      send({
        type: "screen.view.answer",
        operationId: message.operationId,
        answerSdp,
        clientSignature,
      });
      if (!renewing) {
        scheduleCredentialRenewal(message.turnExpiresAt);
        setStatus("Connecting encrypted WebRTC mirror...");
      }
    } catch (error) {
      if (!isCurrentNegotiation()) {
        peer.close();
        return;
      }
      if (error instanceof IceGatheringTimeoutError) {
        failStart(
          `Relay candidate gathering timed out (relay candidates: ${relayCandidateCount}, ICE error: ${lastIceErrorCode ?? "none"}). Canceling the PC capture...`,
        );
      } else {
        failStart(
          "This browser could not negotiate the PC's H.264 and Opus WebRTC stream. Canceling the PC capture...",
        );
      }
    }
  }

  function handleScreenEvent(event: MessageEvent) {
    if (!(event.data instanceof ArrayBuffer)) {
      closeStream();
      setStatus("The screen event channel sent invalid data.");
      return;
    }
    try {
      const record = parseScreenPlaintextRecord(new Uint8Array(event.data));
      if (record.type === "cursor") {
        updateCursor(record);
      } else if (record.type === "status") {
        closeStream();
        setStatus(record.message);
      } else if (record.type === "audio-availability") {
        setAudioAvailable(record.available);
        setAudioNotice(record.message);
        if (!record.available) {
          recording.reportAudioUnavailable();
          if (videoRef.current) {
            videoRef.current.muted = true;
          }
          setSoundOn(false);
        }
      } else {
        throw new Error("Unexpected screen event.");
      }
    } catch {
      closeStream();
      setStatus("The screen event channel sent invalid data.");
    }
  }

  function updateCursor(cursor: ScreenCursorRecord) {
    cursorStateRef.current = cursor;
    if (cursor.pngBytes) {
      if (cursorUrlRef.current) {
        URL.revokeObjectURL(cursorUrlRef.current);
      }
      cursorUrlRef.current = URL.createObjectURL(
        new Blob([Uint8Array.from(cursor.pngBytes).buffer], { type: "image/png" }),
      );
      if (cursorRef.current) {
        cursorRef.current.src = cursorUrlRef.current;
      }
    }
    positionCursor();
  }

  function positionCursor() {
    const cursor = cursorStateRef.current;
    const image = cursorRef.current;
    const video = videoRef.current;
    const stage = stageRef.current;
    const surface = directPointerSurfaceRef.current;
    if (surface && video && stage && video.videoWidth > 0 && video.videoHeight > 0) {
      const surfaceScale = Math.min(
        stage.clientWidth / video.videoWidth,
        stage.clientHeight / video.videoHeight,
      );
      const surfaceWidth = video.videoWidth * surfaceScale;
      const surfaceHeight = video.videoHeight * surfaceScale;
      surface.style.left = `${(stage.clientWidth - surfaceWidth) / 2}px`;
      surface.style.top = `${(stage.clientHeight - surfaceHeight) / 2}px`;
      surface.style.width = `${surfaceWidth}px`;
      surface.style.height = `${surfaceHeight}px`;
    }
    if (
      !hasVisualFrameRef.current ||
      !cursor ||
      !image ||
      !video ||
      !stage ||
      !cursor.visible ||
      !image.src ||
      video.videoWidth === 0 ||
      video.videoHeight === 0
    ) {
      if (image) {
        image.hidden = true;
      }
      return;
    }
    const scale = Math.min(
      stage.clientWidth / video.videoWidth,
      stage.clientHeight / video.videoHeight,
    );
    const renderedWidth = video.videoWidth * scale;
    const renderedHeight = video.videoHeight * scale;
    const renderedLeft = (stage.clientWidth - renderedWidth) / 2;
    const renderedTop = (stage.clientHeight - renderedHeight) / 2;
    const cursorPosition = screenCursorImagePosition(
      cursor.x,
      cursor.y,
      renderedLeft,
      renderedTop,
      scale,
    );
    image.hidden = false;
    image.style.left = `${cursorPosition.left}px`;
    image.style.top = `${cursorPosition.top}px`;
    image.style.width = `${cursor.width * scale}px`;
    image.style.height = `${cursor.height * scale}px`;
  }

  function closeStream() {
    traceScreenView("stream_closed");
    abandonRenewal();
    setCredentialExpires(0);
    recording.stop("Screen viewing ended. Recording is ready.");
    activeOperationRef.current = null;
    negotiationGenerationRef.current += 1;
    disableDirectPointer();
    cancelScreenGesture();
    applyViewTransform(identityScreenViewTransform);
    setTwoFingerMode("scroll");
    window.clearTimeout(credentialRenewalRef.current);
    credentialRenewalRef.current = undefined;
    window.clearTimeout(renewalRestartRef.current);
    renewalRestartRef.current = undefined;
    window.clearTimeout(disconnectedRecoveryRef.current);
    disconnectedRecoveryRef.current = undefined;
    window.clearTimeout(startResponseTimeoutRef.current);
    startResponseTimeoutRef.current = undefined;
    stopQualityMonitorRef.current?.();
    stopQualityMonitorRef.current = null;
    qualitySampleRef.current = null;
    setQualityText("");
    void exitImmersive();
    pendingOfferRef.current = null;
    pendingSourceRef.current = null;
    pendingAnswerRef.current = null;
    eventsRef.current?.close();
    eventsRef.current = null;
    const peer = peerRef.current;
    peerRef.current = null;
    peer?.close();
    remoteStreamRef.current = null;
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
    setStreaming(false);
    setViewing(false);
    setPlaybackBlocked(false);
    setSoundOn(false);
    setAudioAvailable(false);
    setAudioTrackReady(false);
    setAudioNotice("");
    hasVisualFrameRef.current = false;
    cursorStateRef.current = null;
    if (cursorUrlRef.current) {
      URL.revokeObjectURL(cursorUrlRef.current);
      cursorUrlRef.current = null;
    }
    if (cursorRef.current) {
      cursorRef.current.hidden = true;
    }
  }

  function startQualityMonitor(peer: RTCPeerConnection) {
    stopQualityMonitorRef.current?.();
    stopQualityMonitorRef.current = null;
    qualitySampleRef.current = null;
    setQualityText("");
    if (typeof peer.getStats !== "function") {
      return;
    }
    stopQualityMonitorRef.current = startScreenViewQualityMonitor(peer, (report) => {
      if (peerRef.current !== peer) {
        return;
      }
      const result = screenViewQualityFromStats(
        report,
        videoRef.current,
        qualitySampleRef.current,
        performance.now(),
      );
      if (!result || peerRef.current !== peer) {
        return;
      }
      qualitySampleRef.current = result.sample;
      setQualityText(result.text);
      const operationId = activeOperationRef.current;
      if (operationId && capability.receiverQualityFeedback && result.feedback) {
        send({ type: "screen.view.quality", operationId, ...result.feedback });
      }
    });
  }

  function abandonRenewal() {
    const renewal = renewalRef.current;
    renewalRef.current = null;
    recordAfterRenewalRef.current = false;
    if (!renewal) {
      return;
    }
    renewal.peer?.close();
    if (pendingOfferRef.current?.operationId === renewal.operationId) {
      pendingOfferRef.current = null;
    }
    if (pendingAnswerRef.current === renewal.operationId) {
      pendingAnswerRef.current = null;
    }
    window.clearTimeout(startResponseTimeoutRef.current);
    startResponseTimeoutRef.current = undefined;
  }

  const renewCredentials = useEffectEvent(() => {
    if (activeOperationRef.current && capability.relayRenewal) {
      start(selected, activeOperationRef.current);
    } else {
      restartExpiredConnection();
    }
  });

  const restartExpiredConnection = useEffectEvent(() => {
    send({ type: "screen.view.stop", operationId: createLocalId() });
    closeStream();
    setStatus("Renewing secure relay credentials...");
    renewalRestartRef.current = window.setTimeout(() => start(selected), 250);
  });

  function scheduleCredentialRenewal(expiresAt: string | null | undefined) {
    const expires = expiresAt ? Date.parse(expiresAt) : 0;
    setCredentialExpires(Number.isFinite(expires) ? expires : 0);
  }

  useEffect(() => {
    if (activePc.transportMode !== "relay" || !credentialExpires) {
      return;
    }
    const renewalTimer = window.setTimeout(
      () => renewCredentials(),
      Math.max(0, credentialExpires - Date.now() - 60_000),
    );
    // Keep the existing bounded recovery if preparation fails; never use expired TURN credentials.
    const expiryTimer = window.setTimeout(
      () => restartExpiredConnection(),
      Math.max(0, credentialExpires - Date.now() - 5_000),
    );
    credentialRenewalRef.current = renewalTimer;
    renewalRestartRef.current = expiryTimer;
    return () => {
      window.clearTimeout(renewalTimer);
      window.clearTimeout(expiryTimer);
    };
  }, [activePc.transportMode, credentialExpires]);

  const onControlResult = useEffectEvent(
    (message: Parameters<Parameters<typeof subscribeScreenViewResults>[0]>[0]) => {
      if (message.type === "screen.view.sources.result") {
        if (message.operationId !== sourcesRequestRef.current) {
          return;
        }
        sourcesRequestRef.current = null;
        if (!message.succeeded) {
          setStatus(message.message);
          return;
        }
        if (new Set(message.sources.map((source) => source.id)).size !== message.sources.length) {
          setStatus("The PC returned an invalid display list.");
          return;
        }
        setSources(message.sources);
        const preferredSource =
          message.sources.find((source) => source.isPrimary) ?? message.sources[0];
        setSelected((current) => (current.length > 0 ? current : (preferredSource?.id ?? "")));
        if (browserPreviewState) {
          setStatus("Live - Encrypted WebRTC");
          return;
        }
        if (message.sources.length === 0) {
          setStatus("No displays are available to mirror.");
        } else if (message.sources.length === 1) {
          start(message.sources[0]!.id);
        } else {
          setStatus("Choose the display you want to mirror.");
        }
      } else if (message.type === "screen.view.start.result") {
        void acceptStart(message);
      } else if (message.type === "screen.view.answer.result") {
        if (pendingAnswerRef.current !== message.operationId) {
          return;
        }
        pendingAnswerRef.current = null;
        if (!message.succeeded) {
          if (renewalRef.current?.operationId === message.operationId) {
            abandonRenewal();
            return;
          }
          closeStream();
          setStatus(message.message);
        }
      } else if (message.type === "screen.view.source.result") {
        const pending = pendingSourceRef.current;
        if (
          pending?.operationId !== message.operationId ||
          pending?.displayId !== message.displayId
        ) {
          return;
        }
        pendingSourceRef.current = null;
        setSelected(message.succeeded ? pending.displayId : pending.previousDisplayId);
        setStatus(message.message);
      } else if (message.type === "screen.view.stop.result") {
        if (pendingStopRef.current === message.operationId) {
          const wasBlocking = blockingStopRef.current === message.operationId;
          if (message.succeeded) {
            pendingStopRef.current = null;
            if (wasBlocking) {
              blockingStopRef.current = null;
              setStatus("The pending screen capture was stopped. Tap Start to try again.");
            }
          } else if (wasBlocking) {
            setStatus(
              "The PC could not confirm that screen capture stopped. Reconnect before trying again.",
            );
          }
        }
      } else if (message.type === "screen.view.ended") {
        if (activeOperationRef.current !== message.operationId) {
          return;
        }
        pendingStopRef.current = null;
        blockingStopRef.current = null;
        closeStream();
        setStatus(message.message);
      }
    },
  );
  const stopLocalStream = useEffectEvent(closeStream);

  useEffect(() => {
    const unsubscribe = subscribeScreenViewResults(onControlResult);
    if (state === "paired" && capability.canView) {
      const operationId = createLocalId();
      sourcesRequestRef.current = operationId;
      send({ type: "screen.view.sources.get", operationId });
    }
    return () => {
      sourcesRequestRef.current = null;
      pendingStopRef.current = null;
      blockingStopRef.current = null;
      unsubscribe();
    };
  }, [activePc.id, capability.canView, send, state]);

  useEffect(() => {
    if (browserPreviewState) {
      return;
    }
    if (state !== "paired" || !capability.canView) {
      stopLocalStream();
    }
    return () => {
      stopLocalStream();
    };
  }, [activePc.id, browserPreviewState, capability.canView, state]);

  useEffect(
    () => () => {
      if (browserPreviewState) {
        return;
      }
      send({ type: "screen.view.stop", operationId: createLocalId() });
      stopLocalStream();
    },
    [browserPreviewState, send],
  );

  return (
    <section
      ref={workspaceRef}
      className={`screen-view-workspace${immersive ? " is-immersive" : ""}`}
    >
      <header className="screen-view-header">
        <button
          type="button"
          className="screen-view-icon-button"
          onClick={() => {
            if (recording.busy) {
              void recording.discard();
            }
            onBack();
          }}
          aria-label="Back"
        >
          <ChevronLeft />
        </button>
        <div>
          <span className="screen-view-eyebrow">SCREEN</span>
          <strong>Live mirror</strong>
        </div>
        <span className={`screen-view-live-pill${viewing ? " active" : ""}`}>
          {viewing ? "LIVE" : streaming ? "WAITING" : "READY"}
        </span>
      </header>
      <div
        ref={stageRef}
        className="screen-view-stage"
        tabIndex={-1}
        onTouchStart={onScreenTouchStart}
        onTouchMove={onScreenTouchMove}
        onTouchEnd={onScreenTouchEnd}
        onTouchCancel={onScreenTouchCancel}
      >
        {viewing && (
          <div className="screen-view-top-actions">
            <button
              type="button"
              className="screen-view-sound-action"
              onTouchStart={stopScreenGesture}
              onTouchMove={stopScreenGesture}
              onTouchEnd={stopScreenGesture}
              onTouchCancel={stopScreenGesture}
              disabled={!audioAvailable || !audioTrackReady || recording.lockSound}
              onClick={() => void toggleSound()}
              aria-label={soundOn ? "Mute PC sound" : "Play PC sound"}
              aria-pressed={soundOn}
              title={
                recording.lockSound
                  ? "Sound is fixed until this recording stops"
                  : !audioAvailable || !audioTrackReady
                    ? "PC sound is unavailable"
                    : soundOn
                      ? "Mute PC sound"
                      : "Play PC sound"
              }
            >
              {soundOn ? <Volume2 aria-hidden="true" /> : <VolumeX aria-hidden="true" />}
            </button>
            {capability.screenshot && activeOperationRef.current && (
              <button
                type="button"
                className="screen-view-camera-action"
                onTouchStart={stopScreenGesture}
                onTouchMove={stopScreenGesture}
                onTouchEnd={stopScreenGesture}
                onTouchCancel={stopScreenGesture}
                disabled={
                  screenshotBusy ||
                  recording.busy ||
                  !supportsScreenshotStorage ||
                  !capability.screenshot.transferPermissionGranted
                }
                onClick={() => {
                  const screenOperationId = activeOperationRef.current;
                  if (screenOperationId && selected) {
                    screenshotTransfer.startScreenCapture({
                      screenOperationId,
                      displayId: selected,
                    });
                  }
                }}
                aria-label="Capture PC screenshot"
                title={
                  !capability.screenshot.transferPermissionGranted
                    ? "Allow Transfer files for this device on the PC"
                    : !supportsScreenshotStorage
                      ? "This browser cannot stage a screenshot for Save or Share"
                      : screenshotBusy
                        ? "Save or discard the current screenshot first"
                        : recording.busy
                          ? "Finish or discard the current recording first"
                          : "Capture this PC display"
                }
              >
                <Camera aria-hidden="true" />
              </button>
            )}
            <button
              type="button"
              className={`screen-view-record-action${recording.presentation.phase === "recording" ? " active" : ""}`}
              onTouchStart={stopScreenGesture}
              onTouchMove={stopScreenGesture}
              onTouchEnd={stopScreenGesture}
              onTouchCancel={stopScreenGesture}
              disabled={
                recording.presentation.phase !== "recording" && (screenshotBusy || recording.busy)
              }
              aria-disabled={!recording.supported}
              aria-label={
                recording.presentation.phase === "recording"
                  ? "Stop screen recording"
                  : "Start screen recording"
              }
              aria-pressed={recording.presentation.phase === "recording"}
              title={
                recording.presentation.phase === "recording"
                  ? "Stop recording"
                  : recording.unsupportedReason ||
                    (screenshotBusy
                      ? "Finish or discard the screenshot first"
                      : recording.busy
                        ? "Save or discard the current recording first"
                        : soundOn
                          ? "Record PC video with sound"
                          : "Record PC video")
              }
              onClick={() => {
                if (!recording.supported) {
                  setStatus(recording.unsupportedReason);
                  return;
                }
                if (recording.presentation.phase === "recording") {
                  recording.stop();
                  return;
                }
                if (
                  capability.relayRenewal &&
                  activePc.transportMode === "relay" &&
                  credentialExpires - Date.now() < screenViewRecordingMaximumDurationMs + 120_000
                ) {
                  recordAfterRenewalRef.current = true;
                  start(selected, activeOperationRef.current ?? undefined);
                  setStatus("Preparing screen recording...");
                } else {
                  void recording.start(remoteStreamRef.current, soundOn);
                }
              }}
            >
              {recording.presentation.phase === "recording" ? (
                <Square aria-hidden="true" />
              ) : (
                <Circle aria-hidden="true" />
              )}
            </button>
            <button
              type="button"
              className="screen-view-fullscreen-toggle"
              onTouchStart={stopScreenGesture}
              onTouchMove={stopScreenGesture}
              onTouchEnd={stopScreenGesture}
              onTouchCancel={stopScreenGesture}
              onClick={() => {
                if (immersive) {
                  void exitImmersive();
                } else {
                  void enterImmersive();
                }
              }}
              aria-label={immersive ? "Exit full screen" : "View PC screen full screen"}
              title={immersive ? "Exit full screen" : "View full screen"}
            >
              {immersive ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
            </button>
          </div>
        )}
        {immersive && viewing && audioNotice && !audioAvailable && (
          <p className="screen-view-audio-overlay" role="status">
            {audioNotice}
          </p>
        )}
        {viewing && (
          <div className="screen-view-overlay-actions">
            <button
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
            >
              {twoFingerMode === "scroll" ? "Scroll" : "Zoom"}
            </button>
            {capability.directPointer && hasFinePointer && (
              <button
                ref={directPointerButtonRef}
                type="button"
                className={`screen-view-mouse-mode${directPointerActive ? " active" : ""}`}
                aria-label="Mouse and keyboard control"
                aria-pressed={directPointerActive}
                aria-disabled={!capability.directPointer.permissionGranted}
                title={
                  capability.directPointer.permissionGranted
                    ? "Control the mirrored PC with this mouse and keyboard"
                    : "Allow Pointer and keyboard for this device on the PC"
                }
                onClick={() => {
                  if (!capability.directPointer?.permissionGranted) {
                    setStatus("Allow Pointer and keyboard for this device in PC permissions.");
                    return;
                  }
                  if (directPointerActiveRef.current) {
                    disableDirectPointer();
                  } else {
                    enableDirectPointer();
                  }
                }}
              >
                <Mouse aria-hidden="true" />
                <Keyboard aria-hidden="true" />
              </button>
            )}
            <AnchoredHint
              anchorRef={directPointerButtonRef}
              open={directPointerGuidance}
              preferredPlacement="above-start"
            >
              Mouse and keyboard control the PC. Select this button to stop.
            </AnchoredHint>
          </div>
        )}
        <div
          className={`screen-view-content${viewTransform.scale > 1.01 ? " zoomed" : ""}`}
          style={
            viewTransform.scale > 1.01
              ? {
                  transform: `translate3d(${viewTransform.x}px, ${viewTransform.y}px, 0) scale(${viewTransform.scale})`,
                }
              : undefined
          }
        >
          <video
            ref={videoRef}
            className="screen-view-video"
            aria-label="Mirrored PC display video"
            autoPlay
            muted={!soundOn}
            playsInline
            onLoadedData={() => {
              traceScreenView("first_frame_rendered", "loadeddata");
              hasVisualFrameRef.current = true;
              setViewing(true);
              setPlaybackBlocked(false);
              setStatus("Live - Encrypted WebRTC");
              requestAnimationFrame(positionCursor);
            }}
            onPlaying={() => {
              traceScreenView("first_frame_rendered", "playing");
              hasVisualFrameRef.current = true;
              setViewing(true);
              setPlaybackBlocked(false);
              setStatus("Live - Encrypted WebRTC");
            }}
          >
            <track kind="captions" label="Live PC audio has no captions" />
          </video>
          <img ref={cursorRef} className="screen-view-cursor" alt="" hidden />
          <div
            ref={directPointerSurfaceRef}
            className={`screen-view-direct-pointer${directPointerActive ? " active" : ""}`}
            onMouseMove={onDirectMouseMove}
            onMouseDown={onDirectMouseDown}
            onMouseUp={onDirectMouseUp}
            onMouseLeave={(event) =>
              releaseDirectButtons(
                pointFromDirectSurface(event.clientX, event.clientY, true) ??
                  lastDirectPointRef.current,
              )
            }
            onWheel={onDirectWheel}
            onContextMenu={onDirectContextMenu}
            aria-hidden="true"
          />
          {!viewing && !playbackBlocked && (
            <div className="screen-view-placeholder">
              <MonitorUp />
              <strong>Your PC display appears here</strong>
              <span>Video and PC sound. Touch gestures remain relative.</span>
            </div>
          )}
        </div>
        {playbackBlocked && (
          <button
            type="button"
            className="screen-view-playback-button"
            onTouchStart={stopScreenGesture}
            onTouchMove={stopScreenGesture}
            onTouchEnd={stopScreenGesture}
            onTouchCancel={stopScreenGesture}
            onClick={() => {
              void playVideo();
            }}
          >
            <Play aria-hidden="true" /> Show video
          </button>
        )}
        {viewTransform.scale > 1.01 && (
          <button
            type="button"
            className="screen-view-zoom-reset"
            onTouchStart={stopScreenGesture}
            onTouchMove={stopScreenGesture}
            onTouchEnd={stopScreenGesture}
            onTouchCancel={stopScreenGesture}
            onClick={() => {
              applyViewTransform(identityScreenViewTransform);
            }}
            aria-label="Reset screen zoom"
          >
            {viewTransform.scale.toFixed(1)}×
          </button>
        )}
        {(screenshotTransfer.presentation.active ||
          screenshotTransfer.presentation.readyToSave) && (
          <div
            className="screen-view-screenshot-transfer"
            role="status"
            onTouchStart={stopScreenGesture}
            onTouchMove={stopScreenGesture}
            onTouchEnd={stopScreenGesture}
            onTouchCancel={stopScreenGesture}
          >
            <div>
              <strong>{screenshotTransfer.presentation.fileName}</strong>
              <span>{screenshotTransfer.presentation.message}</span>
            </div>
            {screenshotTransfer.presentation.active && (
              <progress max={1} value={screenshotTransfer.presentation.progress} />
            )}
            {screenshotTransfer.presentation.readyToSave ? (
              <div className="screen-view-screenshot-ready-actions">
                <button type="button" onClick={() => void screenshotTransfer.saveReadyFile()}>
                  <Share2 aria-hidden="true" /> Save / Share
                </button>
                <button
                  type="button"
                  className="screen-view-screenshot-icon-action"
                  aria-label="Discard screenshot"
                  title="Discard"
                  onClick={() => void screenshotTransfer.discardReadyFile()}
                >
                  <X aria-hidden="true" />
                </button>
              </div>
            ) : (
              <button
                type="button"
                className="screen-view-screenshot-icon-action"
                aria-label="Cancel screenshot transfer"
                onClick={screenshotTransfer.cancel}
              >
                <X aria-hidden="true" />
              </button>
            )}
          </div>
        )}
        <ScreenViewRecordingPanel
          presentation={recording.presentation}
          onDiscard={() => void recording.discard()}
          onSave={() => void recording.saveReadyFile()}
        />
      </div>
      <div className="screen-view-controls">
        {sources.length > 1 && (
          <label>
            Display
            <select value={selected} onChange={(event) => selectSource(event.target.value)}>
              {sources.map((source) => (
                <option key={source.id} value={source.id}>
                  {source.label} - {source.width}x{source.height}
                </option>
              ))}
            </select>
          </label>
        )}
        <div className="screen-view-status-block">
          <p role="status">{status}</p>
          {audioNotice && <p className="screen-view-audio-notice">{audioNotice}</p>}
          {qualityText && (
            <p className="screen-view-quality" aria-hidden="true">
              {qualityText}
            </p>
          )}
        </div>
        <div className="screen-view-actions">
          <button
            type="button"
            disabled={!viewing}
            onClick={() =>
              send({
                type: "pointer.button",
                inputContext: "screen-view",
                button: "left",
                action: "click",
              })
            }
          >
            <MousePointer2 /> Click
          </button>
          <button type="button" disabled={!viewing} onClick={onOpenKeyboard}>
            <Keyboard /> Keys
          </button>
          {streaming ? (
            <button type="button" className="danger" onClick={stop}>
              <Square /> Stop
            </button>
          ) : (
            <button
              type="button"
              className="primary"
              disabled={
                !selected ||
                !capability.canView ||
                capability.requiresRepair ||
                blockingStopRef.current !== null
              }
              onClick={() => start()}
            >
              <MonitorUp /> Start
            </button>
          )}
        </div>
      </div>
    </section>
  );
}

function stopScreenGesture(event: TouchEvent<HTMLElement>) {
  event.stopPropagation();
}

function directMouseButton(button: number): "left" | "right" | null {
  return button === 0 ? "left" : button === 2 ? "right" : null;
}

function useFineHoverPointer() {
  const query = "(any-pointer: fine) and (any-hover: hover)";
  const [matches, setMatches] = useState(
    () => typeof window.matchMedia === "function" && window.matchMedia(query).matches,
  );
  useEffect(() => {
    if (typeof window.matchMedia !== "function") {
      return;
    }
    const media = window.matchMedia(query);
    const update = () => setMatches(media.matches);
    update();
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);
  return matches;
}
