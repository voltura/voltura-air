import { useEffect, useEffectEvent, useRef, useState } from "react";
import {
  prepareDeviceFileStorage,
  removeDeviceFile,
  saveOrShareDeviceFile,
  supportsDeviceFileStorage,
  type DeviceFileStorage,
} from "../../foundation/file-transfer/fileTransferDeviceStorage";
import { createLocalId } from "../../foundation/identity/localId";
import {
  chooseScreenViewRecordingFormat,
  createScreenViewRecordingFileName,
  screenViewRecordingMaximumBytes,
  screenViewRecordingMaximumDurationMs,
  screenViewRecordingMaximumQueuedBytes,
  screenViewRecordingSoftStopBytes,
  type ScreenViewRecordingPresentation,
} from "./screenViewRecording";

const recordingChunkIntervalMs = 1_000;
const recordingVideoBitsPerSecond = 8_000_000;
const recordingAudioBitsPerSecond = 96_000;

const idlePresentation = (message = ""): ScreenViewRecordingPresentation => ({
  phase: "idle",
  fileName: "",
  message,
  elapsedMs: 0,
  includesSound: false,
});

interface RecordingRuntime {
  recorder: MediaRecorder | null;
  tracks: MediaStreamTrack[];
  storage: DeviceFileStorage | null;
  writable: FileSystemWritableFileStream | null;
  readyFile: File | null;
  fileName: string;
  includesSound: boolean;
  startedAt: number;
  bytesWritten: number;
  queuedBytes: number;
  writeChain: Promise<void>;
  stopMessage: string;
  stopTimer: number | undefined;
  discarding: boolean;
  stopRequested: boolean;
  finalizationStarted: boolean;
  videoEnded: (() => void) | null;
  audioEnded: (() => void) | null;
}

