import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeft, Camera, CameraOff, Maximize2, Mic, MicOff, Minimize2 } from "lucide-react";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { subscribePhoneWebcamResults } from "../../foundation/connection/phoneWebcamResultBus";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type {
  ClientMessage,
  PhoneWebcamCapability,
  PhoneWebcamServerMessage,
} from "../../foundation/protocol/messages";
import { createLocalId } from "../../foundation/identity/localId";
import {
  hashSessionDescription,
  verifyHostSessionSignature,
} from "../../foundation/webrtc/sessionCrypto";
import {
  hasOnlyRelayCandidates,
  IceGatheringTimeoutError,
  waitForIceGathering,
} from "../../foundation/webrtc/iceGathering";
import { runWebRtcSetupStep } from "../../foundation/webrtc/peerSetup";
import "./phone-webcam.css";

interface PhoneWebcamWorkspaceProps {
  activePc: PcProfile;
  capability: PhoneWebcamCapability;
  capabilityConfirmed?: boolean;
  clientId: string;
  connectionEpoch: number;
  onBack: () => void;
  send: (message: ClientMessage) => boolean | void;
  state: ConnectionState;
}

interface CameraChoice {
  deviceId: string;
  label: string;
}
interface PendingStart {
  operationId: string;
  stream: MediaStream;
  useMicrophone: boolean;
}
interface PendingReplacement {
  updatingSender: boolean;
  stop: () => void;
}
interface SendQuality {
  width?: number;
  height?: number;
  fps?: number;
  bitrateMbps?: number | undefined;
}
type CameraChangeReason = "selection" | "orientation" | "stalled";
type Phase = "idle" | "connecting" | "streaming" | "interrupted" | "recovering";

const preferredWidth = 1920;
const preferredHeight = 1080;
const preferredFps = 30;
const stalledStatsSampleLimit = 3;
const stalledRecoveryCooldownMs = 10_000;
const startResponseTimeoutMs = 25_000;
const relayStartResponseTimeoutMs = 35_000;
const peerSetupTimeoutMs = 20_000;
const disconnectedRecoveryMs = 8_000;
const cameraCaptureTimeoutMs = 10_000;
const senderUpdateTimeoutMs = 5_000;
const statsResponseTimeoutMs = 4_000;
const recoveryDelaysMs = [250, 1000, 2500, 5000];
const recoveryOperationHistoryLimit = 16;
const stableConnectionMs = 15_000;
const cameraPauseNoticeMs = 2_000;
const previewFrameDeadlineMs = 2_500;
const previewCheckIntervalMs = 5_000;
const recoverableEndReasons = new Set([
  "connection-lost",
  "transport-lost",
  "decoder-failed",
  "offer-expired",
]);
const recoveryVetoReasons = new Set(["host-stopped", "permission-revoked", "pairing-revoked"]);
const recoverableStartCodes = new Set([
  "busy",
  "offer-expired",
  "turn-unavailable",
  "webrtc-unavailable",
]);

function isEndedTrack(track: MediaStreamTrack) {
  return track.readyState === "ended";
}

function readCameraChoices(devices: MediaDeviceInfo[]): CameraChoice[] {
  const choices = new Map<string, CameraChoice>();
  for (const device of devices) {
    if (device.kind === "videoinput" && device.deviceId && !choices.has(device.deviceId)) {
      choices.set(device.deviceId, {
        deviceId: device.deviceId,
        label: device.label || `Camera ${choices.size + 1}`,
      });
    }
  }
  return Array.from(choices.values());
}

function preferredCameraId(choices: CameraChoice[]) {
  return (
    choices.find((choice) => /front/i.test(choice.label))?.deviceId ?? choices[0]?.deviceId ?? ""
  );
}

function captureValue(actual: number | undefined, fallback: number, limit: number) {
  return actual !== undefined && Number.isFinite(actual) && actual > 0
    ? Math.min(limit, Math.max(1, Math.round(actual)))
    : fallback;
}

function openCameraCapture(signal: AbortSignal, constraints: MediaStreamConstraints) {
  return runWebRtcSetupStep(
    signal,
    () => navigator.mediaDevices.getUserMedia(constraints),
    (stream) => stream.getTracks().forEach((track) => track.stop()),
  );
}

function scheduleCameraDeadline(capture: AbortController) {
  return window.setTimeout(() => {
    capture.abort(new DOMException("The camera did not become available.", "TimeoutError"));
  }, cameraCaptureTimeoutMs);
}

