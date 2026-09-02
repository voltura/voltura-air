export type ScreenViewRecordingPhase = "idle" | "recording" | "finalizing" | "ready" | "error";

export interface ScreenViewRecordingPresentation {
  phase: ScreenViewRecordingPhase;
  fileName: string;
  message: string;
  elapsedMs: number;
  includesSound: boolean;
}

export const screenViewRecordingMaximumDurationMs = 5 * 60 * 1_000;
export const screenViewRecordingMaximumBytes = 512 * 1024 * 1024;
export const screenViewRecordingSoftStopBytes = 480 * 1024 * 1024;
export const screenViewRecordingMaximumQueuedBytes = 16 * 1024 * 1024;

export interface ScreenViewRecordingFormat {
  extension: "mp4" | "webm";
  mimeType: string;
}

export function chooseScreenViewRecordingFormat(
  includeSound: boolean,
): ScreenViewRecordingFormat | null {
  if (typeof MediaRecorder === "undefined" || typeof MediaRecorder.isTypeSupported !== "function") {
    return null;
  }
  const candidates: ScreenViewRecordingFormat[] = includeSound
    ? [
        { extension: "mp4", mimeType: "video/mp4;codecs=avc1.42E01E,mp4a.40.2" },
        { extension: "mp4", mimeType: "video/mp4" },
        { extension: "webm", mimeType: "video/webm;codecs=vp8,opus" },
        { extension: "webm", mimeType: "video/webm" },
      ]
    : [
        { extension: "mp4", mimeType: "video/mp4;codecs=avc1.42E01E" },
        { extension: "mp4", mimeType: "video/mp4" },
        { extension: "webm", mimeType: "video/webm;codecs=vp8" },
        { extension: "webm", mimeType: "video/webm" },
      ];
  return candidates.find((candidate) => MediaRecorder.isTypeSupported(candidate.mimeType)) ?? null;
}

export function createScreenViewRecordingFileName(capturedAt: Date, mimeType: string): string {
  const extension = mimeType.toLowerCase().startsWith("video/mp4") ? "mp4" : "webm";
  const part = (value: number) => value.toString().padStart(2, "0");
  const timestamp = `${capturedAt.getFullYear()}-${part(capturedAt.getMonth() + 1)}-${part(capturedAt.getDate())} ${part(capturedAt.getHours())}-${part(capturedAt.getMinutes())}-${part(capturedAt.getSeconds())}`;
  return `Voltura Air - Screen recording - ${timestamp}.${extension}`;
}
