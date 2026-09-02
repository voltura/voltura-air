import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { ScreenViewCapability } from "../../foundation/protocol/messages";
import ScreenViewWorkspace from "./ScreenViewWorkspace";

const transfer = vi.hoisted(() => ({
  cancel: vi.fn(),
  discardReadyFile: vi.fn(() => Promise.resolve()),
  presentation: {
    active: false,
    fileName: "Voltura Air - Display 1.png",
    message: "File ready to save.",
    needsReplacementName: false,
    progress: 1,
    readyToSave: true,
  },
  retryUploadName: vi.fn(),
  saveReadyFile: vi.fn(() => Promise.resolve()),
  startDownload: vi.fn(),
  startScreenCapture: vi.fn(),
  startUpload: vi.fn(),
}));
const useFileTransferMock = vi.hoisted(() =>
  vi.fn((..._arguments: [unknown, string, boolean, unknown, unknown?, unknown?]) => transfer),
);
const recording = vi.hoisted(() => ({
  busy: false,
  discard: vi.fn(() => Promise.resolve()),
  lockSound: false,
  presentation: {
    elapsedMs: 0,
    fileName: "",
    includesSound: false,
    message: "",
    phase: "idle" as "idle" | "recording" | "finalizing" | "ready" | "error",
  },
  reportAudioUnavailable: vi.fn(),
  saveReadyFile: vi.fn(() => Promise.resolve()),
  start: vi.fn(() => Promise.resolve(true)),
  stop: vi.fn(),
  supported: true,
  unsupportedReason: "",
}));

vi.mock("../../foundation/file-transfer/fileTransferDeviceStorage", () => ({
  supportsDeviceFileStorage: () => true,
}));

vi.mock("../../foundation/file-transfer/useFileTransfer", () => ({
  useFileTransfer: useFileTransferMock,
}));

vi.mock("./useScreenViewRecording", () => ({
  useScreenViewRecording: () => recording,
}));

describe("Screen View screenshot controls", () => {
  beforeEach(() => {
    transfer.presentation.active = false;
    transfer.presentation.readyToSave = true;
    recording.busy = false;
    recording.lockSound = false;
    recording.presentation.phase = "idle";
    vi.stubGlobal("RTCPeerConnection", class {});
    vi.stubGlobal(
      "MediaRecorder",
      class {
        static isTypeSupported() {
          return true;
        }
      },
    );
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it("keeps Save or Share and Discard taps out of the live screen gesture surface", () => {
    render(
      <ScreenViewWorkspace
        activePc={{ customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" }}
        browserPreviewState="active"
        capability={{
          enabled: true,
          permissionGranted: true,
          canView: true,
          requiresRepair: false,
          encrypted: true,
          maxWidth: 1920,
          maxHeight: 1080,
          maxFramesPerSecond: 30,
          systemAudio: { codec: "opus", sampleRate: 48_000, channels: 2 },
          screenshot: {
            transferPermissionGranted: true,
            format: "image/png",
            maxPixels: 33_177_600,
            maxBytes: 67_108_864,
          },
        }}
        clientId="preview-client"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    const save = screen.getByRole("button", { name: "Save / Share" });
    const camera = screen.getByRole("button", { name: "Capture PC screenshot" });
    const record = screen.getByRole("button", { name: "Start screen recording" });
    expect(camera.getAttribute("title")).toContain("screenshot");
    expect(record.hasAttribute("disabled")).toBe(true);
    expect(record.getAttribute("title")).toContain("screenshot");
    expect(fireEvent.touchStart(save, { targetTouches: [{ identifier: 1 }] })).toBe(true);
    expect(fireEvent.touchEnd(save, { targetTouches: [] })).toBe(true);
    fireEvent.click(save);
    expect(transfer.saveReadyFile).toHaveBeenCalledOnce();

    const discard = screen.getByRole("button", { name: "Discard screenshot" });
    expect(fireEvent.touchStart(discard, { targetTouches: [{ identifier: 2 }] })).toBe(true);
    expect(fireEvent.touchEnd(discard, { targetTouches: [] })).toBe(true);
    fireEvent.click(discard);
    expect(transfer.discardReadyFile).toHaveBeenCalledOnce();
  });

  it("explains that a recording must finish before taking a screenshot", () => {
    transfer.presentation.readyToSave = false;
    recording.busy = true;
    recording.presentation.phase = "ready";

    render(
      <ScreenViewWorkspace
        activePc={{ customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" }}
        browserPreviewState="active"
        capability={{
          enabled: true,
          permissionGranted: true,
          canView: true,
          requiresRepair: false,
          encrypted: true,
          maxWidth: 1920,
          maxHeight: 1080,
          maxFramesPerSecond: 30,
          systemAudio: { codec: "opus", sampleRate: 48_000, channels: 2 },
          screenshot: {
            transferPermissionGranted: true,
            format: "image/png",
            maxPixels: 33_177_600,
            maxBytes: 67_108_864,
          },
        }}
        clientId="preview-client"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    const camera = screen.getByRole("button", { name: "Capture PC screenshot" });
    expect(camera.hasAttribute("disabled")).toBe(true);
    expect(camera.getAttribute("title")).toContain("recording");
  });

  it("disables screenshot storage when Screen viewing permission is revoked", () => {
    const capability: ScreenViewCapability = {
      enabled: true,
      permissionGranted: true,
      canView: true,
      requiresRepair: false,
      encrypted: true,
      maxWidth: 1920,
      maxHeight: 1080,
      maxFramesPerSecond: 30,
      systemAudio: { codec: "opus", sampleRate: 48_000, channels: 2 },
      screenshot: {
        transferPermissionGranted: true,
        format: "image/png" as const,
        maxPixels: 33_177_600,
        maxBytes: 67_108_864,
      },
    };
    const props = {
      activePc: {
        customName: false,
        id: "preview",
        name: "PC",
        url: "http://127.0.0.1",
      },
      browserPreviewState: "active" as const,
      clientId: "preview-client",
      onBack: vi.fn(),
      onOpenKeyboard: vi.fn(),
      send: vi.fn(),
      state: "paired" as const,
      trackpadSettings: defaultTrackpadSettings,
    };
    const view = render(<ScreenViewWorkspace {...props} capability={capability} />);

    expect(useFileTransferMock.mock.calls.at(-1)?.[2]).toBe(true);

    view.rerender(
      <ScreenViewWorkspace
        {...props}
        capability={{ ...capability, permissionGranted: false, canView: false }}
      />,
    );

    expect(useFileTransferMock.mock.calls.at(-1)?.[2]).toBe(false);
  });
});