export function useScreenViewRecording(
  onNotice?: (message: string, tone: "success" | "error" | "neutral") => void,
) {
  const [presentation, setPresentation] = useState<ScreenViewRecordingPresentation>(() =>
    idlePresentation(),
  );
  const runtimeRef = useRef<RecordingRuntime | null>(null);
  const noticeRef = useRef(onNotice);
  const notify = (message: string, tone: "success" | "error" | "neutral") =>
    noticeRef.current?.(message, tone);
  const supported = supportsDeviceFileStorage() && chooseScreenViewRecordingFormat(false) !== null;
  const unsupportedReason = !supportsDeviceFileStorage()
    ? "This browser cannot stage a recording for Save or Share."
    : chooseScreenViewRecordingFormat(false) === null
      ? "This browser cannot record the live PC video."
      : "";

  useEffect(() => {
    noticeRef.current = onNotice;
  }, [onNotice]);

  function stopOwnedTracks(runtime: RecordingRuntime) {
    if (runtime.videoEnded) {
      runtime.tracks[0]?.removeEventListener("ended", runtime.videoEnded);
      runtime.videoEnded = null;
    }
    if (runtime.audioEnded) {
      runtime.tracks
        .find((track) => track.kind === "audio")
        ?.removeEventListener("ended", runtime.audioEnded);
      runtime.audioEnded = null;
    }
    for (const track of runtime.tracks) {
      track.stop();
    }
  }

  async function removeStoredRuntime(runtime: RecordingRuntime) {
    if (!runtime.storage) {
      return;
    }
    try {
      await removeDeviceFile(runtime.storage.directory, runtime.storage.storedName);
    } catch {
      /* The next page initialization retries the shared owner sweep. */
    }
  }

  async function disposeRuntime(runtime: RecordingRuntime) {
    if (runtime.discarding) {
      return;
    }
    runtime.discarding = true;
    window.clearTimeout(runtime.stopTimer);
    runtime.stopTimer = undefined;
    if (runtime.recorder?.state !== "inactive") {
      try {
        runtime.recorder?.stop();
      } catch {
        /* Cleanup continues through the owned stream and partial file. */
      }
    }
    if (runtime.writable) {
      try {
        await runtime.writable.abort();
      } catch {
        /* Removal below remains the authoritative cleanup attempt. */
      }
      runtime.writable = null;
    }
    stopOwnedTracks(runtime);
    await removeStoredRuntime(runtime);
  }

  async function failRuntime(runtime: RecordingRuntime, message: string) {
    if (runtimeRef.current !== runtime || runtime.discarding) {
      return;
    }
    runtimeRef.current = null;
    await disposeRuntime(runtime);
    setPresentation({
      phase: "error",
      fileName: "",
      message,
      elapsedMs: 0,
      includesSound: false,
    });
    notify(message, "error");
  }

  async function finalizeRuntime(runtime: RecordingRuntime) {
    if (runtimeRef.current !== runtime || runtime.discarding || runtime.finalizationStarted) {
      return;
    }
    runtime.finalizationStarted = true;
    try {
      await runtime.writeChain;
      if (
        runtimeRef.current !== runtime ||
        runtime.discarding ||
        !runtime.writable ||
        !runtime.storage
      ) {
        return;
      }
      await runtime.writable.close();
      runtime.writable = null;
      const stored = await runtime.storage.handle.getFile();
      if (stored.size === 0 || stored.size !== runtime.bytesWritten) {
        throw new Error("The recorded file was incomplete.");
      }
      runtime.readyFile = stored;
      window.clearTimeout(runtime.stopTimer);
      runtime.stopTimer = undefined;
      stopOwnedTracks(runtime);
      if (runtimeRef.current !== runtime || runtime.discarding) {
        return;
      }
      setPresentation({
        phase: "ready",
        fileName: runtime.fileName,
        message: runtime.stopMessage,
        elapsedMs: Math.min(
          screenViewRecordingMaximumDurationMs,
          Math.max(0, performance.now() - runtime.startedAt),
        ),
        includesSound: runtime.includesSound,
      });
    } catch (error) {
      await failRuntime(
        runtime,
        error instanceof Error && error.message === "The recorded file was incomplete."
          ? error.message
          : "This device could not finish storing the recording.",
      );
    }
  }

  function requestStop(runtime: RecordingRuntime, message: string) {
    if (runtimeRef.current !== runtime || runtime.discarding || runtime.stopRequested) {
      return;
    }
    runtime.stopRequested = true;
    runtime.stopMessage = message;
    window.clearTimeout(runtime.stopTimer);
    runtime.stopTimer = undefined;
    setPresentation((current) => ({ ...current, phase: "finalizing", message: "Finalizing…" }));
    if (!runtime.recorder) {
      runtimeRef.current = null;
      void disposeRuntime(runtime).then(() =>
        setPresentation(idlePresentation("Recording canceled.")),
      );
      return;
    }
    if (runtime.recorder.state === "inactive") {
      return;
    }
    try {
      runtime.recorder.stop();
    } catch {
      void failRuntime(runtime, "This browser could not stop the recording cleanly.");
    }
  }

  function acceptChunk(runtime: RecordingRuntime, blob: Blob) {
    if (runtimeRef.current !== runtime || runtime.discarding || blob.size === 0) {
      return;
    }
    const projectedBytes = runtime.bytesWritten + runtime.queuedBytes + blob.size;
    if (
      projectedBytes > screenViewRecordingMaximumBytes ||
      runtime.queuedBytes + blob.size > screenViewRecordingMaximumQueuedBytes
    ) {
      void failRuntime(
        runtime,
        projectedBytes > screenViewRecordingMaximumBytes
          ? "The recording exceeded its 512 MiB device-storage limit."
          : "This device could not store recording data quickly enough.",
      );
      return;
    }
    runtime.queuedBytes += blob.size;
    const position = runtime.bytesWritten + runtime.queuedBytes - blob.size;
    runtime.writeChain = runtime.writeChain.then(async () => {
      if (runtimeRef.current !== runtime || runtime.discarding || !runtime.writable) {
        runtime.queuedBytes -= blob.size;
        return;
      }
      try {
        await runtime.writable.write({ type: "write", position, data: blob });
        runtime.bytesWritten += blob.size;
        runtime.queuedBytes -= blob.size;
      } catch {
        runtime.queuedBytes -= blob.size;
        await failRuntime(runtime, "This device could not store the recording.");
      }
    });
    const elapsedMs = Math.min(
      screenViewRecordingMaximumDurationMs,
      Math.max(0, performance.now() - runtime.startedAt),
    );
    setPresentation((current) =>
      current.phase === "recording" ? { ...current, elapsedMs } : current,
    );
    if (projectedBytes >= screenViewRecordingSoftStopBytes) {
      requestStop(runtime, "Recording stopped at its device-storage limit.");
    }
  }

  async function start(stream: MediaStream | null, includeSound: boolean): Promise<boolean> {
    if (runtimeRef.current) {
      return false;
    }
    const format = chooseScreenViewRecordingFormat(includeSound);
    if (!supportsDeviceFileStorage() || !format) {
      const message = !supportsDeviceFileStorage()
        ? "This browser cannot stage a recording for Save or Share."
        : "This browser cannot record the requested video and sound format.";
      setPresentation({
        phase: "error",
        fileName: "",
        message,
        elapsedMs: 0,
        includesSound: false,
      });
      notify(message, "error");
      return false;
    }
    const sourceVideo = stream?.getVideoTracks()[0];
    const sourceAudio = includeSound ? stream?.getAudioTracks()[0] : undefined;
    if (!sourceVideo || sourceVideo.readyState === "ended") {
      const message = "The live PC video is not ready to record.";
      setPresentation({
        phase: "error",
        fileName: "",
        message,
        elapsedMs: 0,
        includesSound: includeSound,
      });
      notify(message, "error");
      return false;
    }
    if (includeSound && (!sourceAudio || sourceAudio.readyState === "ended")) {
      const message = "PC sound is enabled but its live audio track is not ready.";
      setPresentation({
        phase: "error",
        fileName: "",
        message,
        elapsedMs: 0,
        includesSound: includeSound,
      });
      notify(message, "error");
      return false;
    }

    const tracks: MediaStreamTrack[] = [];
    let recordingStream: MediaStream;
    try {
      tracks.push(sourceVideo.clone());
      if (sourceAudio) {
        tracks.push(sourceAudio.clone());
      }
      recordingStream = new MediaStream(tracks);
    } catch {
      for (const track of tracks) {
        track.stop();
      }
      const message = "This browser could not create an isolated recording stream.";
      setPresentation({
        phase: "error",
        fileName: "",
        message,
        elapsedMs: 0,
        includesSound: includeSound,
      });
      notify(message, "error");
      return false;
    }
    const runtime: RecordingRuntime = {
      recorder: null,
      tracks,
      storage: null,
      writable: null,
      readyFile: null,
      fileName: "PC screen recording",
      includesSound: includeSound,
      startedAt: performance.now(),
      bytesWritten: 0,
      queuedBytes: 0,
      writeChain: Promise.resolve(),
      stopMessage: "Recording ready to save or share.",
      stopTimer: undefined,
      discarding: false,
      stopRequested: false,
      finalizationStarted: false,
      videoEnded: null,
      audioEnded: null,
    };
    runtimeRef.current = runtime;
    setPresentation({
      phase: "recording",
      fileName: runtime.fileName,
      message: "Preparing recording…",
      elapsedMs: 0,
      includesSound: includeSound,
    });

    try {
      const storage = await prepareDeviceFileStorage(
        screenViewRecordingMaximumBytes,
        `screen-recording-${createLocalId()}`,
        true,
      );
      runtime.storage = storage;
      runtime.writable = storage.writable;
      if (runtimeRef.current !== runtime || runtime.discarding) {
        try {
          await storage.writable.abort();
        } catch {
          /* Removal remains the authoritative cleanup attempt. */
        }
        try {
          await removeDeviceFile(storage.directory, storage.storedName);
        } catch {
          /* The next page initialization retries the shared owner sweep. */
        }
        return false;
      }
      const recorder = new MediaRecorder(recordingStream, {
        mimeType: format.mimeType,
        videoBitsPerSecond: recordingVideoBitsPerSecond,
        ...(includeSound ? { audioBitsPerSecond: recordingAudioBitsPerSecond } : {}),
      });
      runtime.recorder = recorder;
      runtime.fileName = createScreenViewRecordingFileName(
        new Date(),
        recorder.mimeType || format.mimeType,
      );
      runtime.startedAt = performance.now();
      recorder.addEventListener("dataavailable", (event) => acceptChunk(runtime, event.data));
      recorder.addEventListener("error", () => {
        void failRuntime(runtime, "This browser could not continue recording the PC screen.");
      });
      recorder.addEventListener("stop", () => {
        void finalizeRuntime(runtime);
      });
      runtime.videoEnded = () => requestStop(runtime, "Screen viewing ended. Recording is ready.");
      tracks[0]!.addEventListener("ended", runtime.videoEnded);
      const audioTrack = tracks.find((track) => track.kind === "audio");
      if (audioTrack) {
        runtime.audioEnded = () => {
          if (runtimeRef.current === runtime && !runtime.discarding) {
            setPresentation((current) => ({
              ...current,
              message: "Recording continues without further PC sound.",
            }));
            notify("Recording continues without further PC sound.", "neutral");
          }
        };
        audioTrack.addEventListener("ended", runtime.audioEnded);
      }
      recorder.start(recordingChunkIntervalMs);
      runtime.stopTimer = window.setTimeout(
        () => requestStop(runtime, "Five-minute recording complete."),
        screenViewRecordingMaximumDurationMs,
      );
      setPresentation({
        phase: "recording",
        fileName: runtime.fileName,
        message: includeSound ? "Recording PC video with sound…" : "Recording PC video…",
        elapsedMs: 0,
        includesSound: includeSound,
      });
      return true;
    } catch (error) {
      await failRuntime(
        runtime,
        error instanceof Error && error.message.includes("enough available browser storage")
          ? "Recording needs about 512 MiB of available browser storage."
          : "This browser could not start the screen recording.",
      );
      return false;
    }
  }

  async function saveReadyFile() {
    const runtime = runtimeRef.current;
    if (!runtime?.readyFile || presentation.phase !== "ready") {
      return;
    }
    try {
      const result = await saveOrShareDeviceFile(runtime.readyFile, runtime.fileName);
      runtimeRef.current = null;
      await removeStoredRuntime(runtime);
      setPresentation(idlePresentation(result === "shared" ? "Shared." : "Download started."));
      notify(result === "shared" ? "Shared." : "Download started.", "success");
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        return;
      }
      setPresentation((current) => ({ ...current, message: "The recording could not be saved." }));
      notify("The recording could not be saved.", "error");
    }
  }

  async function discard() {
    const runtime = runtimeRef.current;
    runtimeRef.current = null;
    if (runtime) {
      await disposeRuntime(runtime);
    }
    setPresentation(idlePresentation("Recording discarded."));
  }

  function stop(message = "Recording ready to save or share.") {
    const runtime = runtimeRef.current;
    if (runtime && !runtime.discarding && !runtime.stopRequested && !runtime.finalizationStarted) {
      requestStop(runtime, message);
    }
  }

  function reportAudioUnavailable() {
    const runtime = runtimeRef.current;
    if (runtime?.includesSound && !runtime.discarding && !runtime.finalizationStarted) {
      setPresentation((current) => ({
        ...current,
        message: "Recording continues without further PC sound.",
      }));
    }
  }

  const stopForForegroundLoss = useEffectEvent(() => {
    const runtime = runtimeRef.current;
    if (runtime && presentation.phase === "recording") {
      requestStop(runtime, "Recording stopped when Voltura Air left the foreground.");
    }
  });

  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        stopForForegroundLoss();
      }
    };
    const onPageHide = () => {
      const runtime = runtimeRef.current;
      runtimeRef.current = null;
      if (runtime) {
        void disposeRuntime(runtime);
      }
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    window.addEventListener("pagehide", onPageHide);
    return () => {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("pagehide", onPageHide);
      const runtime = runtimeRef.current;
      runtimeRef.current = null;
      if (runtime) {
        void disposeRuntime(runtime);
      }
    };
  }, []);

  return {
    busy:
      presentation.phase === "recording" ||
      presentation.phase === "finalizing" ||
      presentation.phase === "ready",
    discard,
    lockSound: presentation.phase === "recording" || presentation.phase === "finalizing",
    presentation,
    reportAudioUnavailable,
    saveReadyFile,
    start,
    stop,
    supported,
    unsupportedReason,
  };
}
