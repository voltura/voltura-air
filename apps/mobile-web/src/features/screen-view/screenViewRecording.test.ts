import { afterEach, describe, expect, it, vi } from "vitest";
import {
  chooseScreenViewRecordingFormat,
  createScreenViewRecordingFileName,
} from "./screenViewRecording";

describe("Screen View recording format", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("prefers MP4 with H.264 and AAC when sound is included", () => {
    const isTypeSupported = vi.fn((type: string) => type.includes("avc1.42E01E,mp4a.40.2"));
    vi.stubGlobal("MediaRecorder", { isTypeSupported });

    expect(chooseScreenViewRecordingFormat(true)).toEqual({
      extension: "mp4",
      mimeType: "video/mp4;codecs=avc1.42E01E,mp4a.40.2",
    });
    expect(isTypeSupported).toHaveBeenCalledTimes(1);
  });

  it("falls back to WebM with VP8 and Opus", () => {
    vi.stubGlobal("MediaRecorder", {
      isTypeSupported: (type: string) => type === "video/webm;codecs=vp8,opus",
    });

    expect(chooseScreenViewRecordingFormat(true)).toEqual({
      extension: "webm",
      mimeType: "video/webm;codecs=vp8,opus",
    });
  });

  it("rejects browsers without a supported video-only format", () => {
    vi.stubGlobal("MediaRecorder", { isTypeSupported: () => false });
    expect(chooseScreenViewRecordingFormat(false)).toBeNull();
  });

  it("uses the actual recorder container in a local-time filename", () => {
    const capturedAt = new Date(2026, 8, 2, 7, 8, 9);

    expect(createScreenViewRecordingFileName(capturedAt, "video/mp4")).toBe(
      "Voltura Air - Screen recording - 2026-09-02 07-08-09.mp4",
    );
    expect(createScreenViewRecordingFileName(capturedAt, "video/webm;codecs=vp8")).toBe(
      "Voltura Air - Screen recording - 2026-09-02 07-08-09.webm",
    );
  });
});
