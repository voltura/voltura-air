import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeft, Camera, CameraOff, Maximize2, Minimize2 } from "lucide-react";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { subscribePhoneWebcamResults } from "../../foundation/connection/phoneWebcamResultBus";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { ClientMessage, PhoneWebcamCapability, PhoneWebcamServerMessage } from "../../foundation/protocol/messages";
import { createLocalId } from "../../foundation/identity/localId";
import { hashSessionDescription, verifyHostSessionSignature } from "../../foundation/webrtc/sessionCrypto";
import { hasOnlyRelayCandidates, waitForIceGathering } from "../../foundation/webrtc/iceGathering";
import "./phone-webcam.css";

interface PhoneWebcamWorkspaceProps {
  activePc: PcProfile;
  capability: PhoneWebcamCapability;
  clientId: string;
  connectionEpoch: number;
  onBack: () => void;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
}

interface CameraChoice { deviceId: string; label: string; }
interface PendingStart { operationId: string; stream: MediaStream; settings: MediaTrackSettings; }
interface PendingReplacement { generation: number; stream: MediaStream; stop: () => void; }
interface SendQuality { width?: number; height?: number; fps?: number; bitrateMbps?: number; }
type CameraChangeReason = "selection" | "orientation" | "stalled";

const preferredWidth = 1920;
const preferredHeight = 1080;
const preferredFps = 30;
const stalledStatsSampleLimit = 3;
const stalledRecoveryCooldownMs = 10_000;