export default function PhoneWebcamWorkspace({
  activePc,
  capability,
  capabilityConfirmed = true,
  clientId,
  connectionEpoch,
  onBack,
  send,
  state,
}: PhoneWebcamWorkspaceProps) {
  const supportedTransport =
    activePc.transportMode === "secure-direct" || activePc.transportMode === "relay";
  const [cameras, setCameras] = useState<CameraChoice[]>([]);
  const [cameraPreferenceId, setSelectedCameraId] = useState("");
  const [permissionGranted, setPermissionGranted] = useState(false);
  const [permissionLoading, setPermissionLoading] = useState(false);
  const [useMicrophone, setUseMicrophone] = useState(false);
  const [microphonePermissionGranted, setMicrophonePermissionGranted] = useState(false);
  const [microphonePermissionLoading, setMicrophonePermissionLoading] = useState(false);
  const [microphoneActive, setMicrophoneActive] = useState(false);
  const [microphoneMuted, setMicrophoneMuted] = useState(false);
  const [phase, setPhase] = useState<Phase>("idle");
  const selectedCameraId =
    phase === "idle" && !cameras.some(({ deviceId }) => deviceId === cameraPreferenceId)
      ? preferredCameraId(cameras)
      : cameraPreferenceId;
  const [isCameraViewExpanded, setIsCameraViewExpanded] = useState(false);
  const [cameraPaused, setCameraPaused] = useState(false);
  const [status, setStatus] = useState(initialStatus(capability));
  const [quality, setQuality] = useState<SendQuality>({});
  const videoRef = useRef<HTMLVideoElement>(null);
  const previewRetryRef = useRef<number | undefined>(undefined);
  const permissionLoadingRef = useRef(false);
  const permissionGenerationRef = useRef(0);
  const permissionCaptureRef = useRef<AbortController | null>(null);
  const permissionStreamRef = useRef<MediaStream | null>(null);
  const microphoneRequestGenerationRef = useRef(0);
  const microphoneCaptureRef = useRef<AbortController | null>(null);
  const microphoneAvailableRef = useRef(capability.microphoneAvailable);
  const streamRef = useRef<MediaStream | null>(null);
  const activeCameraIdRef = useRef<string | null>(null);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const peerSetupRef = useRef<AbortController | null>(null);
  const senderRef = useRef<RTCRtpSender | null>(null);
  const pendingRef = useRef<PendingStart | null>(null);
  const replacementRef = useRef<PendingReplacement | null>(null);
  const replacementGenerationRef = useRef(0);
  const acquiringReplacementGenerationRef = useRef<number | null>(null);
  const replacementCaptureRef = useRef<AbortController | null>(null);
  const activeVideoLossRef = useRef<"ended" | "released" | null>(null);
  const releaseVideoWatchRef = useRef<(() => void) | null>(null);
  const senderUpdateTailRef = useRef<Promise<void>>(Promise.resolve());
  const acquiringGenerationRef = useRef<number | null>(null);
  const initialCaptureRef = useRef<AbortController | null>(null);
  const operationIdRef = useRef<string | null>(null);
  const recoveringOperationIdsRef = useRef(new Set<string>());
  const resumeRef = useRef(false);
  const recoveryAttemptRef = useRef(0);
  const pageHiddenRef = useRef(false);
  const microphoneMutedRef = useRef(false);
  const recoverVideoOnlyRef = useRef(false);
  const lastConnectionRef = useRef({
    pcId: activePc.id,
    hostKey: activePc.hostIdentityPublicKey,
    epoch: connectionEpoch,
  });
  const startRef = useRef<(recovering: boolean) => void>(() => undefined);
  const scheduleResumeRef = useRef<() => void>(() => undefined);
  const renewalTimerRef = useRef<number | undefined>(undefined);
  const statsTimerRef = useRef<number | undefined>(undefined);
  const restartTimerRef = useRef<number | undefined>(undefined);
  const orientationTimerRef = useRef<number | undefined>(undefined);
  const startResponseTimerRef = useRef<number | undefined>(undefined);
  const disconnectedTimerRef = useRef<number | undefined>(undefined);
  const stableTimerRef = useRef<number | undefined>(undefined);
  const lastStatsRef = useRef<{ bytes: number; framesEncoded?: number; at: number } | null>(null);
  const stalledStatsSamplesRef = useRef(0);
  const stalledRecoveryAfterRef = useRef(0);
  const attemptedStallRefreshRef = useRef(false);
  const statsGenerationRef = useRef(0);
  const generationRef = useRef(0);
  const releaseRef = useRef<(notifyHost: boolean, message: string, recovering?: boolean) => void>(
    () => undefined,
  );
  const changeCameraRef = useRef<(deviceId: string, reason?: CameraChangeReason) => Promise<void>>(
    () => Promise.resolve(),
  );

  const releaseLocal = useCallback(
    (notifyHost: boolean, message: string, recovering = false) => {
      resumeRef.current = recovering;
      if (!recovering) {
        recoveryAttemptRef.current = 0;
        recoveringOperationIdsRef.current.clear();
        microphoneMutedRef.current = false;
        recoverVideoOnlyRef.current = false;
      }
      permissionGenerationRef.current += 1;
      microphoneRequestGenerationRef.current += 1;
      permissionLoadingRef.current = false;
      setPermissionLoading(false);
      setMicrophonePermissionLoading(false);
      permissionCaptureRef.current?.abort();
      permissionCaptureRef.current = null;
      microphoneCaptureRef.current?.abort();
      microphoneCaptureRef.current = null;
      permissionStreamRef.current?.getTracks().forEach((track) => track.stop());
      permissionStreamRef.current = null;
      generationRef.current += 1;
      replacementGenerationRef.current += 1;
      window.clearTimeout(renewalTimerRef.current);
      window.clearInterval(statsTimerRef.current);
      window.clearTimeout(restartTimerRef.current);
      window.clearTimeout(orientationTimerRef.current);
      window.clearTimeout(startResponseTimerRef.current);
      window.clearTimeout(disconnectedTimerRef.current);
      window.clearTimeout(stableTimerRef.current);
      window.clearTimeout(previewRetryRef.current);
      renewalTimerRef.current = undefined;
      statsTimerRef.current = undefined;
      restartTimerRef.current = undefined;
      disconnectedTimerRef.current = undefined;
      stableTimerRef.current = undefined;
      previewRetryRef.current = undefined;
      lastStatsRef.current = null;
      stalledStatsSamplesRef.current = 0;
      stalledRecoveryAfterRef.current = 0;
      attemptedStallRefreshRef.current = false;
      statsGenerationRef.current += 1;
      acquiringGenerationRef.current = null;
      acquiringReplacementGenerationRef.current = null;
      initialCaptureRef.current?.abort();
      initialCaptureRef.current = null;
      replacementCaptureRef.current?.abort();
      replacementCaptureRef.current = null;
      const videoWasReleased = activeVideoLossRef.current === "released";
      activeVideoLossRef.current = null;
      releaseVideoWatchRef.current?.();
      releaseVideoWatchRef.current = null;
      senderUpdateTailRef.current = Promise.resolve();
      const operationId = operationIdRef.current;
      if (recovering && operationId) {
        const history = recoveringOperationIdsRef.current;
        history.add(operationId);
        if (history.size > recoveryOperationHistoryLimit) {
          const oldest = history.values().next().value;
          if (oldest !== undefined) {
            history.delete(oldest);
          }
        }
      }
      operationIdRef.current = null;
      pendingRef.current = null;
      senderRef.current = null;
      const retiredPeer = peerRef.current;
      const retiredSetup = peerSetupRef.current;
      peerRef.current = null;
      peerSetupRef.current = null;
      retiredSetup?.abort();
      retiredPeer?.close();
      const stream = streamRef.current;
      streamRef.current = null;
      activeCameraIdRef.current = null;
      const ownedTracks = videoWasReleased ? stream?.getAudioTracks() : stream?.getTracks();
      for (const track of ownedTracks ?? []) {
        track.stop();
      }
      const replacement = replacementRef.current;
      replacementRef.current = null;
      replacement?.stop();
      if (videoRef.current) {
        videoRef.current.srcObject = null;
      }
      setPhase(recovering ? "recovering" : "idle");
      setCameraPaused(false);
      setMicrophoneActive(false);
      setMicrophoneMuted(false);
      setQuality({});
      setStatus(message);
      if (notifyHost && operationId && state === "paired") {
        try {
          send({ type: "phone.webcam.stop", operationId: createLocalId() });
        } catch {
          // Closing the peer has already stopped local capture.
        }
      }
    },
    [send, state],
  );
  useEffect(() => {
    releaseRef.current = releaseLocal;
  }, [releaseLocal]);

  const recoverSession = useCallback((notifyHost: boolean, message: string) => {
    const videoTrack = streamRef.current?.getVideoTracks()[0];
    if (videoTrack?.muted && !isEndedTrack(videoTrack) && document.visibilityState === "visible") {
      releaseRef.current(notifyHost, "The browser paused your camera. Tap Start to resume.");
      return;
    }
    if (recoveryAttemptRef.current >= recoveryDelaysMs.length) {
      releaseRef.current(notifyHost, `${message} Tap Start to try again.`);
      return;
    }
    releaseRef.current(notifyHost, message, true);
    scheduleResumeRef.current();
  }, []);

  const recoverWithoutMicrophone = useCallback(
    (notifyHost: boolean, message: string) => {
      recoverVideoOnlyRef.current = true;
      setUseMicrophone(false);
      setMicrophonePermissionGranted(false);
      recoverSession(notifyHost, message);
    },
    [recoverSession],
  );

  const requestPreviewPlayback = useCallback(() => {
    const video = videoRef.current;
    const stream = streamRef.current;
    if (
      !video ||
      !stream ||
      video.srcObject !== stream ||
      pageHiddenRef.current ||
      document.visibilityState !== "visible"
    ) {
      return;
    }
    try {
      void video.play().catch(() => undefined);
    } catch {
      // The preview is independent of the video sender.
    }
  }, []);

  const resumePausedPreview = useCallback(() => {
    if (previewRetryRef.current !== undefined) {
      return;
    }
    const stream = streamRef.current;
    if (!stream) {
      return;
    }
    previewRetryRef.current = window.setTimeout(() => {
      previewRetryRef.current = undefined;
      if (streamRef.current === stream && videoRef.current?.paused) {
        requestPreviewPlayback();
      }
    }, 250);
  }, [requestPreviewPlayback]);

  const watchActiveVideo = useCallback(
    (stream: MediaStream, generation: number) => {
      releaseVideoWatchRef.current?.();
      setCameraPaused(false);
      const track = stream.getVideoTracks()[0];
      if (!track) {
        return;
      }
      const watch = new AbortController();
      let muteTimer: number | undefined;
      const ownsTrack = () => generationRef.current === generation && streamRef.current === stream;
      const onEnded = () => {
        if (!ownsTrack()) {
          return;
        }
        if (acquiringReplacementGenerationRef.current !== null) {
          activeVideoLossRef.current ??= "ended";
          return;
        }
        recoverSession(true, "The selected camera stopped.");
      };
      const clearMuteTimer = () => {
        window.clearTimeout(muteTimer);
        muteTimer = undefined;
      };
      const onMute = () => {
        if (muteTimer !== undefined || !ownsTrack()) {
          return;
        }
        muteTimer = window.setTimeout(() => {
          muteTimer = undefined;
          if (ownsTrack() && track.muted) {
            setCameraPaused(true);
          }
        }, cameraPauseNoticeMs);
      };
      const onUnmute = () => {
        clearMuteTimer();
        if (ownsTrack()) {
          setCameraPaused(false);
          requestPreviewPlayback();
        }
      };
      releaseVideoWatchRef.current = () => {
        watch.abort();
        clearMuteTimer();
      };
      track.addEventListener("ended", onEnded, { once: true, signal: watch.signal });
      track.addEventListener("mute", onMute, { signal: watch.signal });
      track.addEventListener("unmute", onUnmute, { signal: watch.signal });
      if (track.muted) {
        onMute();
      }
    },
    [recoverSession, requestPreviewPlayback],
  );

  useEffect(() => {
    const video = videoRef.current;
    if (
      phase !== "streaming" ||
      !video?.requestVideoFrameCallback ||
      !video.cancelVideoFrameCallback
    ) {
      return;
    }
    let callback: number | undefined;
    let deadline: number | undefined;
    const clearProbe = () => {
      if (callback !== undefined) {
        video.cancelVideoFrameCallback(callback);
        callback = undefined;
      }
      window.clearTimeout(deadline);
    };
    const canProbe = (stream: MediaStream) =>
      streamRef.current === stream &&
      video.srcObject === stream &&
      !stream.getVideoTracks()[0]?.muted &&
      !pageHiddenRef.current &&
      document.visibilityState === "visible";
    const probe = () => {
      const stream = streamRef.current;
      if (!stream || !canProbe(stream)) {
        return;
      }
      callback = video.requestVideoFrameCallback(() => {
        callback = undefined;
        window.clearTimeout(deadline);
      });
      deadline = window.setTimeout(() => {
        clearProbe();
        if (canProbe(stream)) {
          // Reattach the preview when its video layer stalls without replacing the sender track.
          video.srcObject = null;
          video.srcObject = stream;
          requestPreviewPlayback();
        }
      }, previewFrameDeadlineMs);
    };
    const interval = window.setInterval(probe, previewCheckIntervalMs);
    return () => {
      window.clearInterval(interval);
      clearProbe();
    };
  }, [phase, requestPreviewPlayback]);

  const loadCameras = useCallback(async () => {
    if (permissionLoadingRef.current) {
      return;
    }
    if (!navigator.mediaDevices?.getUserMedia || !navigator.mediaDevices.enumerateDevices) {
      setStatus("This browser does not provide camera capture.");
      return;
    }
    let permissionStream: MediaStream | null = null;
    const generation = ++permissionGenerationRef.current;
    const capture = new AbortController();
    permissionCaptureRef.current = capture;
    const deadline = scheduleCameraDeadline(capture);
    permissionLoadingRef.current = true;
    setPermissionLoading(true);
    try {
      permissionStream = await openCameraCapture(capture.signal, { audio: false, video: true });
      if (permissionGenerationRef.current !== generation) {
        permissionStream.getTracks().forEach((track) => track.stop());
        return;
      }
      permissionStreamRef.current = permissionStream;
      if (document.visibilityState !== "visible") {
        setStatus("Camera access is paused while Voltura Air is in the background.");
        return;
      }
      const devices = await runWebRtcSetupStep(capture.signal, () =>
        navigator.mediaDevices.enumerateDevices(),
      );
      if (
        permissionGenerationRef.current !== generation ||
        document.visibilityState !== "visible"
      ) {
        return;
      }
      const choices = readCameraChoices(devices);
      setCameras(choices);
      setSelectedCameraId((current) =>
        current && choices.some((choice) => choice.deviceId === current)
          ? current
          : preferredCameraId(choices),
      );
      setPermissionGranted(choices.length > 0);
      setStatus(
        choices.length > 0 ? "Choose a camera, then start webcam." : "No camera is available.",
      );
    } catch (error) {
      if (permissionGenerationRef.current !== generation) {
        return;
      }
      setStatus(
        error instanceof DOMException && error.name === "NotAllowedError"
          ? "Camera access was not allowed."
          : "The phone camera could not be opened.",
      );
    } finally {
      window.clearTimeout(deadline);
      if (permissionStreamRef.current === permissionStream) {
        permissionStream?.getTracks().forEach((track) => track.stop());
        permissionStreamRef.current = null;
      }
      if (permissionCaptureRef.current === capture) {
        permissionCaptureRef.current = null;
      }
      if (permissionGenerationRef.current === generation) {
        permissionLoadingRef.current = false;
        setPermissionLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const devices = navigator.mediaDevices;
    if (!permissionGranted || !devices?.addEventListener) {
      return;
    }
    let refresh: AbortController | undefined;
    const updateChoices = () => {
      if (pageHiddenRef.current || document.visibilityState !== "visible") {
        return;
      }
      refresh?.abort();
      const next = new AbortController();
      refresh = next;
      const deadline = scheduleCameraDeadline(next);
      void runWebRtcSetupStep(next.signal, () => devices.enumerateDevices())
        .then((current) => {
          if (
            refresh === next &&
            !next.signal.aborted &&
            !pageHiddenRef.current &&
            document.visibilityState === "visible"
          ) {
            setCameras(readCameraChoices(current));
          }
        })
        .catch(() => undefined)
        .finally(() => window.clearTimeout(deadline));
    };
    devices.addEventListener("devicechange", updateChoices);
    return () => {
      refresh?.abort();
      devices.removeEventListener("devicechange", updateChoices);
    };
  }, [permissionGranted]);

  const requestMicrophone = useCallback(async (enabled: boolean) => {
    const requestGeneration = ++microphoneRequestGenerationRef.current;
    microphoneCaptureRef.current?.abort();
    microphoneCaptureRef.current = null;
    if (!enabled) {
      setMicrophonePermissionLoading(false);
      setUseMicrophone(false);
      setMicrophonePermissionGranted(false);
      return;
    }
    const capture = new AbortController();
    microphoneCaptureRef.current = capture;
    const deadline = scheduleCameraDeadline(capture);
    setMicrophonePermissionLoading(true);
    let stream: MediaStream | null = null;
    try {
      stream = await openCameraCapture(capture.signal, { audio: true, video: false });
      if (!stream.getAudioTracks()[0] || stream.getAudioTracks()[0]?.readyState === "ended") {
        throw new Error("No audio track.");
      }
      if (
        microphoneRequestGenerationRef.current !== requestGeneration ||
        !microphoneAvailableRef.current
      ) {
        return;
      }
      setMicrophonePermissionGranted(true);
      setUseMicrophone(true);
      setStatus("Phone microphone is ready. Start when you are ready.");
    } catch {
      if (microphoneRequestGenerationRef.current !== requestGeneration) {
        return;
      }
      setMicrophonePermissionGranted(false);
      setUseMicrophone(false);
      setStatus("Microphone access was not allowed or no microphone is available.");
    } finally {
      window.clearTimeout(deadline);
      stream?.getTracks().forEach((track) => track.stop());
      if (microphoneCaptureRef.current === capture) {
        microphoneCaptureRef.current = null;
      }
      if (microphoneRequestGenerationRef.current === requestGeneration) {
        setMicrophonePermissionLoading(false);
      }
    }
  }, []);

  const openSelectedCamera = useCallback(
    async (
      includeMicrophone: boolean,
      generation: number,
      signal: AbortSignal,
    ): Promise<{
      stream: MediaStream;
      settings: MediaTrackSettings;
      useMicrophone: boolean;
      microphoneFallback: boolean;
    }> => {
      const constraints: MediaStreamConstraints = {
        audio: includeMicrophone,
        video: {
          ...(selectedCameraId ? { deviceId: { exact: selectedCameraId } } : {}),
          width: { ideal: preferredWidth, max: preferredWidth },
          height: { ideal: preferredHeight, max: preferredHeight },
          frameRate: { ideal: preferredFps, max: preferredFps },
        },
      };
      let stream: MediaStream | undefined;
      let microphoneIncluded = includeMicrophone;
      let microphoneFallback = false;
      try {
        stream = await openCameraCapture(signal, constraints);
        if (
          includeMicrophone &&
          (!stream.getAudioTracks()[0] || stream.getAudioTracks()[0]?.readyState === "ended")
        ) {
          throw new Error("No audio track.");
        }
      } catch (error) {
        if (!includeMicrophone || signal.aborted) {
          stream?.getTracks().forEach((track) => track.stop());
          throw error;
        }
        stream?.getTracks().forEach((track) => track.stop());
        if (generationRef.current !== generation) {
          throw error;
        }
        stream = await openCameraCapture(signal, { ...constraints, audio: false });
        microphoneIncluded = false;
        microphoneFallback = true;
        if (generationRef.current === generation) {
          setUseMicrophone(false);
          setMicrophonePermissionGranted(false);
        }
      }
      if (!stream) {
        throw new Error("Camera stream unavailable.");
      }
      try {
        const track = stream.getVideoTracks()[0];
        if (!track || track.readyState === "ended") {
          throw new Error("No live video track.");
        }
        track.contentHint = "motion";
        return {
          stream,
          settings: track.getSettings(),
          useMicrophone: microphoneIncluded,
          microphoneFallback,
        };
      } catch (error) {
        stream.getTracks().forEach((track) => track.stop());
        throw error;
      }
    },
    [selectedCameraId],
  );

  const start = useCallback(
    async (recovering = false) => {
      if (
        !supportedTransport ||
        phase !== (recovering ? "recovering" : "idle") ||
        (recovering && !resumeRef.current) ||
        acquiringGenerationRef.current !== null ||
        microphoneCaptureRef.current !== null ||
        operationIdRef.current !== null ||
        state !== "paired" ||
        pageHiddenRef.current ||
        document.visibilityState !== "visible" ||
        !capability.canUse ||
        !capabilityConfirmed ||
        !selectedCameraId ||
        !activePc.hostIdentityPublicKey
      ) {
        return;
      }
      if (typeof RTCPeerConnection === "undefined") {
        releaseLocal(false, "This browser does not provide WebRTC video.");
        return;
      }
      if (recovering) {
        resumeRef.current = false;
        recoveryAttemptRef.current += 1;
      }
      setSelectedCameraId(selectedCameraId);
      const generation = ++generationRef.current;
      acquiringGenerationRef.current = generation;
      const capture = new AbortController();
      initialCaptureRef.current = capture;
      const captureDeadline = scheduleCameraDeadline(capture);
      setPhase("connecting");
      setStatus("Opening the selected camera…");
      try {
        const opened = await openSelectedCamera(
          useMicrophone &&
            microphonePermissionGranted &&
            capability.microphoneAvailable &&
            !recoverVideoOnlyRef.current,
          generation,
          capture.signal,
        );
        if (generationRef.current !== generation || acquiringGenerationRef.current !== generation) {
          opened.stream.getTracks().forEach((track) => track.stop());
          return;
        }
        acquiringGenerationRef.current = null;
        streamRef.current = opened.stream;
        activeCameraIdRef.current = selectedCameraId;
        const width = captureValue(opened.settings.width, preferredWidth, 4096);
        const height = captureValue(opened.settings.height, preferredHeight, 4096);
        const fps = captureValue(opened.settings.frameRate, preferredFps, 60);
        const operationId = createLocalId();
        const transcript = `VolturaAir phone-webcam:start:v2:${clientId}:${operationId}:${width}:${height}:${fps}:${String(opened.useMicrophone)}`;
        const signature = signClientPayload(clientId, activePc.id, transcript);
        if (!signature) {
          releaseLocal(false, "The PC reconnect key is unavailable.");
          return;
        }
        setMicrophoneActive(opened.useMicrophone);
        watchActiveVideo(opened.stream, generation);
        const audioTrack = opened.stream.getAudioTracks()[0];
        if (audioTrack) {
          audioTrack.enabled = !microphoneMutedRef.current;
          setMicrophoneMuted(microphoneMutedRef.current);
        }
        audioTrack?.addEventListener(
          "ended",
          () => {
            if (
              generationRef.current === generation &&
              streamRef.current?.getAudioTracks()[0] === audioTrack
            ) {
              recoverWithoutMicrophone(true, "The phone microphone stopped. Restoring video…");
            }
          },
          { once: true },
        );
        pendingRef.current = {
          operationId,
          stream: opened.stream,
          useMicrophone: opened.useMicrophone,
        };
        operationIdRef.current = operationId;
        if (videoRef.current) {
          videoRef.current.srcObject = opened.stream;
          requestPreviewPlayback();
          if (generationRef.current !== generation || streamRef.current !== opened.stream) {
            if (streamRef.current === opened.stream) {
              opened.stream.getTracks().forEach((track) => track.stop());
              streamRef.current = null;
            }
            return;
          }
        }
        setQuality({ width, height, fps });
        setStatus(
          opened.microphoneFallback
            ? "The microphone could not be opened. Starting encrypted video only…"
            : opened.useMicrophone
              ? "Preparing encrypted webcam video and audio…"
              : "Preparing encrypted webcam video…",
        );
        const sent = send({
          type: "phone.webcam.start",
          operationId,
          captureWidth: width,
          captureHeight: height,
          captureFps: fps,
          useMicrophone: opened.useMicrophone,
          clientSignature: signature,
        });
        if (sent === false) {
          recoverSession(false, "The webcam request could not be sent.");
          return;
        }
        window.clearTimeout(startResponseTimerRef.current);
        startResponseTimerRef.current = window.setTimeout(
          () => {
            if (
              operationIdRef.current === operationId &&
              pendingRef.current?.operationId === operationId
            ) {
              recoverSession(true, "The PC did not respond to the webcam request.");
            }
          },
          activePc.transportMode === "relay" ? relayStartResponseTimeoutMs : startResponseTimeoutMs,
        );
      } catch (error) {
        if (generationRef.current === generation) {
          const cameraFailure = error instanceof DOMException ? error.name : undefined;
          const denied = cameraFailure === "NotAllowedError" || cameraFailure === "SecurityError";
          const message = denied
            ? "Camera access was not allowed."
            : cameraFailure === "TimeoutError"
              ? "The camera did not start in time. Tap Start to try again."
              : "The selected camera could not be started.";
          if (denied) {
            setPermissionGranted(false);
          }
          if (recovering && !denied && cameraFailure !== "TimeoutError") {
            recoverSession(true, message);
          } else {
            releaseLocal(true, message);
          }
        }
      } finally {
        window.clearTimeout(captureDeadline);
        if (initialCaptureRef.current === capture) {
          initialCaptureRef.current = null;
        }
      }
    },
    [
      activePc.hostIdentityPublicKey,
      activePc.id,
      activePc.transportMode,
      capability.canUse,
      capabilityConfirmed,
      capability.microphoneAvailable,
      clientId,
      microphonePermissionGranted,
      openSelectedCamera,
      phase,
      recoverSession,
      recoverWithoutMicrophone,
      requestPreviewPlayback,
      releaseLocal,
      selectedCameraId,
      send,
      state,
      supportedTransport,
      useMicrophone,
      watchActiveVideo,
    ],
  );
  useEffect(() => {
    startRef.current = (recovering) => {
      void start(recovering);
    };
  }, [start]);

  const scheduleResume = useCallback(() => {
    if (
      !resumeRef.current ||
      state !== "paired" ||
      !capability.canUse ||
      !capabilityConfirmed ||
      !supportedTransport ||
      !selectedCameraId ||
      !activePc.hostIdentityPublicKey ||
      pageHiddenRef.current ||
      document.visibilityState !== "visible" ||
      restartTimerRef.current !== undefined
    ) {
      return;
    }
    restartTimerRef.current = window.setTimeout(() => {
      restartTimerRef.current = undefined;
      startRef.current(true);
    }, recoveryDelaysMs[recoveryAttemptRef.current]);
  }, [
    activePc.hostIdentityPublicKey,
    capability.canUse,
    capabilityConfirmed,
    selectedCameraId,
    state,
    supportedTransport,
  ]);

  useEffect(() => {
    scheduleResumeRef.current = scheduleResume;
    scheduleResume();
  }, [scheduleResume]);

  const beginStats = useCallback(
    (peer: RTCPeerConnection, signal: AbortSignal) => {
      window.clearInterval(statsTimerRef.current);
      statsGenerationRef.current += 1;
      lastStatsRef.current = null;
      stalledStatsSamplesRef.current = 0;
      let pollPending = false;
      statsTimerRef.current = window.setInterval(() => {
        void (async () => {
          if (pollPending || peerRef.current !== peer || peer.connectionState !== "connected") {
            return;
          }
          const sender = senderRef.current;
          const track = sender?.track ?? null;
          if (track?.muted) {
            lastStatsRef.current = null;
            stalledStatsSamplesRef.current = 0;
            return;
          }
          const statsGeneration = statsGenerationRef.current;
          const ownsStats = () =>
            peerRef.current === peer &&
            peer.connectionState === "connected" &&
            senderRef.current === sender &&
            sender?.track === track &&
            !track?.muted &&
            statsGenerationRef.current === statsGeneration;
          pollPending = true;
          let expired = false;
          const deadline = window.setTimeout(() => {
            expired = true;
            if (ownsStats()) {
              lastStatsRef.current = null;
              stalledStatsSamplesRef.current = 0;
              setQuality(({ width, height }) =>
                width === undefined || height === undefined ? {} : { width, height },
              );
            }
          }, statsResponseTimeoutMs);
          let report: RTCStatsReport;
          try {
            report = await runWebRtcSetupStep(signal, () => peer.getStats(track));
          } catch {
            // Optional statistics must not interrupt the stream or future polls.
            return;
          } finally {
            window.clearTimeout(deadline);
            pollPending = false;
          }
          if (expired || !ownsStats()) {
            return;
          }
          const outbound = readOutboundVideoReport(report);
          if (outbound) {
            const now = performance.now();
            const previous = lastStatsRef.current;
            const frames = outbound.framesEncoded;
            const previousFrames = previous?.framesEncoded;
            const countersReset =
              previous !== null &&
              (outbound.bytesSent < previous.bytes ||
                (frames !== undefined && previousFrames !== undefined && frames < previousFrames));
            const bitrateMbps =
              previous && !countersReset && now > previous.at
                ? ((outbound.bytesSent - previous.bytes) * 8) / ((now - previous.at) * 1000)
                : undefined;
            const videoAdvanced =
              previous !== null &&
              !countersReset &&
              outbound.bytesSent > previous.bytes &&
              (frames === undefined || previousFrames === undefined || frames > previousFrames);
            if (videoAdvanced) {
              attemptedStallRefreshRef.current = false;
            }
            const progressed = !previous || countersReset || videoAdvanced;
            stalledStatsSamplesRef.current = progressed ? 0 : stalledStatsSamplesRef.current + 1;
            lastStatsRef.current = {
              bytes: outbound.bytesSent,
              ...(frames === undefined ? {} : { framesEncoded: frames }),
              at: now,
            };
            setQuality((current) => ({
              ...current,
              ...(outbound.frameWidth === undefined ? {} : { width: outbound.frameWidth }),
              ...(outbound.frameHeight === undefined ? {} : { height: outbound.frameHeight }),
              ...(outbound.framesPerSecond === undefined ? {} : { fps: outbound.framesPerSecond }),
              bitrateMbps,
            }));
            if (
              stalledStatsSamplesRef.current >= stalledStatsSampleLimit &&
              now >= stalledRecoveryAfterRef.current &&
              acquiringReplacementGenerationRef.current === null
            ) {
              const deviceId = activeCameraIdRef.current;
              if (!deviceId || attemptedStallRefreshRef.current) {
                recoverSession(true, "Webcam video stopped advancing.");
              } else {
                attemptedStallRefreshRef.current = true;
                stalledStatsSamplesRef.current = 0;
                stalledRecoveryAfterRef.current = now + stalledRecoveryCooldownMs;
                void changeCameraRef.current(deviceId, "stalled");
              }
            }
          }
        })();
      }, 1000);
    },
    [recoverSession],
  );

  const scheduleCredentialRenewal = useCallback(
    (expiresAt?: string | null, relayCheckedAt?: string | null) => {
      window.clearTimeout(renewalTimerRef.current);
      if (!expiresAt || activePc.transportMode !== "relay") {
        return;
      }
      const expiry = Date.parse(expiresAt);
      if (!Number.isFinite(expiry)) {
        return;
      }
      const relayTime = relayCheckedAt ? Date.parse(relayCheckedAt) : Number.NaN;
      const relayLifetime = expiry - relayTime;
      const remaining =
        relayLifetime > 0 && relayLifetime <= 24 * 60 * 60_000
          ? relayLifetime
          : expiry - Date.now();
      const delay = remaining - 60_000;
      renewalTimerRef.current = window.setTimeout(
        () => {
          recoverSession(true, "Refreshing Relay credentials…");
        },
        Math.max(5_000, delay),
      );
    },
    [activePc.transportMode, recoverSession],
  );

  const acceptOffer = useCallback(
    async (message: Extract<PhoneWebcamServerMessage, { type: "phone.webcam.start.result" }>) => {
      const pending = pendingRef.current;
      if (pending?.operationId !== message.operationId || peerRef.current) {
        return;
      }
      window.clearTimeout(startResponseTimerRef.current);
      startResponseTimerRef.current = undefined;
      const generation = generationRef.current;
      const isCurrent = () =>
        generationRef.current === generation && operationIdRef.current === message.operationId;
      if (!message.succeeded) {
        if (message.code === "microphone-unavailable" && pending.useMicrophone) {
          recoverWithoutMicrophone(false, message.message);
        } else if (
          message.code &&
          recoverableStartCodes.has(message.code) &&
          (message.code !== "busy" || recoveryAttemptRef.current > 0)
        ) {
          recoverSession(false, message.message);
        } else {
          releaseLocal(false, message.message);
        }
        return;
      }
      if (!message.offerSdp || !message.hostSignature || !activePc.hostIdentityPublicKey) {
        releaseLocal(true, message.message);
        return;
      }
      if (!hasExpectedPhoneWebcamMedia(message.offerSdp, pending.useMicrophone, "recvonly")) {
        releaseLocal(true, "The PC offered unexpected Phone webcam media.");
        return;
      }
      const offerSdp = message.offerSdp;
      const offerHash = hashSessionDescription(offerSdp);
      const hostTranscript = `VolturaAir phone-webcam:offer:v2:${clientId}:${message.operationId}:${offerHash}`;
      if (
        !verifyHostSessionSignature(
          activePc.hostIdentityPublicKey,
          message.hostSignature,
          hostTranscript,
        )
      ) {
        releaseLocal(true, "The PC identity signature was invalid. Camera video was stopped.");
        return;
      }
      const relayMode = activePc.transportMode === "relay";
      if (relayMode && (!message.iceServers || message.iceServers.length === 0)) {
        recoverSession(true, "Relay credentials are temporarily unavailable.");
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
      } catch {
        releaseLocal(true, "This browser could not create the encrypted webcam connection.");
        return;
      }
      peerRef.current = peer;
      const setup = new AbortController();
      peerSetupRef.current = setup;
      startResponseTimerRef.current = window.setTimeout(() => {
        if (isCurrent() && peerRef.current === peer) {
          recoverSession(true, "The encrypted webcam connection timed out.");
        }
      }, peerSetupTimeoutMs);
      let negotiationStage = "applying the PC media offer";
      let answerSent = false;
      const onConnectionStateChange = () => {
        if (peerRef.current !== peer) {
          return;
        }
        if (peer.connectionState === "connected") {
          if (!answerSent) {
            return;
          }
          window.clearTimeout(startResponseTimerRef.current);
          startResponseTimerRef.current = undefined;
          window.clearTimeout(disconnectedTimerRef.current);
          disconnectedTimerRef.current = undefined;
          setPhase("streaming");
          setStatus(relayMode ? "Streaming through Relay" : "Streaming through Enhanced Direct");
          if (statsTimerRef.current === undefined) {
            beginStats(peer, setup.signal);
          }
          if (recoveryAttemptRef.current > 0 && stableTimerRef.current === undefined) {
            stableTimerRef.current = window.setTimeout(() => {
              stableTimerRef.current = undefined;
              recoveryAttemptRef.current = 0;
            }, stableConnectionMs);
          }
        } else if (peer.connectionState === "disconnected") {
          window.clearTimeout(stableTimerRef.current);
          stableTimerRef.current = undefined;
          if (disconnectedTimerRef.current !== undefined) {
            return;
          }
          statsGenerationRef.current += 1;
          lastStatsRef.current = null;
          stalledStatsSamplesRef.current = 0;
          setPhase("interrupted");
          setStatus("Webcam video interrupted. Reconnecting…");
          disconnectedTimerRef.current = window.setTimeout(() => {
            disconnectedTimerRef.current = undefined;
            if (peerRef.current === peer && peer.connectionState !== "connected") {
              recoverSession(true, "Webcam video connection was lost.");
            }
          }, disconnectedRecoveryMs);
        } else if (peer.connectionState === "failed" || peer.connectionState === "closed") {
          recoverSession(true, "Webcam video connection was lost.");
        }
      };
      peer.addEventListener("connectionstatechange", onConnectionStateChange);
      try {
        await runWebRtcSetupStep(setup.signal, () =>
          peer.setRemoteDescription({ type: "offer", sdp: offerSdp }),
        );
        if (!isCurrent()) {
          return;
        }
        negotiationStage = "attaching the camera";
        const transceiver = peer
          .getTransceivers()
          .find((candidate) => candidate.receiver.track.kind === "video");
        if (!transceiver) {
          throw new Error("Missing video transceiver.");
        }
        transceiver.direction = "sendonly";
        const track = pending.stream.getVideoTracks()[0];
        if (!track || isEndedTrack(track)) {
          throw new Error("Missing camera track.");
        }
        await runWebRtcSetupStep(setup.signal, () => transceiver.sender.replaceTrack(track));
        if (!isCurrent()) {
          return;
        }
        if (pending.useMicrophone) {
          negotiationStage = "attaching the microphone";
          const audioTransceiver = peer
            .getTransceivers()
            .find((candidate) => candidate.receiver.track.kind === "audio");
          const audioTrack = pending.stream.getAudioTracks()[0];
          if (!audioTransceiver || !audioTrack) {
            throw new Error("Missing audio transceiver or microphone track.");
          }
          const audioSender = peer.addTrack(audioTrack, pending.stream);
          if (audioSender !== audioTransceiver.sender) {
            throw new Error("The microphone did not reuse the offered audio section.");
          }
        }
        negotiationStage = "creating the encrypted media answer";
        const answer = await runWebRtcSetupStep(setup.signal, () => peer.createAnswer());
        if (!isCurrent()) {
          return;
        }
        await runWebRtcSetupStep(setup.signal, () => peer.setLocalDescription(answer));
        if (!isCurrent()) {
          return;
        }
        negotiationStage = "configuring the camera stream";
        senderRef.current = transceiver.sender;
        const parameters = transceiver.sender.getParameters();
        const encoding = parameters.encodings[0];
        if (!encoding) {
          throw new Error("Missing sender encoding.");
        }
        encoding.maxBitrate = message.maximumBitrate ?? 12_000_000;
        encoding.maxFramerate = preferredFps;
        parameters.degradationPreference = "maintain-resolution";
        await runWebRtcSetupStep(setup.signal, () => transceiver.sender.setParameters(parameters));
        if (!isCurrent()) {
          return;
        }
        negotiationStage = "gathering the connection route";
        await waitForIceGathering(peer, relayMode, setup.signal);
        if (!isCurrent()) {
          return;
        }
        negotiationStage = "validating the media answer";
        const answerSdp = peer.localDescription?.sdp;
        if (
          !answerSdp ||
          answerSdp.length > 32 * 1024 ||
          !hasExpectedPhoneWebcamMedia(answerSdp, pending.useMicrophone, "sendonly")
        ) {
          throw new Error("Invalid Phone webcam answer.");
        }
        if (relayMode && !hasOnlyRelayCandidates(answerSdp)) {
          if (!/^a=candidate:/mu.test(answerSdp)) {
            recoverSession(true, "The browser could not find a Relay route for webcam video.");
            return;
          }
          throw new Error("Relay-only candidates required.");
        }
        const answerHash = hashSessionDescription(answerSdp);
        const answerTranscript = `VolturaAir phone-webcam:answer:v2:${clientId}:${message.operationId}:${offerHash}:${answerHash}`;
        const signature = signClientPayload(clientId, activePc.id, answerTranscript);
        if (!signature) {
          throw new Error("Reconnect key unavailable.");
        }
        const sent = send({
          type: "phone.webcam.answer",
          operationId: message.operationId,
          answerSdp,
          clientSignature: signature,
        });
        if (sent === false) {
          recoverSession(true, "The webcam answer could not be sent.");
          return;
        }
        if (!isCurrent()) {
          return;
        }
        answerSent = true;
        pendingRef.current = null;
        scheduleCredentialRenewal(message.turnExpiresAt, message.relayUsageCheckedAt);
        setStatus(
          pending.useMicrophone
            ? "Connecting encrypted webcam video and audio…"
            : "Connecting encrypted webcam video…",
        );
        if (peer.connectionState === "connected") {
          onConnectionStateChange();
        }
      } catch (error) {
        if (isCurrent()) {
          if (error instanceof IceGatheringTimeoutError) {
            recoverSession(true, "The browser timed out finding a route for webcam video.");
          } else {
            releaseLocal(true, describeNegotiationFailure(negotiationStage, error));
          }
        }
      }
    },
    [
      activePc.hostIdentityPublicKey,
      activePc.id,
      activePc.transportMode,
      beginStats,
      clientId,
      recoverSession,
      recoverWithoutMicrophone,
      releaseLocal,
      scheduleCredentialRenewal,
      send,
    ],
  );

  const handleResult = useCallback(
    (message: PhoneWebcamServerMessage) => {
      if (message.type === "phone.webcam.start.result") {
        void acceptOffer(message);
        return;
      }
      if (
        message.type === "phone.webcam.answer.result" &&
        message.operationId === operationIdRef.current
      ) {
        if (!message.succeeded) {
          if (
            message.code === "microphone-unavailable" &&
            streamRef.current?.getAudioTracks().length
          ) {
            recoverWithoutMicrophone(true, message.message);
          } else if (message.code === "offer-expired") {
            recoverSession(true, message.message);
          } else {
            releaseLocal(true, message.message);
          }
        }
        return;
      }
      if (message.type === "phone.webcam.ended") {
        if (message.operationId !== operationIdRef.current) {
          if (
            recoveryVetoReasons.has(message.reason) &&
            recoveringOperationIdsRef.current.has(message.operationId)
          ) {
            releaseLocal(true, message.message);
          }
          return;
        }
        if (message.reason === "audio-failed") {
          recoverWithoutMicrophone(false, "The PC microphone output stopped. Restoring video…");
        } else if (recoverableEndReasons.has(message.reason)) {
          recoverSession(false, message.message);
        } else {
          releaseLocal(false, message.message);
        }
      }
    },
    [acceptOffer, recoverSession, recoverWithoutMicrophone, releaseLocal],
  );

  useEffect(() => subscribePhoneWebcamResults(handleResult), [handleResult]);

  const changeCamera = useCallback(
    async (deviceId: string, reason: CameraChangeReason = "selection") => {
      if (reason === "selection") {
        window.clearTimeout(orientationTimerRef.current);
      } else if (
        acquiringReplacementGenerationRef.current !== null ||
        streamRef.current?.getVideoTracks()[0]?.muted
      ) {
        return;
      }
      const sender = senderRef.current;
      const peerSignal = peerSetupRef.current?.signal;
      if (!sender || !peerSignal || phase !== "streaming") {
        setSelectedCameraId(deviceId);
        return;
      }
      const previousCameraId = activeCameraIdRef.current ?? selectedCameraId;
      setSelectedCameraId(deviceId);
      setStatus(
        reason === "orientation"
          ? "Refreshing camera after rotation…"
          : reason === "stalled"
            ? "Restoring camera video…"
            : "Switching camera…",
      );
      const generation = generationRef.current;
      const replacementGeneration = ++replacementGenerationRef.current;
      acquiringReplacementGenerationRef.current = replacementGeneration;
      replacementCaptureRef.current?.abort();
      const capture = new AbortController();
      replacementCaptureRef.current = capture;
      const captureDeadline = scheduleCameraDeadline(capture);
      const previousReplacement = replacementRef.current;
      replacementRef.current = null;
      if (previousReplacement && !previousReplacement.updatingSender) {
        previousReplacement.stop();
      }
      const hasCurrentSender = () =>
        generationRef.current === generation && senderRef.current === sender;
      const ownsReplacement = () =>
        replacementGenerationRef.current === replacementGeneration && hasCurrentSender();
      const serializeSenderUpdate = (update: () => Promise<void>) => {
        const completion = senderUpdateTailRef.current.then(update);
        senderUpdateTailRef.current = completion.catch(() => undefined);
        return completion;
      };
      const updateSender = async (track: MediaStreamTrack | null) => {
        const deadline = window.setTimeout(() => {
          if (hasCurrentSender()) {
            recoverSession(true, "The camera switch timed out.");
          }
        }, senderUpdateTimeoutMs);
        try {
          await runWebRtcSetupStep(peerSignal, () => sender.replaceTrack(track));
        } finally {
          window.clearTimeout(deadline);
        }
      };
      let replacement: MediaStream | null = null;
      let ownedReplacement: PendingReplacement | null = null;
      let committed = false;
      try {
        const constraints: MediaStreamConstraints = {
          audio: false,
          video: {
            deviceId: { exact: deviceId },
            width: { ideal: preferredWidth, max: preferredWidth },
            height: { ideal: preferredHeight, max: preferredHeight },
            frameRate: { ideal: preferredFps, max: preferredFps },
          },
        };
        for (let attempt = 0; attempt < 2; attempt += 1) {
          if (!ownsReplacement()) {
            return;
          }
          try {
            replacement = await openCameraCapture(capture.signal, constraints);
            break;
          } catch (error) {
            const denied =
              error instanceof DOMException &&
              (error.name === "NotAllowedError" || error.name === "SecurityError");
            if (!ownsReplacement() || capture.signal.aborted || denied || attempt === 1) {
              throw error;
            }
            const requiresExclusiveCapture =
              error instanceof DOMException &&
              (error.name === "NotReadableError" || error.name === "AbortError");
            if (requiresExclusiveCapture) {
              await serializeSenderUpdate(async () => {
                if (!ownsReplacement() || capture.signal.aborted) {
                  return;
                }
                const activeStream = streamRef.current;
                const previousVideoWasReleased = activeVideoLossRef.current === "released";
                activeVideoLossRef.current = "released";
                if (!activeStream?.getAudioTracks()[0]) {
                  streamRef.current = null;
                }
                if (videoRef.current) {
                  videoRef.current.srcObject = null;
                }
                if (!previousVideoWasReleased) {
                  activeStream?.getVideoTracks().forEach((track) => track.stop());
                }
                await updateSender(null);
              });
              if (!ownsReplacement()) {
                return;
              }
            }
            let retryTimer: number | undefined;
            try {
              await runWebRtcSetupStep(
                capture.signal,
                () =>
                  new Promise<void>((resolve) => {
                    retryTimer = window.setTimeout(resolve, 200);
                  }),
              );
            } finally {
              window.clearTimeout(retryTimer);
            }
          }
        }
        if (!replacement) {
          throw new Error("Camera replacement did not open a video stream.");
        }
        window.clearTimeout(captureDeadline);
        if (!ownsReplacement()) {
          return;
        }
        const stream = replacement;
        let stopped = false;
        const candidate: PendingReplacement = {
          updatingSender: false,
          stop: () => {
            if (stopped) {
              return;
            }
            stopped = true;
            stream.getTracks().forEach((track) => track.stop());
          },
        };
        ownedReplacement = candidate;
        replacementRef.current = candidate;
        const nextTrack = stream.getVideoTracks()[0];
        if (!nextTrack || isEndedTrack(nextTrack)) {
          if (activeVideoLossRef.current !== null) {
            recoverSession(true, "The active camera stopped while switching cameras.");
          } else {
            setSelectedCameraId(previousCameraId);
            setStatus("The selected camera did not provide video.");
          }
          return;
        }
        nextTrack.contentHint = "motion";
        const settings = nextTrack.getSettings();
        await serializeSenderUpdate(async () => {
          if (!ownsReplacement()) {
            return;
          }
          if (isEndedTrack(nextTrack)) {
            throw new Error("The replacement camera stopped.");
          }
          candidate.updatingSender = true;
          await updateSender(nextTrack);
          if (!hasCurrentSender()) {
            return;
          }
          const ended = isEndedTrack(nextTrack);
          if (!ownsReplacement() || ended) {
            try {
              const currentTrack =
                activeVideoLossRef.current === null
                  ? (streamRef.current?.getVideoTracks()[0] ?? null)
                  : null;
              await updateSender(currentTrack);
            } catch {
              if (hasCurrentSender()) {
                recoverSession(true, "The camera connection could not be restored.");
              }
            }
            if (ended) {
              throw new Error("The replacement camera stopped.");
            }
            return;
          }
          const previous = streamRef.current;
          const activeAudioTrack = previous?.getAudioTracks()[0];
          if (activeAudioTrack) {
            stream.addTrack(activeAudioTrack);
          }
          const previousVideoWasReleased = activeVideoLossRef.current === "released";
          streamRef.current = stream;
          activeCameraIdRef.current = deviceId;
          watchActiveVideo(stream, generation);
          committed = true;
          statsGenerationRef.current += 1;
          lastStatsRef.current = null;
          stalledStatsSamplesRef.current = 0;
          activeVideoLossRef.current = null;
          setSelectedCameraId(deviceId);
          if (videoRef.current) {
            videoRef.current.srcObject = stream;
            requestPreviewPlayback();
          }
          if (!previousVideoWasReleased) {
            previous?.getVideoTracks().forEach((track) => track.stop());
          }
          setQuality({
            ...(settings.width === undefined ? {} : { width: settings.width }),
            ...(settings.height === undefined ? {} : { height: settings.height }),
            ...(settings.frameRate === undefined ? {} : { fps: settings.frameRate }),
          });
          if (peerRef.current?.connectionState === "connected") {
            setStatus(
              activePc.transportMode === "relay"
                ? "Streaming through Relay"
                : "Streaming through Enhanced Direct",
            );
          }
        });
      } catch (error) {
        if (!ownsReplacement()) {
          return;
        }
        if (activeVideoLossRef.current !== null || !streamRef.current) {
          recoverSession(
            true,
            activeVideoLossRef.current === "ended"
              ? "The active camera stopped while switching cameras."
              : "The selected camera could not be opened after releasing the previous camera.",
          );
          return;
        }
        setSelectedCameraId(previousCameraId);
        setStatus(
          error instanceof DOMException && error.name === "TimeoutError"
            ? "The selected camera did not open in time."
            : "The selected camera could not replace the active camera.",
        );
      } finally {
        if (acquiringReplacementGenerationRef.current === replacementGeneration) {
          acquiringReplacementGenerationRef.current = null;
        }
        if (replacementRef.current === ownedReplacement) {
          replacementRef.current = null;
        }
        if (!committed) {
          if (ownedReplacement) {
            ownedReplacement.stop();
          } else {
            replacement?.getTracks().forEach((track) => track.stop());
          }
        }
        window.clearTimeout(captureDeadline);
        if (replacementCaptureRef.current === capture) {
          replacementCaptureRef.current = null;
        }
      }
    },
    [
      activePc.transportMode,
      phase,
      recoverSession,
      requestPreviewPlayback,
      selectedCameraId,
      watchActiveVideo,
    ],
  );

  useEffect(() => {
    changeCameraRef.current = changeCamera;
  }, [changeCamera]);
  useEffect(() => {
    const refreshAfterRotation = () => {
      window.clearTimeout(orientationTimerRef.current);
      orientationTimerRef.current = window.setTimeout(() => {
        const deviceId = activeCameraIdRef.current;
        if (
          deviceId &&
          peerRef.current &&
          senderRef.current &&
          !pageHiddenRef.current &&
          document.visibilityState === "visible"
        ) {
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
    const pauseForBackground = () => {
      window.clearTimeout(restartTimerRef.current);
      restartTimerRef.current = undefined;
      if (acquiringGenerationRef.current !== null || streamRef.current || operationIdRef.current) {
        recoverSession(true, "Camera released while Voltura Air is in the background.");
      } else if (permissionLoadingRef.current || microphoneCaptureRef.current) {
        releaseRef.current(true, "Camera access is paused while Voltura Air is in the background.");
      }
    };
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        pauseForBackground();
      } else {
        scheduleResume();
      }
    };
    const onPageHide = () => {
      pageHiddenRef.current = true;
      pauseForBackground();
    };
    const onPageShow = () => {
      pageHiddenRef.current = false;
      onVisibility();
    };
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener("pageshow", onPageShow);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener("pageshow", onPageShow);
    };
  }, [recoverSession, scheduleResume]);

  useEffect(() => {
    const previous = lastConnectionRef.current;
    lastConnectionRef.current = {
      pcId: activePc.id,
      hostKey: activePc.hostIdentityPublicKey,
      epoch: connectionEpoch,
    };
    if (previous.pcId !== activePc.id || previous.hostKey !== activePc.hostIdentityPublicKey) {
      setUseMicrophone(false);
      setMicrophonePermissionGranted(false);
      releaseLocal(false, initialStatus(capability));
      return;
    }
    if (state === "needs-pairing" || state === "rejected") {
      if (
        operationIdRef.current ||
        acquiringGenerationRef.current !== null ||
        resumeRef.current ||
        permissionCaptureRef.current ||
        microphoneCaptureRef.current
      ) {
        setUseMicrophone(false);
        setMicrophonePermissionGranted(false);
        releaseLocal(false, "Pair with the PC to use Phone webcam.");
      }
      return;
    }
    const sameConnection = state === "paired" && previous.epoch === connectionEpoch;
    if (sameConnection && capabilityConfirmed) {
      scheduleResume();
      return;
    }
    window.clearTimeout(restartTimerRef.current);
    restartTimerRef.current = undefined;
    if (acquiringGenerationRef.current !== null || streamRef.current || operationIdRef.current) {
      recoverSession(
        sameConnection,
        sameConnection
          ? "Waiting for the PC to confirm Phone webcam…"
          : "Waiting for the PC connection…",
      );
    } else if (permissionLoadingRef.current || microphoneCaptureRef.current) {
      releaseLocal(false, "Waiting for the PC connection…");
    }
    if (state === "paired") {
      scheduleResume();
    }
  }, [
    activePc.hostIdentityPublicKey,
    activePc.id,
    capability,
    capabilityConfirmed,
    connectionEpoch,
    recoverSession,
    releaseLocal,
    scheduleResume,
    state,
  ]);

  useEffect(() => {
    microphoneAvailableRef.current = capability.microphoneAvailable;
    if (capability.microphoneAvailable || (phase !== "idle" && phase !== "recovering")) {
      return;
    }
    microphoneRequestGenerationRef.current += 1;
    microphoneCaptureRef.current?.abort();
    let cancelled = false;
    queueMicrotask(() => {
      if (cancelled) {
        return;
      }
      setMicrophonePermissionLoading(false);
      setMicrophonePermissionGranted(false);
      setUseMicrophone(false);
    });
    return () => {
      cancelled = true;
    };
  }, [capability.microphoneAvailable, phase]);

  useEffect(() => {
    if (capability.canUse) {
      return;
    }
    if (
      resumeRef.current ||
      permissionLoadingRef.current ||
      microphoneCaptureRef.current ||
      acquiringGenerationRef.current !== null ||
      streamRef.current ||
      replacementRef.current ||
      pendingRef.current ||
      peerRef.current
    ) {
      releaseLocal(false, initialStatus(capability));
    } else {
      setStatus(initialStatus(capability));
    }
  }, [capability, releaseLocal]);

  useEffect(
    () => () => {
      releaseRef.current(true, "Phone webcam stopped.");
    },
    [],
  );

  const toggleMicrophoneMute = useCallback(() => {
    const track = streamRef.current?.getAudioTracks()[0];
    if (!track || !microphoneActive) {
      return;
    }
    const muted = track.enabled;
    track.enabled = !muted;
    microphoneMutedRef.current = muted;
    setMicrophoneMuted(muted);
  }, [microphoneActive]);

  const stop = () => {
    setUseMicrophone(false);
    setMicrophonePermissionGranted(false);
    releaseLocal(true, "Ready to start.");
  };

  const canStart =
    supportedTransport &&
    permissionGranted &&
    !microphonePermissionLoading &&
    selectedCameraId &&
    capability.canUse &&
    capabilityConfirmed &&
    state === "paired" &&
    phase === "idle";
  const cameraAccessReady = permissionGranted && cameras.length > 0;
  const visibleStatus = cameraPaused
    ? "Camera paused by the browser."
    : phase === "idle" && permissionGranted && !cameraAccessReady
      ? "No camera is available."
      : status;
  const expandedStatus = phase !== "streaming" || cameraPaused;
  return (
    <section
      className={`phone-webcam-workspace${isCameraViewExpanded ? " camera-view-expanded" : ""}`}
      aria-labelledby="phone-webcam-title"
      onKeyDown={(event) => {
        if (event.key === "Escape" && isCameraViewExpanded) {
          event.stopPropagation();
          event.preventDefault();
          setIsCameraViewExpanded(false);
        }
      }}
    >
      <header className="phone-webcam-header">
        <button
          type="button"
          className="icon-button"
          aria-label="Back"
          inert={isCameraViewExpanded}
          onClick={() => {
            stop();
            onBack();
          }}
        >
          <ArrowLeft aria-hidden="true" />
        </button>
        <div>
          <p>Video with optional audio</p>
          <h1 id="phone-webcam-title">Phone webcam</h1>
        </div>
      </header>

      <div className="phone-webcam-preview" aria-label="Camera view">
        <video
          ref={videoRef}
          muted
          playsInline
          autoPlay
          aria-label="Selected phone camera view"
          onPause={resumePausedPreview}
        />
        <button
          type="button"
          className="phone-webcam-expand"
          aria-label={isCameraViewExpanded ? "Restore camera view" : "Expand camera view"}
          title={isCameraViewExpanded ? "Restore camera view" : "Expand camera view"}
          onClick={() => {
            setIsCameraViewExpanded((current) => !current);
          }}
        >
          {isCameraViewExpanded ? (
            <Minimize2 aria-hidden="true" />
          ) : (
            <Maximize2 aria-hidden="true" />
          )}
        </button>
        {phase === "idle" && !isCameraViewExpanded && (
          <div className="phone-webcam-placeholder">
            <Camera aria-hidden="true" />
            <span>Camera view appears when streaming starts</span>
          </div>
        )}
        {isCameraViewExpanded && (
          <div className="phone-webcam-preview-status">
            {expandedStatus && (
              <span className="phone-webcam-preview-status-text" role="status">
                {(phase === "recovering" || phase === "interrupted") && (
                  <strong>Reconnecting</strong>
                )}
                <span>{visibleStatus}</span>
              </span>
            )}
            {phase === "idle" ? (
              <button type="button" disabled={!canStart} onClick={() => void start()}>
                Start
              </button>
            ) : (
              <button type="button" onClick={stop}>
                Stop
              </button>
            )}
          </div>
        )}
      </div>

      <div
        className="phone-webcam-controls"
        inert={isCameraViewExpanded}
        aria-hidden={isCameraViewExpanded}
      >
        {!cameraAccessReady && phase === "idle" && (
          <button
            type="button"
            className="primary-button"
            disabled={
              permissionLoading ||
              !supportedTransport ||
              !capability.canUse ||
              !capabilityConfirmed ||
              state !== "paired"
            }
            onClick={() => {
              void loadCameras();
            }}
          >
            {permissionLoading ? "Opening camera…" : "Allow camera access"}
          </button>
        )}
        {cameraAccessReady && (
          <label>
            Camera
            <select
              value={selectedCameraId}
              disabled={phase !== "idle" && phase !== "streaming"}
              onChange={(event) => {
                void changeCamera(event.target.value);
              }}
            >
              {cameras.map((camera) => (
                <option key={camera.deviceId} value={camera.deviceId}>
                  {camera.label}
                </option>
              ))}
            </select>
          </label>
        )}
        {cameraAccessReady && (capability.microphoneAvailable || microphoneActive) && (
          <div className="phone-webcam-microphone">
            {capability.microphoneAvailable && (
              <label>
                <input
                  type="checkbox"
                  checked={useMicrophone}
                  disabled={
                    phase !== "idle" ||
                    microphonePermissionLoading ||
                    !capabilityConfirmed ||
                    state !== "paired"
                  }
                  onChange={(event) => {
                    void requestMicrophone(event.target.checked);
                  }}
                />
                {microphonePermissionLoading ? "Opening microphone…" : "Use microphone"}
              </label>
            )}
            {microphoneActive && phase !== "idle" && (
              <button type="button" aria-pressed={microphoneMuted} onClick={toggleMicrophoneMute}>
                {microphoneMuted ? <MicOff aria-hidden="true" /> : <Mic aria-hidden="true" />}
                {microphoneMuted ? "Unmute" : "Mute"}
              </button>
            )}
          </div>
        )}
        <div className="phone-webcam-actions">
          <button
            type="button"
            className="primary-button"
            disabled={!canStart}
            onClick={() => {
              void start();
            }}
          >
            <Camera aria-hidden="true" />
            Start
          </button>
          <button type="button" disabled={phase === "idle"} onClick={stop}>
            <CameraOff aria-hidden="true" />
            Stop
          </button>
        </div>
      </div>

      <div className="phone-webcam-status" role="status" aria-hidden={isCameraViewExpanded}>
        <strong>
          {phase === "streaming"
            ? "Streaming"
            : phase === "connecting"
              ? "Connecting"
              : phase === "idle"
                ? "Ready"
                : "Reconnecting"}
        </strong>
        <span>
          {supportedTransport ? visibleStatus : "Phone webcam requires Enhanced Direct or Relay."}
        </span>
        {quality.width && quality.height && (
          <span>
            {Math.round(quality.width)}×{Math.round(quality.height)}
            {quality.fps ? ` at ${quality.fps.toFixed(1)} fps` : ""}
            {quality.bitrateMbps !== undefined ? `; ${quality.bitrateMbps.toFixed(2)} Mbps` : ""}
          </span>
        )}
        {supportedTransport && (
          <span>
            {activePc.transportMode === "relay"
              ? "Relay · shared usage limits apply"
              : "Enhanced Direct · free and unlimited"}
          </span>
        )}
      </div>
    </section>
  );
}

function initialStatus(capability: PhoneWebcamCapability): string {
  if (capability.requiresRepair) {
    return "Repair Phone webcam on the PC before using it.";
  }
  if (!capability.enabled) {
    return "Enable Phone webcam in the Windows app first.";
  }
  if (!capability.permissionGranted) {
    return "This paired device is not allowed to use Phone webcam.";
  }
  return "Allow camera access to choose a camera.";
}

function describeNegotiationFailure(stage: string, error: unknown): string {
  const category =
    error instanceof DOMException ? error.name : error instanceof Error ? error.name : "Error";
  const detail =
    error instanceof DOMException || error instanceof Error
      ? sanitizeBrowserErrorDetail(error.message)
      : "No browser detail was provided.";
  return `This browser could not finish Phone webcam while ${stage} (${category}: ${detail || "No browser detail was provided."}).`;
}

function sanitizeBrowserErrorDetail(message: string): string {
  return Array.from(message, (character) => {
    const code = character.charCodeAt(0);
    return code <= 31 || code === 127 ? " " : character;
  })
    .join("")
    .trim()
    .slice(0, 180);
}

function hasExpectedPhoneWebcamMedia(
  sdp: string,
  useMicrophone: boolean,
  direction: "recvonly" | "sendonly",
): boolean {
  const sections = sdp
    .split(/\r?\n/u)
    .reduce<{ kind: string; lines: string[] }[]>((result, line) => {
      if (line.startsWith("m=")) {
        result.push({ kind: line.slice(2).split(" ", 1)[0] ?? "", lines: [line] });
      } else {
        result.at(-1)?.lines.push(line);
      }
      return result;
    }, []);
  if (sections.length !== (useMicrophone ? 2 : 1)) {
    return false;
  }
  const video = sections.filter((section) => section.kind === "video");
  const audio = sections.filter((section) => section.kind === "audio");
  if (video.length !== 1 || audio.length !== (useMicrophone ? 1 : 0)) {
    return false;
  }
  if (!hasExactCodec(video[0]!, "102", "h264/90000", direction)) {
    return false;
  }
  if (!useMicrophone) {
    return true;
  }
  return hasExactCodec(audio[0]!, "111", "opus/48000/2", direction);
}

function hasExactCodec(
  section: { lines: string[] },
  payloadType: string,
  codec: string,
  direction: "recvonly" | "sendonly",
): boolean {
  const media = section.lines[0]?.trim().split(/\s+/u) ?? [];
  if (media.length !== 4 || media[1] === "0" || media[3] !== payloadType) {
    return false;
  }
  const mappings = section.lines.filter((line) => line.startsWith("a=rtpmap:"));
  const directions = section.lines.filter(
    (line) =>
      line === "a=sendrecv" ||
      line === "a=sendonly" ||
      line === "a=recvonly" ||
      line === "a=inactive",
  );
  return (
    mappings.length === 1 &&
    mappings[0]?.toLowerCase() === `a=rtpmap:${payloadType} ${codec}` &&
    directions.length === 1 &&
    directions[0] === `a=${direction}`
  );
}

interface OutboundVideoStats {
  bytesSent: number;
  framesEncoded?: number;
  frameWidth?: number;
  frameHeight?: number;
  framesPerSecond?: number;
}

function readOutboundVideoReport(report: RTCStatsReport): OutboundVideoStats | null {
  let sample: OutboundVideoStats | null = null;
  let area = -1;
  let bytesSent = 0;
  let framesEncoded: number | undefined = 0;
  for (const value of report.values()) {
    const row = readOutboundVideoStats(value);
    if (!row) {
      continue;
    }
    bytesSent += row.bytesSent;
    framesEncoded =
      framesEncoded !== undefined && row.framesEncoded !== undefined
        ? framesEncoded + row.framesEncoded
        : undefined;
    const rowArea = (row.frameWidth ?? 0) * (row.frameHeight ?? 0);
    if (rowArea > area) {
      sample = row;
      area = rowArea;
    }
  }
  if (!sample) {
    return null;
  }
  const { frameWidth, frameHeight, framesPerSecond } = sample;
  return {
    bytesSent,
    ...(framesEncoded === undefined ? {} : { framesEncoded }),
    ...(frameWidth === undefined ? {} : { frameWidth }),
    ...(frameHeight === undefined ? {} : { frameHeight }),
    ...(framesPerSecond === undefined ? {} : { framesPerSecond }),
  };
}

function validStat(value: unknown, minimum = 0): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= minimum;
}

function readOutboundVideoStats(value: unknown): OutboundVideoStats | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const record = value as Record<string, unknown>;
  if (
    record.type !== "outbound-rtp" ||
    (record.kind ?? record.mediaType) !== "video" ||
    record.active === false ||
    !validStat(record.bytesSent)
  ) {
    return null;
  }
  return {
    bytesSent: record.bytesSent,
    ...(validStat(record.framesEncoded) ? { framesEncoded: record.framesEncoded } : {}),
    ...(validStat(record.frameWidth, 1) ? { frameWidth: record.frameWidth } : {}),
    ...(validStat(record.frameHeight, 1) ? { frameHeight: record.frameHeight } : {}),
    ...(validStat(record.framesPerSecond) ? { framesPerSecond: record.framesPerSecond } : {}),
  };
}
