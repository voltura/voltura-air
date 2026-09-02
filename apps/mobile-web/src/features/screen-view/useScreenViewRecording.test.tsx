import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  screenViewRecordingMaximumBytes,
  screenViewRecordingMaximumDurationMs,
  screenViewRecordingMaximumQueuedBytes,
} from "./screenViewRecording";

const storage = vi.hoisted(() => {
  const parts: Blob[] = [];
  const writable = {
    abort: vi.fn(() => Promise.resolve()),
    close: vi.fn(() => Promise.resolve()),
    write: vi.fn((chunk: { data: Blob }) => {
      parts.push(chunk.data);
      return Promise.resolve();
    }),
  };
  const handle = {
    getFile: vi.fn(() => Promise.resolve(new File([...parts], "recording.partial"))),
  };
  const directory = {};
  const prepared = { directory, handle, storedName: "recording.partial", writable };
  return {
    directory,
    handle,
    parts,
    prepared,
    prepare: vi.fn(() => Promise.resolve(prepared)),
    remove: vi.fn(() => Promise.resolve()),
    save: vi.fn<() => Promise<"shared" | "download-started">>(() =>
      Promise.resolve("download-started"),
    ),
    writable,
  };
});

vi.mock("../../foundation/file-transfer/fileTransferDeviceStorage", () => ({
  prepareDeviceFileStorage: storage.prepare,
  removeDeviceFile: storage.remove,
  saveOrShareDeviceFile: storage.save,
  supportsDeviceFileStorage: () => true,
}));

import { useScreenViewRecording } from "./useScreenViewRecording";

class FakeTrack extends EventTarget {
  readonly clones: FakeTrack[] = [];
  readyState: MediaStreamTrackState = "live";
  stop = vi.fn(() => {
    this.readyState = "ended";
  });
  clone = vi.fn(() => {
    const clone = new FakeTrack(this.kind);
    this.clones.push(clone);
    return clone as unknown as MediaStreamTrack;
  });

  constructor(readonly kind: "video" | "audio") {
    super();
  }

  end() {
    this.readyState = "ended";
    this.dispatchEvent(new Event("ended"));
  }
}

class FakeMediaStream {
  static constructError: Error | null = null;

  constructor(private readonly tracks: MediaStreamTrack[] = []) {
    if (FakeMediaStream.constructError) {
      throw FakeMediaStream.constructError;
    }
  }
  getTracks() {
    return [...this.tracks];
  }
  getVideoTracks() {
    return this.tracks.filter((track) => track.kind === "video");
  }
  getAudioTracks() {
    return this.tracks.filter((track) => track.kind === "audio");
  }
}

class FakeMediaRecorder extends EventTarget {
  static instances: FakeMediaRecorder[] = [];
  static constructError: Error | null = null;
  static startError: Error | null = null;
  static isTypeSupported = vi.fn((type: string) => type.startsWith("video/mp4"));

  state: RecordingState = "inactive";
  readonly mimeType: string;
  readonly start = vi.fn((timeslice?: number) => {
    void timeslice;
    if (FakeMediaRecorder.startError) {
      throw FakeMediaRecorder.startError;
    }
    this.state = "recording";
  });
  readonly stop = vi.fn(() => {
    if (this.state === "inactive") {
      return;
    }
    this.state = "inactive";
    queueMicrotask(() => this.dispatchEvent(new Event("stop")));
  });

  constructor(
    readonly stream: MediaStream,
    readonly options: MediaRecorderOptions = {},
  ) {
    super();
    if (FakeMediaRecorder.constructError) {
      throw FakeMediaRecorder.constructError;
    }
    this.mimeType = options.mimeType ?? "";
    FakeMediaRecorder.instances.push(this);
  }

  emitData(blob: Blob) {
    const event = new Event("dataavailable") as BlobEvent;
    Object.defineProperty(event, "data", { value: blob });
    this.dispatchEvent(event);
  }

  emitError() {
    this.dispatchEvent(new Event("error"));
  }
}

function sourceStream(includeAudio = false) {
  const video = new FakeTrack("video");
  const audio = new FakeTrack("audio");
  const stream = new FakeMediaStream([
    video as unknown as MediaStreamTrack,
    ...(includeAudio ? [audio as unknown as MediaStreamTrack] : []),
  ]) as unknown as MediaStream;
  return { audio, stream, video };
}

function oversizedBlob(size: number): Blob {
  const blob = new Blob();
  Object.defineProperty(blob, "size", { value: size });
  return blob;
}