export default function PhoneWebcamWorkspace({ activePc, capability, clientId, connectionEpoch, onBack, send, state }: PhoneWebcamWorkspaceProps) {
  const supportedTransport = activePc.transportMode === "secure-direct" || activePc.transportMode === "relay";
  const [cameras, setCameras] = useState<CameraChoice[]>([]);
  const [selectedCameraId, setSelectedCameraId] = useState("");
  const [permissionGranted, setPermissionGranted] = useState(false);
  const [phase, setPhase] = useState<"idle" | "connecting" | "streaming">("idle");
  const [isCameraViewExpanded, setIsCameraViewExpanded] = useState(false);
  const [status, setStatus] = useState(initialStatus(capability));
  const [quality, setQuality] = useState<SendQuality>({});
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const senderRef = useRef<RTCRtpSender | null>(null);
  const pendingRef = useRef<PendingStart | null>(null);
  const replacementRef = useRef<PendingReplacement | null>(null);
  const replacementGenerationRef = useRef(0);
  const acquiringReplacementGenerationRef = useRef<number | null>(null);
  const activeStreamEndedRef = useRef(false);
  const acquiringGenerationRef = useRef<number | null>(null);
  const operationIdRef = useRef<string | null>(null);
  const resumeRef = useRef(false);
  const startRef = useRef<() => void>(() => undefined);
  const renewalTimerRef = useRef<number | undefined>(undefined);
  const statsTimerRef = useRef<number | undefined>(undefined);
  const restartTimerRef = useRef<number | undefined>(undefined);
  const orientationTimerRef = useRef<number | undefined>(undefined);
  const lastStatsRef = useRef<{ bytes: number; framesEncoded?: number; at: number } | null>(null);
  const stalledStatsSamplesRef = useRef(0);
  const stalledRecoveryAfterRef = useRef(0);
  const statsGenerationRef = useRef(0);
  const generationRef = useRef(0);
  const releaseRef = useRef<(notifyHost: boolean, message: string) => void>(() => undefined);
  const changeCameraRef = useRef<(deviceId: string, reason?: CameraChangeReason) => Promise<void>>(() => Promise.resolve());

  const releaseLocal = useCallback((notifyHost: boolean, message: string) => {
    generationRef.current += 1;
    replacementGenerationRef.current += 1;
    window.clearTimeout(renewalTimerRef.current);
    window.clearInterval(statsTimerRef.current);
    window.clearTimeout(restartTimerRef.current);
    window.clearTimeout(orientationTimerRef.current);
    renewalTimerRef.current = undefined;
    statsTimerRef.current = undefined;
    lastStatsRef.current = null;
    stalledStatsSamplesRef.current = 0;
    stalledRecoveryAfterRef.current = 0;
    statsGenerationRef.current += 1;
    acquiringGenerationRef.current = null;
    acquiringReplacementGenerationRef.current = null;
    activeStreamEndedRef.current = false;
    const operationId = operationIdRef.current;
    operationIdRef.current = null;
    pendingRef.current = null;
    senderRef.current = null;
    peerRef.current?.close();
    peerRef.current = null;
    const stream = streamRef.current;
    streamRef.current = null;
    for (const track of stream?.getTracks() ?? []) {track.stop();}
    const replacement = replacementRef.current;
    replacementRef.current = null;
    replacement?.stop();
    if (videoRef.current) {videoRef.current.srcObject = null;}
    if (notifyHost && operationId && state === "paired") {
      send({ type: "phone.webcam.stop", operationId: createLocalId() });
    }
    setPhase("idle");
    setQuality({});
    setStatus(message);
  }, [send, state]);
  useEffect(() => {releaseRef.current = releaseLocal;}, [releaseLocal]);

  const loadCameras = useCallback(async () => {
    if (!navigator.mediaDevices?.getUserMedia || !navigator.mediaDevices.enumerateDevices) {
      setStatus("This browser does not provide camera capture.");
      return;
    }
    let permissionStream: MediaStream | null = null;
    try {
      permissionStream = await navigator.mediaDevices.getUserMedia({ audio: false, video: true });
      if (document.visibilityState !== "visible") {
        setStatus("Camera access is paused while Voltura Air is in the background.");
        return;
      }
      const devices = await navigator.mediaDevices.enumerateDevices();
      const choices = devices
        .filter((device) => device.kind === "videoinput" && device.deviceId)
        .map((device, index) => ({ deviceId: device.deviceId, label: device.label || `Camera ${index + 1}` }));
      setCameras(choices);
      setSelectedCameraId((current) => current && choices.some((choice) => choice.deviceId === current)
        ? current
        : choices.find((choice) => /front/i.test(choice.label))?.deviceId ?? choices[0]?.deviceId ?? "");
      setPermissionGranted(true);
      setStatus(choices.length > 0 ? "Choose a camera, then start webcam." : "No camera is available.");
    } catch (error) {
      setStatus(error instanceof DOMException && error.name === "NotAllowedError"
        ? "Camera access was not allowed."
        : "The phone camera could not be opened.");
    } finally {
      for (const track of permissionStream?.getTracks() ?? []) {track.stop();}
    }
  }, []);

  const openSelectedCamera = useCallback(async (): Promise<{ stream: MediaStream; settings: MediaTrackSettings }> => {
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        ...(selectedCameraId ? { deviceId: { exact: selectedCameraId } } : {}),
        width: { ideal: preferredWidth, max: preferredWidth },
        height: { ideal: preferredHeight, max: preferredHeight },
        frameRate: { ideal: preferredFps, max: preferredFps }
      }
    });
    const track = stream.getVideoTracks()[0];
    if (!track) {throw new Error("No video track.");}
    track.contentHint = "motion";
    return { stream, settings: track.getSettings() };
  }, [selectedCameraId]);

  const start = useCallback(async () => {
    if (!supportedTransport || phase !== "idle" || state !== "paired" || !capability.canUse || !selectedCameraId || !activePc.hostIdentityPublicKey) {return;}
    if (typeof RTCPeerConnection === "undefined") {setStatus("This browser does not provide WebRTC video."); return;}
    const generation = ++generationRef.current;
    acquiringGenerationRef.current = generation;
    setPhase("connecting");
    setStatus("Opening the selected camera…");
    try {
      const opened = await openSelectedCamera();
      if (generationRef.current !== generation || acquiringGenerationRef.current !== generation) {
        opened.stream.getTracks().forEach((track) => track.stop());
        return;
      }
      acquiringGenerationRef.current = null;
      const width = Math.max(1, Math.round(opened.settings.width ?? preferredWidth));
      const height = Math.max(1, Math.round(opened.settings.height ?? preferredHeight));
      const fps = Math.max(1, Math.round(opened.settings.frameRate ?? preferredFps));
      const operationId = createLocalId();
      const transcript = `VolturaAir phone-webcam:start:v1:${clientId}:${operationId}:${width}:${height}:${fps}`;
      const signature = signClientPayload(clientId, activePc.id, transcript);
      if (!signature) {opened.stream.getTracks().forEach((track) => track.stop()); throw new Error("Reconnect key unavailable.");}
      streamRef.current = opened.stream;
      opened.stream.getVideoTracks()[0]?.addEventListener("ended", () => {
        if (generationRef.current !== generation || streamRef.current !== opened.stream) {return;}
        if (acquiringReplacementGenerationRef.current !== null) {
          activeStreamEndedRef.current = true;
          return;
        }
        resumeRef.current = false;
        releaseRef.current(true, "The selected camera stopped.");
      }, { once: true });
      pendingRef.current = { operationId, stream: opened.stream, settings: opened.settings };
      operationIdRef.current = operationId;
      if (videoRef.current) {
        videoRef.current.srcObject = opened.stream;
        await videoRef.current.play().catch(() => undefined);
        if (generationRef.current !== generation || streamRef.current !== opened.stream) {
          if (streamRef.current === opened.stream) {
            opened.stream.getTracks().forEach((track) => track.stop());
            streamRef.current = null;
          }
          return;
        }
      }
      setQuality({ width, height, fps });
      setStatus("Preparing encrypted webcam video…");
      send({ type: "phone.webcam.start", operationId, captureWidth: width, captureHeight: height, captureFps: fps, clientSignature: signature });
    } catch (error) {
      if (generationRef.current === generation) {
        releaseLocal(true, error instanceof DOMException && error.name === "NotAllowedError"
          ? "Camera access was not allowed."
          : "The selected camera could not be started.");
      }
    }
  }, [activePc.hostIdentityPublicKey, activePc.id, capability.canUse, clientId, openSelectedCamera, phase, releaseLocal, selectedCameraId, send, state, supportedTransport]);
  useEffect(() => {startRef.current = () => {void start();};}, [start]);

  const beginStats = useCallback((peer: RTCPeerConnection) => {
    window.clearInterval(statsTimerRef.current);
    statsGenerationRef.current += 1;
    lastStatsRef.current = null;
    stalledStatsSamplesRef.current = 0;
    let pollPending = false;
    statsTimerRef.current = window.setInterval(() => {void (async () => {
      if (pollPending || peerRef.current !== peer) {return;}
      const sender = senderRef.current;
      const track = sender?.track ?? null;
      const statsGeneration = statsGenerationRef.current;
      pollPending = true;
      let report: RTCStatsReport | null;
      try {
        report = await peer.getStats(track).catch(() => null);
      } finally {
        pollPending = false;
      }
      if (!report || peerRef.current !== peer || senderRef.current !== sender ||
          sender?.track !== track || statsGenerationRef.current !== statsGeneration) {return;}
      const outbound = readOutboundVideoReport(report);
      if (outbound) {
        const now = performance.now();
        const previous = lastStatsRef.current;
        const bitrateMbps = previous && now > previous.at ? ((outbound.bytesSent - previous.bytes) * 8) / ((now - previous.at) * 1000) : undefined;
        const progressed = !previous || (outbound.framesEncoded !== undefined && previous.framesEncoded !== undefined
          ? outbound.framesEncoded > previous.framesEncoded
          : outbound.bytesSent > previous.bytes);
        stalledStatsSamplesRef.current = progressed ? 0 : stalledStatsSamplesRef.current + 1;
        lastStatsRef.current = {
          bytes: outbound.bytesSent,
          ...(outbound.framesEncoded === undefined ? {} : { framesEncoded: outbound.framesEncoded }),
          at: now
        };
        setQuality((current) => ({
          ...current,
          ...(outbound.frameWidth === undefined ? {} : { width: outbound.frameWidth }),
          ...(outbound.frameHeight === undefined ? {} : { height: outbound.frameHeight }),
          ...(outbound.framesPerSecond === undefined ? {} : { fps: outbound.framesPerSecond }),
          ...(bitrateMbps === undefined ? {} : { bitrateMbps: Math.max(0, bitrateMbps) })
        }));
        if (stalledStatsSamplesRef.current >= stalledStatsSampleLimit &&
            now >= stalledRecoveryAfterRef.current &&
            acquiringReplacementGenerationRef.current === null) {
          const deviceId = streamRef.current?.getVideoTracks()[0]?.getSettings().deviceId;
          if (deviceId) {
            stalledStatsSamplesRef.current = 0;
            stalledRecoveryAfterRef.current = now + stalledRecoveryCooldownMs;
            void changeCameraRef.current(deviceId, "stalled");
          }
        }
      }
    })();}, 1000);
  }, []);

  const scheduleCredentialRenewal = useCallback((expiresAt?: string | null) => {
    window.clearTimeout(renewalTimerRef.current);
    if (!expiresAt || activePc.transportMode !== "relay") {return;}
    const delay = new Date(expiresAt).getTime() - Date.now() - 60_000;
    renewalTimerRef.current = window.setTimeout(() => {
      if (document.visibilityState !== "visible" || !streamRef.current) {resumeRef.current = true; return;}
      releaseLocal(true, "Refreshing Relay credentials…");
      restartTimerRef.current = window.setTimeout(() => startRef.current(), 250);
    }, Math.max(1000, delay));
  }, [activePc.transportMode, releaseLocal]);

  const acceptOffer = useCallback(async (message: Extract<PhoneWebcamServerMessage, { type: "phone.webcam.start.result" }>) => {
    const pending = pendingRef.current;
    if (pending?.operationId !== message.operationId) {return;}
    const generation = generationRef.current;
    const isCurrent = () => generationRef.current === generation &&
      pendingRef.current === pending &&
      operationIdRef.current === message.operationId;
    if (!message.succeeded || !message.offerSdp || !message.hostSignature || !activePc.hostIdentityPublicKey) {
      releaseLocal(true, message.message);
      return;
    }
    if (!/a=rtpmap:\d+ H264\/90000/i.test(message.offerSdp) || /^m=audio\s/im.test(message.offerSdp)) {
      releaseLocal(true, "The PC did not offer a video-only H.264 webcam connection.");
      return;
    }
    const offerHash = hashSessionDescription(message.offerSdp);
    const hostTranscript = `VolturaAir phone-webcam:offer:v1:${clientId}:${message.operationId}:${offerHash}`;
    if (!verifyHostSessionSignature(activePc.hostIdentityPublicKey, message.hostSignature, hostTranscript)) {
      releaseLocal(true, "The PC identity signature was invalid. Camera video was stopped.");
      return;
    }
    const relayMode = activePc.transportMode === "relay";
    if (relayMode && (!message.iceServers || message.iceServers.length === 0)) {
      releaseLocal(true, "Relay credentials are temporarily unavailable.");
      return;
    }
    const peer = new RTCPeerConnection({
      iceServers: message.iceServers ?? [],
      iceTransportPolicy: relayMode ? "relay" : "all",
      bundlePolicy: "max-bundle",
      rtcpMuxPolicy: "require"
    });
    peerRef.current = peer;
    peer.addEventListener("connectionstatechange", () => {
      if (peerRef.current !== peer) {return;}
      if (peer.connectionState === "connected") {
        setPhase("streaming");
        setStatus(relayMode ? "Streaming through Relay" : "Streaming through Enhanced Direct");
        beginStats(peer);
      } else if (peer.connectionState === "failed" || peer.connectionState === "closed") {
        releaseLocal(false, "Webcam video connection was lost.");
      }
    });
    try {
      await peer.setRemoteDescription({ type: "offer", sdp: message.offerSdp });
      if (!isCurrent()) {peer.close(); return;}
      const transceiver = peer.getTransceivers().find((candidate) => candidate.receiver.track.kind === "video");
      if (!transceiver) {throw new Error("Missing video transceiver.");}
      transceiver.direction = "sendonly";
      const track = pending.stream.getVideoTracks()[0];
      if (!track) {throw new Error("Missing camera track.");}
      await transceiver.sender.replaceTrack(track);
      if (!isCurrent()) {peer.close(); return;}
      senderRef.current = transceiver.sender;
      const parameters = transceiver.sender.getParameters();
      if (parameters.encodings.length === 0) {parameters.encodings = [{}];}
      const encoding = parameters.encodings[0];
      if (!encoding) {throw new Error("Missing sender encoding.");}
      encoding.maxBitrate = message.maximumBitrate ?? 12_000_000;
      encoding.maxFramerate = preferredFps;
      parameters.degradationPreference = "maintain-resolution";
      await transceiver.sender.setParameters(parameters);
      if (!isCurrent()) {peer.close(); return;}
      const answer = await peer.createAnswer();
      if (!isCurrent()) {peer.close(); return;}
      await peer.setLocalDescription(answer);
      if (!isCurrent()) {peer.close(); return;}
      await waitForIceGathering(peer, relayMode);
      if (!isCurrent()) {peer.close(); return;}
      const answerSdp = peer.localDescription?.sdp;
      if (!answerSdp || answerSdp.length > 32 * 1024 || !/a=rtpmap:\d+ H264\/90000/i.test(answerSdp)) {throw new Error("Invalid H.264 answer.");}
      if (relayMode && !hasOnlyRelayCandidates(answerSdp)) {throw new Error("Relay-only candidates required.");}
      const answerHash = hashSessionDescription(answerSdp);
      const answerTranscript = `VolturaAir phone-webcam:answer:v1:${clientId}:${message.operationId}:${offerHash}:${answerHash}`;
      const signature = signClientPayload(clientId, activePc.id, answerTranscript);
      if (!signature) {throw new Error("Reconnect key unavailable.");}
      send({ type: "phone.webcam.answer", operationId: message.operationId, answerSdp, clientSignature: signature });
      scheduleCredentialRenewal(message.turnExpiresAt);
      setStatus("Connecting encrypted webcam video…");
    } catch {
      if (isCurrent()) {
        releaseLocal(true, "This browser could not negotiate H.264 webcam video with the PC.");
      } else {
        peer.close();
      }
    }
  }, [activePc.hostIdentityPublicKey, activePc.id, activePc.transportMode, beginStats, clientId, releaseLocal, scheduleCredentialRenewal, send]);

  const handleResult = useCallback((message: PhoneWebcamServerMessage) => {
    if (message.type === "phone.webcam.start.result") {void acceptOffer(message); return;}
    if (message.type === "phone.webcam.answer.result" && message.operationId === operationIdRef.current) {
      if (!message.succeeded) {releaseLocal(true, message.message);}
      return;
    }
    if (message.type === "phone.webcam.ended" && message.operationId === operationIdRef.current) {
      releaseLocal(false, message.message);
    }
  }, [acceptOffer, releaseLocal]);

  useEffect(() => subscribePhoneWebcamResults(handleResult), [handleResult]);

  const changeCamera = useCallback(async (deviceId: string, reason: CameraChangeReason = "selection") => {
    if (reason === "selection") {window.clearTimeout(orientationTimerRef.current);}
    if (!senderRef.current || phase !== "streaming") {
      setSelectedCameraId(deviceId);
      return;
    }
    const previousCameraId = streamRef.current?.getVideoTracks()[0]?.getSettings().deviceId ?? selectedCameraId;
    setSelectedCameraId(deviceId);
    setStatus(reason === "orientation"
      ? "Refreshing camera after rotation…"
      : reason === "stalled" ? "Restoring camera video…" : "Switching camera…");
    const generation = generationRef.current;
    const replacementGeneration = ++replacementGenerationRef.current;
    acquiringReplacementGenerationRef.current = replacementGeneration;
    const sender = senderRef.current;
    const previousReplacement = replacementRef.current;
    replacementRef.current = null;
    previousReplacement?.stop();
    const ownsReplacement = () => replacementGenerationRef.current === replacementGeneration &&
      generationRef.current === generation && senderRef.current === sender;
    let replacement: MediaStream | null = null;
    let ownedReplacement: PendingReplacement | null = null;
    try {
      const constraints: MediaStreamConstraints = {
        audio: false,
        video: { deviceId: { exact: deviceId }, width: { ideal: preferredWidth, max: preferredWidth }, height: { ideal: preferredHeight, max: preferredHeight }, frameRate: { ideal: preferredFps, max: preferredFps } }
      };
      for (let attempt = 0; attempt < 2; attempt += 1) {
        if (!ownsReplacement()) {return;}
        try {
          replacement = await navigator.mediaDevices.getUserMedia(constraints);
          break;
        } catch (error) {
          const denied = error instanceof DOMException &&
            (error.name === "NotAllowedError" || error.name === "SecurityError");
          if (!ownsReplacement() || denied || attempt === 1) {throw error;}
          const requiresExclusiveCapture = error instanceof DOMException &&
            (error.name === "NotReadableError" || error.name === "AbortError");
          if (requiresExclusiveCapture) {
            const activeStream = streamRef.current;
            streamRef.current = null;
            if (videoRef.current) {videoRef.current.srcObject = null;}
            activeStream?.getTracks().forEach((track) => track.stop());
            activeStreamEndedRef.current = false;
            await sender.replaceTrack(null);
            if (!ownsReplacement()) {return;}
          }
          await new Promise<void>((resolve) => {window.setTimeout(resolve, 200);});
        }
      }
      if (!replacement) {throw new Error("Camera replacement did not open a video stream.");}
      if (replacementGenerationRef.current !== replacementGeneration || generationRef.current !== generation || senderRef.current !== sender) {
        if (acquiringReplacementGenerationRef.current === replacementGeneration) {acquiringReplacementGenerationRef.current = null;}
        replacement.getTracks().forEach((track) => track.stop());
        return;
      }
      let stopped = false;
      ownedReplacement = {
        generation: replacementGeneration,
        stream: replacement,
        stop: () => {
          if (stopped) {return;}
          stopped = true;
          replacement?.getTracks().forEach((track) => track.stop());
        }
      };
      replacementRef.current = ownedReplacement;
      const nextTrack = replacement.getVideoTracks()[0];
      if (!nextTrack) {
        replacementRef.current = null;
        ownedReplacement.stop();
        return;
      }
      nextTrack.contentHint = "motion";
      nextTrack.addEventListener("ended", () => {
        if (generationRef.current === generation &&
            streamRef.current === replacement) {
          resumeRef.current = false;
          releaseRef.current(true, "The selected camera stopped.");
        }
      }, { once: true });
      await sender.replaceTrack(nextTrack);
      if (replacementGenerationRef.current !== replacementGeneration ||
          generationRef.current !== generation ||
          senderRef.current !== sender ||
          replacementRef.current?.stream !== replacement) {
        if (replacementRef.current?.stream === replacement) {replacementRef.current = null;}
        ownedReplacement.stop();
        return;
      }
      const previous = streamRef.current;
      streamRef.current = replacement;
      statsGenerationRef.current += 1;
      lastStatsRef.current = null;
      stalledStatsSamplesRef.current = 0;
      replacementRef.current = null;
      if (acquiringReplacementGenerationRef.current === replacementGeneration) {acquiringReplacementGenerationRef.current = null;}
      activeStreamEndedRef.current = false;
      setSelectedCameraId(deviceId);
      if (videoRef.current) {videoRef.current.srcObject = replacement; void videoRef.current.play().catch(() => undefined);}
      previous?.getTracks().forEach((track) => track.stop());
      const settings = nextTrack.getSettings();
      setQuality((current) => ({
        ...current,
        ...(settings.width === undefined ? {} : { width: settings.width }),
        ...(settings.height === undefined ? {} : { height: settings.height }),
        ...(settings.frameRate === undefined ? {} : { fps: settings.frameRate })
      }));
      setStatus(activePc.transportMode === "relay" ? "Streaming through Relay" : "Streaming through Enhanced Direct");
    } catch {
      if (acquiringReplacementGenerationRef.current === replacementGeneration) {acquiringReplacementGenerationRef.current = null;}
      if (replacementRef.current?.stream === replacement) {replacementRef.current = null;}
      if (ownedReplacement) {ownedReplacement.stop();}
      else {replacement?.getTracks().forEach((track) => track.stop());}
      if (activeStreamEndedRef.current && replacementGenerationRef.current === replacementGeneration &&
          generationRef.current === generation && senderRef.current === sender) {
        setSelectedCameraId(deviceId);
        releaseLocal(true, "The active camera stopped while switching cameras.");
        return;
      }
      if (!streamRef.current && replacementGenerationRef.current === replacementGeneration &&
          generationRef.current === generation && senderRef.current === sender) {
        releaseLocal(true, "The selected camera could not be opened after releasing the previous camera.");
        return;
      }
      if (replacementGenerationRef.current === replacementGeneration && generationRef.current === generation && senderRef.current === sender) {
        setSelectedCameraId(previousCameraId);
        setStatus("The selected camera could not replace the active camera.");
      }
    }
  }, [activePc.transportMode, phase, releaseLocal, selectedCameraId]);

  useEffect(() => {changeCameraRef.current = changeCamera;}, [changeCamera]);
  useEffect(() => {
    const refreshAfterRotation = () => {
      window.clearTimeout(orientationTimerRef.current);
      orientationTimerRef.current = window.setTimeout(() => {
        const deviceId = streamRef.current?.getVideoTracks()[0]?.getSettings().deviceId;
        if (deviceId && peerRef.current && senderRef.current) {
          void changeCameraRef.current(deviceId, "orientation");
        }
      }, 500);
    };
    window.addEventListener("orientationchange", refreshAfterRotation);
    screen.orientation?.addEventListener("change", refreshAfterRotation);
    return () => {
      window.removeEventListener("orientationchange", refreshAfterRotation);
      screen.orientation?.removeEventListener("change", refreshAfterRotation);
      window.clearTimeout(orientationTimerRef.current);
    };
  }, []);

  useEffect(() => {
    let resumeTimer: number | undefined;
    const pauseForBackground = () => {
      if (acquiringGenerationRef.current !== null || streamRef.current || pendingRef.current || peerRef.current) {
        resumeRef.current = true;
        releaseRef.current(true, "Camera released while Voltura Air is in the background.");
      }
    };
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        pauseForBackground();
      } else if (resumeRef.current && state === "paired") {
        resumeRef.current = false;
        resumeTimer = window.setTimeout(() => startRef.current(), 250);
      }
    };
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("pagehide", pauseForBackground);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pagehide", pauseForBackground);
      window.clearTimeout(resumeTimer);
    };
  }, [releaseLocal, state]);

  useEffect(() => {
    let resumeTimer: number | undefined;
    if (state !== "paired" && (acquiringGenerationRef.current !== null || streamRef.current || pendingRef.current || peerRef.current)) {
      resumeRef.current = true;
      releaseLocal(false, "Waiting for the PC connection…");
    } else if (state === "paired" && document.visibilityState === "visible" && resumeRef.current) {
      resumeRef.current = false;
      resumeTimer = window.setTimeout(() => startRef.current(), 250);
    }
    return () => {window.clearTimeout(resumeTimer);};
  }, [connectionEpoch, releaseLocal, state]);

  useEffect(() => {
    if (capability.canUse) {return;}
    resumeRef.current = false;
    if (acquiringGenerationRef.current !== null || streamRef.current || replacementRef.current || pendingRef.current || peerRef.current) {
      releaseLocal(false, initialStatus(capability));
    } else {
      setStatus(initialStatus(capability));
    }
  }, [capability, releaseLocal]);

  useEffect(() => () => {releaseRef.current(true, "Phone webcam stopped.");}, []);

  const canStart = supportedTransport && permissionGranted && selectedCameraId && capability.canUse && state === "paired" && phase === "idle";
  return (
    <section className={`phone-webcam-workspace${isCameraViewExpanded ? " camera-view-expanded" : ""}`} aria-labelledby="phone-webcam-title">
      <header className="phone-webcam-header">
        <button type="button" className="icon-button" aria-label="Back" onClick={() => {releaseLocal(true, "Phone webcam stopped."); onBack();}}><ArrowLeft aria-hidden="true" /></button>
        <div><p>Video only</p><h1 id="phone-webcam-title">Phone webcam</h1></div>
      </header>

      <div className="phone-webcam-preview" aria-label="Camera view">
        <video ref={videoRef} muted playsInline autoPlay aria-label="Selected phone camera view" />
        <button
          type="button"
          className="phone-webcam-expand"
          aria-label={isCameraViewExpanded ? "Restore camera view" : "Expand camera view"}
          title={isCameraViewExpanded ? "Restore camera view" : "Expand camera view"}
          onClick={() => {setIsCameraViewExpanded((current) => !current);}}
        >
          {isCameraViewExpanded ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
        </button>
        {phase === "idle" && <div className="phone-webcam-placeholder"><Camera aria-hidden="true" /><span>Camera view appears when streaming starts</span></div>}
      </div>

      <div className="phone-webcam-controls">
        {!permissionGranted && <button type="button" className="primary-button" disabled={!supportedTransport || !capability.canUse || state !== "paired"} onClick={() => {void loadCameras();}}>Allow camera access</button>}
        {permissionGranted && <label>Camera<select value={selectedCameraId} disabled={phase === "connecting"} onChange={(event) => {void changeCamera(event.target.value);}}>{cameras.map((camera) => <option key={camera.deviceId} value={camera.deviceId}>{camera.label}</option>)}</select></label>}
        <div className="phone-webcam-actions">
          <button type="button" className="primary-button" disabled={!canStart} onClick={() => {void start();}}><Camera aria-hidden="true" />Start</button>
          <button type="button" disabled={phase === "idle"} onClick={() => {resumeRef.current = false; releaseLocal(true, "Ready to start.");}}><CameraOff aria-hidden="true" />Stop</button>
        </div>
      </div>

      <div className="phone-webcam-status" role="status">
        <strong>{phase === "streaming" ? "Streaming" : phase === "connecting" ? "Connecting" : "Ready"}</strong>
        <span>{supportedTransport ? status : "Phone webcam requires Enhanced Direct or Relay."}</span>
        {quality.width && quality.height && <span>{Math.round(quality.width)}×{Math.round(quality.height)}{quality.fps ? ` at ${quality.fps.toFixed(1)} fps` : ""}{quality.bitrateMbps !== undefined ? `; ${quality.bitrateMbps.toFixed(2)} Mbps` : ""}</span>}
        {supportedTransport && <span>{activePc.transportMode === "relay" ? "Relay · shared usage limits apply" : "Enhanced Direct · free and unlimited"}</span>}
      </div>
    </section>
  );
}

