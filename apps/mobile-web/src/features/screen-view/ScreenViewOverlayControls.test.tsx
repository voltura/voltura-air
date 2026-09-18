import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { AudioStateMessage, ClientMessage } from "../../foundation/protocol/messages";
import ScreenViewWorkspace from "./ScreenViewWorkspace";
import { ScreenViewVolumeControls } from "./ScreenViewVolumeControls";
import * as recordingModule from "./useScreenViewRecording";

const audioState: AudioStateMessage = { type: "audio.state", volume: 50, muted: false };
const props = {
  activePc: { customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" },
  browserPreviewState: "active" as const,
  capability: {
    enabled: true,
    permissionGranted: true,
    canView: true,
    requiresRepair: false,
    systemAudio: { codec: "opus" as const, sampleRate: 48_000 as const, channels: 2 as const },
    encrypted: true as const,
    maxWidth: 1920,
    maxHeight: 1080,
    maxFramesPerSecond: 30,
    screenshot: {
      transferPermissionGranted: true,
      format: "image/png" as const,
      maxPixels: 33_177_600,
      maxBytes: 67_108_864,
    },
    directPointer: { permissionGranted: true },
  },
  audioState,
  supportsVolumeControl: true,
  clientId: "preview-client",
  onBack: vi.fn(),
  onOpenKeyboard: vi.fn(),
  state: "paired" as const,
  trackpadSettings: defaultTrackpadSettings,
};

beforeEach(() => {
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  vi.stubGlobal("RTCPeerConnection", class {});
  vi.stubGlobal(
    "MediaStream",
    class {
      getTracks() {
        return [];
      }
    },
  );
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("Screen View overlay controls", () => {
  it("preserves later taps when earlier host responses arrive, including direction changes", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const { rerender } = render(
      <ScreenViewVolumeControls audioState={audioState} enabled visible send={send} />,
    );
    const up = screen.getByRole("button", { name: "Increase PC volume" });
    const down = screen.getByRole("button", { name: "Decrease PC volume" });
    const respond = (volume: number) =>
      rerender(
        <ScreenViewVolumeControls
          audioState={{ ...audioState, volume }}
          enabled
          visible
          send={send}
        />,
      );
    fireEvent.click(up);
    fireEvent.click(up);
    fireEvent.click(up);
    respond(55);
    fireEvent.click(up);
    respond(60);
    fireEvent.click(down);
    respond(65);
    fireEvent.click(down);
    respond(70);
    respond(65);
    respond(60);
    fireEvent.click(down);
    expect(send.mock.calls.map(([message]) => message)).toEqual(
      [55, 60, 65, 70, 65, 60, 55].map((volume) => ({
        type: "audio.volume.set",
        inputContext: "media-controls",
        volume,
      })),
    );
    // A host update unrelated to our pending commands is authoritative.
    respond(20);
    fireEvent.click(up);
    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ volume: 25 }));
    fireEvent.click(up);
    fireEvent.click(up);
    // React can render only the latest of several host responses.
    respond(35);
    fireEvent.click(down);
    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ volume: 30 }));
  });

  it("bounds pending commands and resets them when volume control becomes unavailable", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const { rerender } = render(
      <ScreenViewVolumeControls audioState={audioState} enabled visible send={send} />,
    );
    const up = screen.getByRole("button", { name: "Increase PC volume" });
    const down = screen.getByRole("button", { name: "Decrease PC volume" });
    for (let tap = 0; tap < 70; tap++) {
      fireEvent.click(tap % 2 === 0 ? up : down);
    }
    expect(send).toHaveBeenCalledTimes(64);
    rerender(
      <ScreenViewVolumeControls
        audioState={{ ...audioState, volume: 55 }}
        enabled
        visible
        send={send}
      />,
    );
    fireEvent.click(up);
    expect(send).toHaveBeenCalledTimes(65);
    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ volume: 55 }));
    rerender(
      <ScreenViewVolumeControls audioState={audioState} enabled={false} visible send={send} />,
    );
    rerender(<ScreenViewVolumeControls audioState={audioState} enabled visible send={send} />);
    fireEvent.click(up);
    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ volume: 55 }));
    rerender(<ScreenViewVolumeControls audioState={null} enabled visible send={send} />);
    rerender(<ScreenViewVolumeControls audioState={audioState} enabled visible send={send} />);
    fireEvent.click(up);
    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ volume: 55 }));
  });

  it("only sends volume commands on activation and accumulates rapid taps within bounds", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const { rerender } = render(
      <ScreenViewVolumeControls audioState={audioState} enabled visible send={send} />,
    );
    expect(send).not.toHaveBeenCalled();
    const up = screen.getByRole("button", { name: "Increase PC volume" });
    act(() => {
      up.click();
      up.click();
      up.click();
    });
    expect(send.mock.calls.map(([message]) => message)).toEqual(
      [55, 60, 65].map((volume) => ({
        type: "audio.volume.set",
        inputContext: "media-controls",
        volume,
      })),
    );
    rerender(
      <ScreenViewVolumeControls
        audioState={{ ...audioState, volume: 98 }}
        enabled
        visible
        send={send}
      />,
    );
    fireEvent.click(up);
    expect(send).toHaveBeenLastCalledWith({
      type: "audio.volume.set",
      inputContext: "media-controls",
      volume: 100,
    });
    rerender(
      <ScreenViewVolumeControls
        audioState={{ ...audioState, volume: 2 }}
        enabled
        visible
        send={send}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Decrease PC volume" }));
    expect(send).toHaveBeenLastCalledWith({
      type: "audio.volume.set",
      inputContext: "media-controls",
      volume: 0,
    });
    rerender(<ScreenViewVolumeControls audioState={null} enabled visible send={send} />);
    expect(up.hasAttribute("disabled")).toBe(true);
    send.mockClear();
    fireEvent.click(up);
    rerender(
      <ScreenViewVolumeControls audioState={audioState} enabled={false} visible send={send} />,
    );
    fireEvent.click(up);
    rerender(
      <ScreenViewVolumeControls audioState={audioState} enabled visible={false} send={send} />,
    );
    fireEvent.click(up);
    expect(send).not.toHaveBeenCalled();
  });

  it("gates volume on local sound and capability and isolates touch gestures", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const { rerender } = render(<ScreenViewWorkspace {...props} send={send} />);
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "screen.view.sources.get" }));
    send.mockClear();
    expect(screen.queryByRole("button", { name: "Increase PC volume" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Play PC sound" }));
    const up = await screen.findByRole("button", { name: "Increase PC volume" });
    fireEvent.touchStart(up, { touches: [{ identifier: 1, clientX: 10, clientY: 10 }] });
    fireEvent.touchEnd(up, { changedTouches: [{ identifier: 1, clientX: 10, clientY: 10 }] });
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(up);
    expect(send).toHaveBeenCalledOnce();
    rerender(<ScreenViewWorkspace {...props} supportsVolumeControl={false} send={send} />);
    expect(screen.queryByRole("button", { name: "Increase PC volume" })).toBeNull();
    rerender(<ScreenViewWorkspace {...props} send={send} />);
    fireEvent.click(screen.getByRole("button", { name: "Mute PC sound" }));
    expect(screen.queryByRole("button", { name: "Increase PC volume" })).toBeNull();
  });

  it("hides exactly the listed controls and retains visibility through fullscreen and rotation", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const view = render(<ScreenViewWorkspace {...props} send={send} />);
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "screen.view.sources.get" }));
    send.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Play PC sound" }));
    await screen.findByRole("button", { name: "Increase PC volume" });
    fireEvent.click(screen.getByRole("button", { name: /Two-finger mode: Zoom/ }));
    fireEvent.click(screen.getByRole("button", { name: "Hide controls" }));
    for (const name of [
      "Decrease PC volume",
      "Increase PC volume",
      "Mute PC sound",
      "Capture PC screenshot",
      "Start screen recording",
      /Two-finger mode/,
    ]) {
      expect(screen.queryByRole("button", { name })).toBeNull();
    }
    expect(screen.getByRole("button", { name: "Mouse and keyboard control" })).toBeTruthy();
    expect(document.querySelector("video")!.muted).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
    fireEvent(window, new Event("orientationchange"));
    expect(screen.getByRole("button", { name: "Show controls" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Exit full screen" }));
    expect(screen.queryByRole("button", { name: "Mute PC sound" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Show controls" }));
    expect(screen.getByRole("button", { name: /Two-finger mode: Scroll/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Mute PC sound" })).toBeTruthy();
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Hide controls" }));
    view.unmount();
    render(<ScreenViewWorkspace {...props} send={send} />);
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "screen.view.sources.get" }));
    send.mockClear();
    expect(screen.getByRole("button", { name: "Hide controls" })).toBeTruthy();
  });

  it("keeps recording and its progress panel running when controls are hidden", () => {
    const stop = vi.fn();
    const discard = vi.fn().mockResolvedValue(undefined);
    vi.spyOn(recordingModule, "useScreenViewRecording").mockReturnValue({
      busy: true,
      lockSound: true,
      supported: true,
      unsupportedReason: "",
      stop,
      discard,
      start: vi.fn().mockResolvedValue(undefined),
      saveReadyFile: vi.fn().mockResolvedValue(undefined),
      reportAudioUnavailable: vi.fn(),
      presentation: {
        phase: "recording",
        fileName: "recording.webm",
        message: "",
        elapsedMs: 12_000,
        includesSound: true,
      },
    });
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace {...props} send={send} />);
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "screen.view.sources.get" }));
    send.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Hide controls" }));
    expect(screen.queryByRole("button", { name: "Stop screen recording" })).toBeNull();
    expect(screen.getByText("recording.webm")).toBeTruthy();
    expect(screen.getByRole("progressbar")).toBeTruthy();
    expect(stop).not.toHaveBeenCalled();
    expect(discard).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Show controls" }));
    fireEvent.click(screen.getByRole("button", { name: "Stop screen recording" }));
    expect(stop).toHaveBeenCalledOnce();
  });
});