describe("useScreenViewRecording", () => {
  beforeEach(() => {
    storage.parts.length = 0;
    storage.prepare.mockReset().mockResolvedValue(storage.prepared);
    storage.remove.mockReset().mockResolvedValue(undefined);
    storage.save.mockReset().mockResolvedValue("download-started");
    storage.writable.abort.mockReset().mockResolvedValue(undefined);
    storage.writable.close.mockReset().mockResolvedValue(undefined);
    storage.writable.write.mockReset().mockImplementation((chunk: { data: Blob }) => {
      storage.parts.push(chunk.data);
      return Promise.resolve();
    });
    storage.handle.getFile
      .mockReset()
      .mockImplementation(() => Promise.resolve(new File([...storage.parts], "recording.partial")));
    FakeMediaRecorder.instances = [];
    FakeMediaRecorder.constructError = null;
    FakeMediaRecorder.startError = null;
    FakeMediaStream.constructError = null;
    FakeMediaRecorder.isTypeSupported
      .mockReset()
      .mockImplementation((type: string) => type.startsWith("video/mp4"));
    vi.stubGlobal("MediaRecorder", FakeMediaRecorder as unknown as typeof MediaRecorder);
    vi.stubGlobal("MediaStream", FakeMediaStream as unknown as typeof MediaStream);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  it("records clean video-only chunks to OPFS and saves the finished file", async () => {
    const notice = vi.fn();
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording(notice));

    await act(async () => expect(await result.current.start(source.stream, false)).toBe(true));

    const recorder = FakeMediaRecorder.instances[0]!;
    expect(storage.prepare).toHaveBeenCalledWith(
      screenViewRecordingMaximumBytes,
      expect.stringMatching(/^screen-recording-/u),
      true,
    );
    expect(recorder.options).toMatchObject({
      mimeType: "video/mp4;codecs=avc1.42E01E",
      videoBitsPerSecond: 8_000_000,
    });
    expect(recorder.options.audioBitsPerSecond).toBeUndefined();
    expect(recorder.start).toHaveBeenCalledWith(1_000);
    expect(result.current.lockSound).toBe(true);
    expect(source.video.stop).not.toHaveBeenCalled();

    act(() => recorder.emitData(new Blob(["recorded-video"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());
    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("ready"));

    expect(source.video.stop).not.toHaveBeenCalled();
    expect(source.video.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(result.current.presentation.fileName).toMatch(/\.mp4$/u);
    await act(() => result.current.saveReadyFile());
    expect(storage.save).toHaveBeenCalledOnce();
    expect(storage.remove).toHaveBeenCalledWith(storage.directory, "recording.partial");
    expect(result.current.presentation.phase).toBe("idle");
    unmount();
  });

  it("includes cloned audio only when Sound was enabled at start", async () => {
    const source = sourceStream(true);
    const { result, unmount } = renderHook(() => useScreenViewRecording());

    await act(async () => expect(await result.current.start(source.stream, true)).toBe(true));

    const recorder = FakeMediaRecorder.instances[0]!;
    expect(recorder.options.audioBitsPerSecond).toBe(96_000);
    expect((recorder.stream as unknown as FakeMediaStream).getAudioTracks()).toHaveLength(1);
    expect(result.current.presentation.includesSound).toBe(true);
    expect(source.audio.stop).not.toHaveBeenCalled();
    await act(() => result.current.discard());
    expect(source.audio.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(source.audio.stop).not.toHaveBeenCalled();
    unmount();
  });

  it("refuses an enabled-sound recording when the live audio track is missing", async () => {
    const notice = vi.fn();
    const source = sourceStream();
    const { result } = renderHook(() => useScreenViewRecording(notice));

    await act(async () => expect(await result.current.start(source.stream, true)).toBe(false));

    expect(storage.prepare).not.toHaveBeenCalled();
    expect(result.current.presentation.phase).toBe("error");
    expect(notice).toHaveBeenCalledWith(expect.stringContaining("audio track"), "error");
  });

  it.each(["audio clone", "MediaStream construction"])(
    "releases every accumulated clone after a %s failure",
    async (boundary) => {
      const source = sourceStream(true);
      if (boundary === "audio clone") {
        source.audio.clone.mockImplementationOnce(() => {
          throw new Error("audio clone failed");
        });
      } else {
        FakeMediaStream.constructError = new Error("stream construction failed");
      }
      const { result } = renderHook(() => useScreenViewRecording());

      await act(async () => expect(await result.current.start(source.stream, true)).toBe(false));

      expect(source.video.clones[0]?.stop).toHaveBeenCalledOnce();
      if (boundary === "MediaStream construction") {
        expect(source.audio.clones[0]?.stop).toHaveBeenCalledOnce();
      }
      expect(source.video.stop).not.toHaveBeenCalled();
      expect(source.audio.stop).not.toHaveBeenCalled();
      expect(storage.prepare).not.toHaveBeenCalled();
    },
  );

  it("auto-stops at five minutes and when the app leaves the foreground", async () => {
    vi.useFakeTimers();
    const first = sourceStream();
    const firstHook = renderHook(() => useScreenViewRecording());
    await act(async () => void (await firstHook.result.current.start(first.stream, false)));
    const timedRecorder = FakeMediaRecorder.instances[0]!;

    await act(() => vi.advanceTimersByTime(screenViewRecordingMaximumDurationMs));
    expect(timedRecorder.stop).toHaveBeenCalledOnce();
    firstHook.unmount();

    vi.useRealTimers();
    const second = sourceStream();
    const secondHook = renderHook(() => useScreenViewRecording());
    await act(async () => void (await secondHook.result.current.start(second.stream, false)));
    const hiddenRecorder = FakeMediaRecorder.instances[1]!;
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    await act(() => document.dispatchEvent(new Event("visibilitychange")));
    expect(hiddenRecorder.stop).toHaveBeenCalledOnce();
    secondHook.unmount();
  });

  it("soft-stops before the hard storage ceiling and verifies the committed size", async () => {
    const committed = new File([], "recording.partial");
    Object.defineProperty(committed, "size", { value: 480 * 1024 * 1024 });
    storage.handle.getFile.mockResolvedValueOnce(committed);
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;

    for (let chunk = 1; chunk <= 30; chunk += 1) {
      act(() => recorder.emitData(oversizedBlob(16 * 1024 * 1024)));
      await waitFor(() => expect(storage.writable.write).toHaveBeenCalledTimes(chunk));
    }
    await waitFor(() => expect(recorder.stop).toHaveBeenCalledOnce());
    await waitFor(() => expect(result.current.presentation.phase).toBe("ready"));
    expect(result.current.presentation.message).toContain("device-storage limit");
    unmount();
  });

  it("bounds queued and total recording bytes before writing", async () => {
    for (const [size, message] of [
      [screenViewRecordingMaximumQueuedBytes + 1, "quickly enough"],
      [screenViewRecordingMaximumBytes + 1, "512 MiB"],
    ] as const) {
      const source = sourceStream();
      const notice = vi.fn();
      const hook = renderHook(() => useScreenViewRecording(notice));
      await act(async () => void (await hook.result.current.start(source.stream, false)));

      act(() => FakeMediaRecorder.instances.at(-1)!.emitData(oversizedBlob(size)));
      await waitFor(() => expect(hook.result.current.presentation.phase).toBe("error"));
      expect(notice).toHaveBeenCalledWith(expect.stringContaining(message), "error");
      expect(storage.writable.write).not.toHaveBeenCalled();
      hook.unmount();
      storage.remove.mockClear();
    }
  });

  it("serializes delayed writes at exact positions within the queued-byte limit", async () => {
    let finishFirstWrite: (() => void) | undefined;
    storage.writable.write
      .mockImplementationOnce(
        () =>
          new Promise<void>((resolve) => {
            finishFirstWrite = resolve;
          }),
      )
      .mockResolvedValueOnce(undefined);
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    const first = new Blob(["first"]);
    const second = new Blob(["second"]);

    act(() => {
      recorder.emitData(first);
      recorder.emitData(second);
    });

    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());
    expect(storage.writable.write.mock.calls[0]?.[0]).toMatchObject({
      position: 0,
      data: first,
    });
    finishFirstWrite?.();
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledTimes(2));
    expect(storage.writable.write.mock.calls[1]?.[0]).toMatchObject({
      position: first.size,
      data: second,
    });
    await act(() => result.current.discard());
    unmount();
  });

  it("cleans the partial and clones when MediaRecorder stop throws", async () => {
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    recorder.stop.mockImplementation(() => {
      throw new Error("stop failed");
    });

    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("error"));

    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(storage.remove).toHaveBeenCalledOnce();
    expect(source.video.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(source.video.stop).not.toHaveBeenCalled();
    unmount();
  });

  it("contains write and cleanup failures without stopping the source stream", async () => {
    storage.writable.write.mockRejectedValueOnce(new Error("write failed"));
    storage.remove.mockRejectedValueOnce(new Error("cleanup failed"));
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));

    act(() => FakeMediaRecorder.instances[0]!.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(result.current.presentation.phase).toBe("error"));

    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(storage.remove).toHaveBeenCalledOnce();
    expect(source.video.stop).not.toHaveBeenCalled();
    unmount();
  });

  it.each([
    ["close", () => storage.writable.close.mockRejectedValueOnce(new Error("close failed"))],
    ["commit", () => storage.handle.getFile.mockRejectedValueOnce(new Error("commit failed"))],
  ])("discards an incomplete recording after a %s failure", async (_name, arrange) => {
    arrange();
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    act(() => recorder.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());

    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("error"));

    expect(storage.remove).toHaveBeenCalledOnce();
    expect(result.current.busy).toBe(false);
    unmount();
  });

  it("rejects a committed file whose actual size does not match the written chunks", async () => {
    storage.handle.getFile.mockResolvedValueOnce(new File(["wrong-size"], "recording.partial"));
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    act(() => recorder.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());

    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("error"));

    expect(result.current.presentation.message).toContain("incomplete");
    expect(storage.remove).toHaveBeenCalledOnce();
    unmount();
  });

  it("contains recorder errors and finalizes when the cloned video track ends", async () => {
    const failedSource = sourceStream();
    const failed = renderHook(() => useScreenViewRecording());
    await act(async () => void (await failed.result.current.start(failedSource.stream, false)));
    act(() => FakeMediaRecorder.instances[0]!.emitError());
    await waitFor(() => expect(failed.result.current.presentation.phase).toBe("error"));
    expect(failedSource.video.stop).not.toHaveBeenCalled();
    failed.unmount();

    const endedSource = sourceStream();
    const ended = renderHook(() => useScreenViewRecording());
    await act(async () => void (await ended.result.current.start(endedSource.stream, false)));
    const recorder = FakeMediaRecorder.instances[1]!;
    act(() => recorder.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());
    act(() => endedSource.video.clones[0]!.end());
    await waitFor(() => expect(ended.result.current.presentation.phase).toBe("ready"));
    ended.unmount();
  });

  it("continues video after cloned audio ends and reports the loss", async () => {
    const notice = vi.fn();
    const source = sourceStream(true);
    const { result, unmount } = renderHook(() => useScreenViewRecording(notice));
    await act(async () => void (await result.current.start(source.stream, true)));

    act(() => source.audio.clones[0]!.end());

    expect(result.current.presentation.phase).toBe("recording");
    expect(result.current.presentation.message).toContain("without further PC sound");
    expect(notice).toHaveBeenCalledWith(
      expect.stringContaining("without further PC sound"),
      "neutral",
    );
    await act(() => result.current.discard());
    unmount();
  });

  it("keeps a completed file ready when native sharing is canceled", async () => {
    storage.save.mockRejectedValueOnce(new DOMException("Canceled", "AbortError"));
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    act(() => recorder.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());
    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("ready"));

    await act(() => result.current.saveReadyFile());

    expect(result.current.presentation.phase).toBe("ready");
    expect(storage.remove).not.toHaveBeenCalled();
    unmount();
  });

  it("reports a successful handoff truthfully when temporary-file removal must wait", async () => {
    storage.remove.mockRejectedValueOnce(new Error("cleanup failed"));
    const source = sourceStream();
    const { result, unmount } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, false)));
    const recorder = FakeMediaRecorder.instances[0]!;
    act(() => recorder.emitData(new Blob(["chunk"])));
    await waitFor(() => expect(storage.writable.write).toHaveBeenCalledOnce());
    act(() => result.current.stop());
    await waitFor(() => expect(result.current.presentation.phase).toBe("ready"));

    await act(() => result.current.saveReadyFile());

    expect(result.current.presentation.phase).toBe("idle");
    expect(result.current.presentation.message).toBe("Download started.");
    expect(storage.remove).toHaveBeenCalledOnce();
    unmount();
  });

  it("discards the partial file and cloned tracks on page exit", async () => {
    const source = sourceStream(true);
    const { result } = renderHook(() => useScreenViewRecording());
    await act(async () => void (await result.current.start(source.stream, true)));

    await act(() => window.dispatchEvent(new Event("pagehide")));
    await waitFor(() => expect(storage.remove).toHaveBeenCalledOnce());

    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(source.video.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(source.audio.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(source.video.stop).not.toHaveBeenCalled();
  });

  it("discards the partial file and cloned tracks on unmount", async () => {
    const source = sourceStream();
    const hook = renderHook(() => useScreenViewRecording());
    await act(async () => void (await hook.result.current.start(source.stream, false)));

    hook.unmount();

    await waitFor(() => expect(storage.remove).toHaveBeenCalledOnce());
    expect(source.video.clones[0]?.stop).toHaveBeenCalledOnce();
    expect(source.video.stop).not.toHaveBeenCalled();
  });

  it.each(["constructor", "start"])("cleans up when MediaRecorder %s fails", async (boundary) => {
    if (boundary === "constructor") {
      FakeMediaRecorder.constructError = new Error("constructor failed");
    } else {
      FakeMediaRecorder.startError = new Error("start failed");
    }
    const source = sourceStream();
    const { result } = renderHook(() => useScreenViewRecording());

    await act(async () => expect(await result.current.start(source.stream, false)).toBe(false));

    expect(result.current.presentation.phase).toBe("error");
    expect(storage.writable.abort).toHaveBeenCalledOnce();
    expect(storage.remove).toHaveBeenCalledOnce();
    expect(source.video.stop).not.toHaveBeenCalled();
  });
});
