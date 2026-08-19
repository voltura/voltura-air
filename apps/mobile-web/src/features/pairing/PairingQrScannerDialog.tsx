import { useEffect, useRef, useState } from "react";
import { Camera, Image } from "lucide-react";
import { createQrDecoderSession } from "../../foundation/pairing/qrCode";
import { ModalDialog } from "../../ui/overlays/ModalDialog";

const liveFrameIntervalMs = 250;
const liveFrameMaximumDimension = 640;

interface PairingQrScannerDialogProps {
  attemptId: number | null;
  onAccept: (attemptId: number, scannedText: string) => boolean;
  onFallback: (attemptId: number, message: string, openPhoto: boolean) => void;
}

export function PairingQrScannerDialog({ attemptId, onAccept, onFallback }: PairingQrScannerDialogProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const onAcceptRef = useRef(onAccept);
  const onFallbackRef = useRef(onFallback);
  const releaseAttemptRef = useRef<() => void>(() => undefined);
  const [cameraReady, setCameraReady] = useState(false);
  const [status, setStatus] = useState("Waiting for camera permission…");

  useEffect(() => {
    onAcceptRef.current = onAccept;
    onFallbackRef.current = onFallback;
  }, [onAccept, onFallback]);

  useEffect(() => {
    if (attemptId === null) {
      return;
    }

    let active = true;
    let finished = false;
    let timer = 0;
    let stream: MediaStream | null = null;
    let videoTrack: MediaStreamTrack | null = null;
    let decoder: ReturnType<typeof createQrDecoderSession> | null = null;
    const canvas = document.createElement("canvas");

    const release = () => {
      active = false;
      window.clearTimeout(timer);
      decoder?.dispose();
      decoder = null;
      stream?.getTracks().forEach((track) => { track.stop(); });
      stream = null;
      if (videoRef.current) {
        videoRef.current.srcObject = null;
      }
    };
    releaseAttemptRef.current = release;
    const fallback = (message: string) => {
      if (!active || finished) {
        return;
      }
      finished = true;
      release();
      onFallbackRef.current(attemptId, message, false);
    };
    const scheduleFrame = (delay = liveFrameIntervalMs) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => { void decodeFrame(); }, delay);
    };
    const decodeFrame = async () => {
      const video = videoRef.current;
      if (!active || finished || !video || !decoder) {
        return;
      }
      if (video.videoWidth <= 0 || video.videoHeight <= 0) {
        scheduleFrame(100);
        return;
      }

      const sourceDimension = Math.min(video.videoWidth, video.videoHeight);
      const targetDimension = Math.min(sourceDimension, liveFrameMaximumDimension);
      const sourceX = Math.floor((video.videoWidth - sourceDimension) / 2);
      const sourceY = Math.floor((video.videoHeight - sourceDimension) / 2);
      canvas.width = targetDimension;
      canvas.height = targetDimension;
      const context = canvas.getContext("2d", { alpha: false, willReadFrequently: true });
      if (!context) {
        fallback("Live QR scanning is unavailable. Take a photo of the QR code instead.");
        return;
      }

      try {
        context.drawImage(
          video,
          sourceX,
          sourceY,
          sourceDimension,
          sourceDimension,
          0,
          0,
          targetDimension,
          targetDimension
        );
        const scannedText = await decoder.decode(context.getImageData(0, 0, targetDimension, targetDimension));
        if (!active || finished) {
          return;
        }
        if (scannedText) {
          if (onAcceptRef.current(attemptId, scannedText)) {
            finished = true;
            release();
            return;
          }
          setStatus("That is not a Voltura Air pairing code. Point the camera at the QR code shown on the PC.");
        }
        scheduleFrame();
      } catch {
        if (active && !finished) {
          fallback("Live QR scanning stopped. Take a photo of the QR code instead.");
        }
      }
    };
    const leaveForeground = () => {
      if (document.visibilityState === "hidden") {
        fallback("Camera scanning stopped when Voltura Air left the foreground. Take a photo of the QR code instead.");
      }
    };
    const pageHide = () => {
      fallback("Camera scanning stopped when Voltura Air left the foreground. Take a photo of the QR code instead.");
    };
    const cameraEnded = () => {
      fallback("The camera stopped. Take a photo of the QR code instead.");
    };

    document.addEventListener("visibilitychange", leaveForeground);
    window.addEventListener("pagehide", pageHide);
    void navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: "environment" } }
    }).then((candidate) => {
      if (!active || finished) {
        candidate.getTracks().forEach((track) => { track.stop(); });
        return;
      }

      stream = candidate;
      videoTrack = candidate.getVideoTracks()[0] ?? null;
      if (!videoTrack) {
        fallback("No camera is available. Take a photo of the QR code instead.");
        return;
      }
      videoTrack.addEventListener("ended", cameraEnded);
      try {
        decoder = createQrDecoderSession();
      } catch {
        fallback("Live QR scanning is unavailable. Take a photo of the QR code instead.");
        return;
      }

      const video = videoRef.current;
      if (!video) {
        fallback("Live QR scanning is unavailable. Take a photo of the QR code instead.");
        return;
      }
      video.srcObject = candidate;
      setStatus("Point the camera at the QR code shown on the PC.");
      void video.play().then(() => {
        setCameraReady(true);
        scheduleFrame(0);
      }).catch(() => {
        fallback("The camera preview could not start. Take a photo of the QR code instead.");
      });
    }).catch(() => {
      if (active && !finished) {
        fallback("Camera access was not allowed. Take a photo of the QR code instead.");
      }
    });

    return () => {
      document.removeEventListener("visibilitychange", leaveForeground);
      window.removeEventListener("pagehide", pageHide);
      videoTrack?.removeEventListener("ended", cameraEnded);
      release();
      if (releaseAttemptRef.current === release) {
        releaseAttemptRef.current = () => undefined;
      }
    };
  }, [attemptId]);

  const usePhoto = () => {
    if (attemptId !== null) {
      releaseAttemptRef.current();
      onFallbackRef.current(attemptId, "Take a clear photo of the QR code shown on the PC.", true);
    }
  };
  const cancel = () => {
    if (attemptId !== null) {
      releaseAttemptRef.current();
      onFallbackRef.current(attemptId, "Live scanning was cancelled. Take a photo of the QR code instead.", false);
    }
  };

  return (
    <ModalDialog
      actions={<>
        <button type="button" onClick={cancel}>Cancel</button>
        <button className="pairing-qr-photo-action" type="button" onClick={usePhoto}>
          <Image aria-hidden="true" />
          <span>Take photo instead</span>
        </button>
      </>}
      actionsClassName="pairing-qr-scanner-actions"
      className="pairing-qr-scanner-dialog"
      dismissLabel="Cancel"
      isOpen={attemptId !== null}
      landscapeSize="wide"
      onClose={cancel}
      title="Scan pairing QR code"
    >
      <div className="pairing-qr-scanner">
        <div className="pairing-qr-preview">
          <video ref={videoRef} autoPlay muted playsInline aria-label="Camera view for pairing QR code" />
          <div className="pairing-qr-guide" aria-hidden="true" />
          {!cameraReady && <div className="pairing-qr-waiting" aria-hidden="true"><Camera /></div>}
        </div>
        <p role="status">{status}</p>
      </div>
    </ModalDialog>
  );
}

export default PairingQrScannerDialog;