function initialStatus(capability: PhoneWebcamCapability): string {
  if (capability.requiresRepair) {return "Repair Phone webcam on the PC before using it.";}
  if (!capability.enabled) {return "Enable Phone webcam in the Windows app first.";}
  if (!capability.permissionGranted) {return "This paired device is not allowed to use Phone webcam.";}
  return "Allow camera access to choose a camera.";
}

interface OutboundVideoStats {
  bytesSent: number;
  framesEncoded?: number;
  frameWidth?: number;
  frameHeight?: number;
  framesPerSecond?: number;
}

function readOutboundVideoReport(report: RTCStatsReport): OutboundVideoStats | null {
  const rows: OutboundVideoStats[] = [];
  report.forEach((value: unknown) => {
    const row = readOutboundVideoStats(value);
    if (row) {rows.push(row);}
  });
  if (rows.length === 0) {return null;}
  const allExposeFramesEncoded = rows.every((row) => row.framesEncoded !== undefined);
  return {
    bytesSent: rows.reduce((total, row) => total + row.bytesSent, 0),
    ...(allExposeFramesEncoded ? { framesEncoded: rows.reduce((total, row) => total + (row.framesEncoded ?? 0), 0) } : {}),
    ...maximumDefined(rows, "frameWidth"),
    ...maximumDefined(rows, "frameHeight"),
    ...maximumDefined(rows, "framesPerSecond")
  };
}

function maximumDefined<Key extends "frameWidth" | "frameHeight" | "framesPerSecond">(
  rows: OutboundVideoStats[],
  key: Key
): Pick<OutboundVideoStats, Key> | object {
  const values = rows.map((row) => row[key]).filter((value): value is number => value !== undefined);
  return values.length === 0 ? {} : { [key]: Math.max(...values) };
}

function readOutboundVideoStats(value: unknown): OutboundVideoStats | null {
  if (typeof value !== "object" || value === null) {return null;}
  const record = value as Record<string, unknown>;
  if (record.type !== "outbound-rtp" || record.kind !== "video" || record.active === false || typeof record.bytesSent !== "number") {return null;}
  return {
    bytesSent: record.bytesSent,
    ...(typeof record.framesEncoded === "number" ? { framesEncoded: record.framesEncoded } : {}),
    ...(typeof record.frameWidth === "number" ? { frameWidth: record.frameWidth } : {}),
    ...(typeof record.frameHeight === "number" ? { frameHeight: record.frameHeight } : {}),
    ...(typeof record.framesPerSecond === "number" ? { framesPerSecond: record.framesPerSecond } : {})
  };
}
