import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createPairingKeyMaterial,
  signPrivateKeyPayload,
} from "../../foundation/connection/pairingCredentials";
import { publishScreenViewResult } from "../../foundation/connection/screenViewResultBus";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { ClientMessage, ScreenViewStartMessage } from "../../foundation/protocol/messages";
import { hashScreenSdp } from "./screenViewCrypto";
import ScreenViewWorkspace from "./ScreenViewWorkspace";

const recording = vi.hoisted(() => ({
  supported: true,
  unsupportedReason: "",
  busy: false,
  lockSound: false,
  presentation: { phase: "idle", fileName: "", message: "", elapsedMs: 0, includesSound: false },
  start: vi.fn(),
  stop: vi.fn(),
  reportAudioUnavailable: vi.fn(),
}));
vi.mock("./useScreenViewRecording", () => ({ useScreenViewRecording: () => recording }));
vi.mock(import("../../foundation/file-transfer/fileTransferDeviceStorage"), async (importOriginal) => ({
  ...(await importOriginal()),
  ensureDeviceFileStorageInitialized: () => Promise.resolve(),
  supportsDeviceFileStorage: () => true,
}));

const offer =
  "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\n" +
  "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=sendonly\r\n" +
  "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\na=candidate:1 1 udp 1 192.0.2.1 50000 typ relay\r\n";

class Track extends EventTarget {
  muted = true;
  readyState: MediaStreamTrackState = "live";
  constructor(readonly kind: string) {
    super();
  }
  unmute() {
    this.muted = false;
    this.dispatchEvent(new Event("unmute"));
  }
  end() {
    this.readyState = "ended";
    this.dispatchEvent(new Event("ended"));
  }
}
class Stream {
  tracks: Track[] = [];
  addTrack(track: Track) {
    this.tracks.push(track);
  }
  removeTrack(track: Track) {
    this.tracks = this.tracks.filter((existing) => existing !== track);
  }
  getTracks() {
    return this.tracks;
  }
  getVideoTracks() {
    return this.tracks.filter((track) => track.kind === "video");
  }
  getAudioTracks() {
    return this.tracks.filter((track) => track.kind === "audio");
  }
}
class Peer extends EventTarget {
  static instances: Peer[] = [];
  connectionState = "new";
  iceGatheringState = "complete";
  localDescription: RTCSessionDescriptionInit | null = null;
  video = new Track("video");
  audio = new Track("audio");
  close = vi.fn(() => {
    this.connectionState = "closed";
    this.dispatchEvent(new Event("connectionstatechange"));
  });
  constructor() {
    super();
    Peer.instances.push(this);
  }
  setRemoteDescription() {
    this.dispatchEvent(Object.assign(new Event("track"), { track: this.video }));
    this.dispatchEvent(Object.assign(new Event("track"), { track: this.audio }));
    return Promise.resolve();
  }
  createAnswer() {
    return Promise.resolve({ type: "answer", sdp: offer.replaceAll("sendonly", "recvonly") });
  }
  setLocalDescription(value: RTCSessionDescriptionInit) {
    this.localDescription = value;
    return Promise.resolve();
  }
  connect() {
    this.connectionState = "connected";
    this.dispatchEvent(new Event("connectionstatechange"));
  }
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-09-05T00:00:00Z"));
  Peer.instances = [];
  vi.stubGlobal("RTCPeerConnection", Peer);
  vi.stubGlobal("MediaStream", Stream);
  const storage = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  });
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  recording.start.mockClear();
  recording.stop.mockClear();
  recording.reportAudioUnavailable.mockClear();
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function openView(renewalSupported = true, connectOriginal = true, multiDisplay = false) {
  const host = createPairingKeyMaterial()!;
  const client = createPairingKeyMaterial()!;
  const send = vi.fn<(message: ClientMessage) => void>();
  const pcId = "https://example.test/d";
  localStorage.setItem(`voltura-air.reconnect-key.viewer.${pcId}`, client.privateKey);
  const workspace = (state: "connecting" | "paired", connectionEpoch: number) => (
    <ScreenViewWorkspace
      activePc={{
        id: pcId,
        url: pcId,
        name: "PC",
        customName: false,
        transportMode: "relay",
        hostIdentityPublicKey: host.reconnectPublicKey,
      }}
      capability={{
        enabled: true,
        canView: true,
        permissionGranted: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30,
        systemAudio: { codec: "opus", sampleRate: 48000, channels: 2 },
        screenshot: {
          transferPermissionGranted: true,
          format: "image/png",
          maxPixels: 33_177_600,
          maxBytes: 67_108_864,
        },
        ...(renewalSupported ? { relayRenewal: true } : {}),
      }}
      clientId="viewer"
      connectionEpoch={connectionEpoch}
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state={state}
      trackpadSettings={defaultTrackpadSettings}
    />
  );
  const view = render(workspace("paired", 1));
  const source = send.mock.calls.find(
    ([message]) => message.type === "screen.view.sources.get",
  )![0];
  if (source.type !== "screen.view.sources.get") {
    throw new Error("Missing sources request");
  }
  act(() =>
    publishScreenViewResult({
      type: "screen.view.sources.result",
      operationId: source.operationId,
      succeeded: true,
      message: "Ready",
      sources: [
        { id: "display-1", label: "Display", width: 1920, height: 1080, isPrimary: true },
        ...(multiDisplay
          ? [{ id: "display-2", label: "Side", width: 1280, height: 720, isPrimary: false }]
          : []),
      ],
    }),
  );
  if (multiDisplay) {
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
  }
  const starts = () =>
    send.mock.calls
      .map(([message]) => message)
      .filter((message): message is ScreenViewStartMessage => message.type === "screen.view.start");
  const stops = () => send.mock.calls.filter(([message]) => message.type === "screen.view.stop");
  async function accept(request = starts().at(-1)!, validSignature = true, answerSucceeded = true) {
    const transcript = `VolturaAir screen-view:offer:v2:viewer:${request.operationId}:${request.displayId}:${hashScreenSdp(offer)}${request.renewalOf && validSignature ? `:renew:${request.renewalOf}` : ""}`;
    await act(async () => {
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: request.operationId,
        displayId: request.displayId,
        succeeded: true,
        code: "accepted",
        message: "Ready",
        offerSdp: offer,
        hostSignature: signPrivateKeyPayload(host.privateKey, new TextEncoder().encode(transcript)),
        iceServers: [{ urls: ["turn:example.test:3478"], username: "test", credential: "test" }],
        turnExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
      });
      await Promise.resolve();
    });
    const answer = send.mock.calls
      .map(([message]) => message)
      .find(
        (message) =>
          message.type === "screen.view.answer" && message.operationId === request.operationId,
      );
    if (answer?.type === "screen.view.answer") {
      act(() =>
        publishScreenViewResult({
          type: "screen.view.answer.result",
          operationId: answer.operationId,
          succeeded: answerSucceeded,
          code: answerSucceeded ? "accepted" : "offer-expired",
          message: answerSucceeded ? "Ready" : "The screen offer expired.",
        }),
      );
    }
  }
  await accept();
  const original = Peer.instances[0]!;
  const video = screen.getByLabelText("Mirrored PC display video") as HTMLVideoElement;
  if (connectOriginal) {
    act(() => {
      original.connect();
      original.video.unmute();
      original.audio.unmute();
    });
    fireEvent.loadedData(video);
  }
  const setConnection = (state: "connecting" | "paired", connectionEpoch: number) =>
    view.rerender(workspace(state, connectionEpoch));
  return { ...view, send, starts, stops, accept, original, video, setConnection };
}
async function advance(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

function openScreenEvents(peer: Peer) {
  const channel = Object.assign(new EventTarget(), {
    label: "screen-events",
    binaryType: "blob",
    close: vi.fn(),
  });
  act(() => {
    peer.dispatchEvent(Object.assign(new Event("datachannel"), { channel }));
  });
  return (available: boolean, message: string) => {
    const text = new TextEncoder();
    const code = text.encode(available ? "audio-ready" : "audio-unavailable");
    const notice = text.encode(message);
    const record = new Uint8Array(5 + code.length + notice.length);
    record.set([7, available ? 1 : 0, code.length]);
    new DataView(record.buffer).setUint16(3, notice.length, false);
    record.set(code, 5);
    record.set(notice, 5 + code.length);
    act(() => {
      channel.dispatchEvent(new MessageEvent("message", { data: record.buffer }));
    });
  };
}

describe("Relay screen renewal", () => {
  it("ignores delayed media events while reconnecting and after the stream is detached", async () => {
    const view = await openView();
    act(() => {
      view.original.connectionState = "disconnected";
      view.original.dispatchEvent(new Event("connectionstatechange"));
    });
    fireEvent.loadedData(view.video);
    fireEvent.playing(view.video);
    expect(screen.getByRole("status").textContent).toContain("Reconnecting for up to 8 seconds");

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    fireEvent.loadedData(view.video);
    fireEvent.playing(view.video);
    expect(screen.getByRole("status").textContent).toBe("Screen viewing stopped.");
    expect(view.video.srcObject).toBeNull();
  });

  it("applies replacement audio availability and leaves current playback alone before promotion", async () => {
    const view = await openView();
    openScreenEvents(view.original)(true, "Original PC audio is ready.");
    await act(() => Promise.resolve(fireEvent.click(screen.getByRole("button", { name: "Play PC sound" }))));
    expect(view.video.muted).toBe(false);
    await advance(14 * 60_000);
    await view.accept();
    const replacement = Peer.instances.at(-1)!;
    const reportAudio = openScreenEvents(replacement);
    reportAudio(true, "Replacement PC audio was ready.");
    reportAudio(false, "Replacement PC audio is unavailable.");
    expect(view.video.muted).toBe(false);
    expect(screen.queryByText("Replacement PC audio is unavailable.")).toBeNull();

    const play = vi.mocked(HTMLMediaElement.prototype.play);
    play.mockClear();
    act(() => {
      replacement.dispatchEvent(Object.assign(new Event("track"), { track: replacement.video }));
    });
    expect(play).not.toHaveBeenCalled();
    await act(async () => {
      replacement.connect();
      replacement.video.unmute();
      await Promise.resolve();
    });
    expect(view.video.muted).toBe(true);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Play PC sound" }).disabled).toBe(true);
    expect(screen.queryByText("Replacement PC audio is unavailable.")).not.toBeNull();
    expect(recording.reportAudioUnavailable).toHaveBeenCalledOnce();
  });

  it("continues muted video when the browser refuses to resume sound on the replacement", async () => {
    const view = await openView();
    openScreenEvents(view.original)(true, "Original PC audio is ready.");
    await act(() => Promise.resolve(fireEvent.click(screen.getByRole("button", { name: "Play PC sound" }))));
    await advance(14 * 60_000);
    await view.accept();
    const replacement = Peer.instances.at(-1)!;
    const mutedDuringPlay: boolean[] = [];
    vi.mocked(HTMLMediaElement.prototype.play).mockImplementation(function (this: HTMLMediaElement) {
      mutedDuringPlay.push(this.muted);
      return this.muted ? Promise.resolve() : Promise.reject(new DOMException("Sound blocked", "NotAllowedError"));
    });

    await act(async () => {
      replacement.connect();
      replacement.video.unmute();
      await Promise.resolve();
    });
    expect(mutedDuringPlay).toEqual([false, true]);
    expect(view.video.muted).toBe(true);
    expect(screen.getByText("Tap Sound again to allow PC audio playback.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Show video" })).toBeNull();
  });

  it("disables Sound after the audio track ends and allows a new track without reloading video", async () => {
    const view = await openView();
    openScreenEvents(view.original)(true, "PC audio is ready.");
    const source = view.video.srcObject;
    await act(() => Promise.resolve(fireEvent.click(screen.getByRole("button", { name: "Play PC sound" }))));
    act(() => view.original.audio.end());
    expect(view.video.muted).toBe(true);
    expect(view.video.srcObject).toBe(source);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Play PC sound" }).disabled).toBe(true);
    expect(recording.reportAudioUnavailable).toHaveBeenCalledOnce();
    const replacement = new Track("audio");
    act(() => {
      view.original.dispatchEvent(Object.assign(new Event("track"), { track: replacement }));
    });
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Play PC sound" }).disabled).toBe(false);
    expect(view.video.srcObject).toBe(source);
  });

  it("adopts the replacement cursor shape only after the Relay peer can render video", async () => {
    const view = await openView();
    const cursorImage = view.container.querySelector<HTMLImageElement>(".screen-view-cursor")!;
    const pictureUrl = "blob:https://example.test/renewed-cursor";
    const createObjectURL = vi.fn(() => pictureUrl);
    vi.stubGlobal("URL", class extends URL {
      static createObjectURL = createObjectURL;
      static revokeObjectURL = vi.fn();
    });
    await advance(14 * 60_000);
    await view.accept();
    const replacement = Peer.instances.at(-1)!;
    const channel = Object.assign(new EventTarget(), { label: "screen-events", binaryType: "blob", close: vi.fn() });
    const cursorMessage = (x: number, png: number[] = []) => {
      const record = new Uint8Array(39 + png.length);
      const header = new DataView(record.buffer);
      record[0] = 4;
      record[9] = 1;
      record[34] = png.length ? 2 : 0;
      header.setInt32(10, x);
      header.setInt32(26, 4);
      header.setInt32(30, 4);
      header.setInt32(35, png.length);
      record.set(png, 39);
      return new MessageEvent("message", { data: record.buffer });
    };
    act(() => {
      replacement.dispatchEvent(Object.assign(new Event("datachannel"), { channel }));
      channel.dispatchEvent(cursorMessage(20, [137, 80, 78, 71]));
      channel.dispatchEvent(cursorMessage(24));
      replacement.connect();
    });
    expect(createObjectURL).not.toHaveBeenCalled();

    act(() => replacement.video.unmute());

    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(cursorImage.getAttribute("src")).toBe(pictureUrl);
  });

  it("expires an unanswered display list when the authenticated connection changes", async () => {
    const view = await openView(false);
    act(() => view.setConnection("paired", 2));
    const requests = view.send.mock.calls.map(([message]) => message).filter(
      (message) => message.type === "screen.view.sources.get",
    );
    expect(requests).toHaveLength(2);
    await advance(10_000);
    expect(screen.getByRole("status").textContent).toContain("did not return a display list");
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result", operationId: requests[1]!.operationId,
        succeeded: true, message: "Late", sources: [],
      });
    });
    expect(screen.getByRole("status").textContent).toContain("did not return a display list");
  });

  it("selects the current primary display if the previous display disappeared", async () => {
    const view = await openView(false);
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    act(() => view.setConnection("paired", 2));
    const request = view.send.mock.calls.map(([message]) => message).reverse().find(
      (message) => message.type === "screen.view.sources.get",
    )!;
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result", operationId: request.operationId,
        succeeded: true, message: "Ready", sources: [
          { id: "display-2", label: "Side", width: 1280, height: 720, isPrimary: false },
          { id: "display-3", label: "Current primary", width: 1920, height: 1080, isPrimary: true },
        ],
      });
    });
    expect(screen.getByRole<HTMLSelectElement>("combobox", { name: "Display" }).value).toBe("display-3");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect(view.starts().at(-1)?.displayId).toBe("display-3");
  });

  it("reports a failed transport send when requesting the display list", async () => {
    const view = await openView(false);
    view.send.mockImplementation((message) => message.type !== "screen.view.sources.get");
    await act(async () => {
      view.setConnection("paired", 2);
      await Promise.resolve();
    });
    expect(screen.getByRole("status").textContent).toContain("display list request could not be sent");
    await advance(10_000);
    expect(screen.getByRole("status").textContent).toContain("display list request could not be sent");
  });

  it.each(["screen.view.start", "screen.view.answer"] as const)(
    "ends local Screen View promptly if the transport refuses %s",
    async (messageType) => {
      const view = await openView(false);
      fireEvent.click(screen.getByRole("button", { name: "Stop" }));
      view.send.mockImplementation((message) => message.type !== messageType);
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      if (messageType === "screen.view.answer") {
        await view.accept();
        expect(Peer.instances.at(-1)?.close).toHaveBeenCalledOnce();
      }
      expect(view.video.srcObject).toBeNull();
      expect(screen.getByRole("status").textContent).toContain("could not be sent");
      expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
    },
  );

  it("keeps viewing the acknowledged display when a switch cannot be sent", async () => {
    const view = await openView(false, true, true);
    const selected = screen.getByRole<HTMLSelectElement>("combobox", { name: "Display" });
    view.send.mockImplementation((message) => message.type !== "screen.view.source.set");
    const stream = view.video.srcObject;
    fireEvent.change(selected, { target: { value: "display-2" } });

    expect(selected.value).toBe("display-1");
    expect(selected.disabled).toBe(false);
    expect(screen.getByRole("status").textContent).toContain("display change could not be sent");
    await advance(10_000);
    expect(view.video.srcObject).toBe(stream);
    expect(view.stops()).toHaveLength(0);
  });

  it("cancels the old screenshot on a display switch and waits for the new display", async () => {
    const view = await openView(false, true, true);
    const camera = screen.getByRole<HTMLButtonElement>("button", { name: "Capture PC screenshot" });
    const messages = () => view.send.mock.calls.map(([message]) => message);
    fireEvent.click(camera);
    const initial = messages().find((message) => message.type === "file.transfer.start");
    expect(initial).toMatchObject({ source: "screen-capture", displayId: "display-1" });

    fireEvent.change(screen.getByRole("combobox", { name: "Display" }), {
      target: { value: "display-2" },
    });
    const change = messages().find((message) => message.type === "screen.view.source.set")!;
    expect(messages()).toContainEqual(expect.objectContaining({
      type: "file.transfer.cancel", requestId: initial?.operationId,
    }));
    expect(camera.disabled).toBe(true);
    fireEvent.click(camera);
    expect(messages().filter((message) => message.type === "file.transfer.start")).toHaveLength(1);

    act(() => publishScreenViewResult({
      type: "screen.view.source.result",
      operationId: change.operationId,
      displayId: "display-2",
      succeeded: true,
      code: "ready",
      message: "Ready",
    }));
    expect(camera.disabled).toBe(false);
    fireEvent.click(camera);
    expect(messages().at(-1)).toMatchObject({
      type: "file.transfer.start", source: "screen-capture", displayId: "display-2",
    });
  });

  it("cancels an unfinished screenshot when the user stops the mirror", async () => {
    const view = await openView(false);
    fireEvent.click(screen.getByRole("button", { name: "Capture PC screenshot" }));
    const start = view.send.mock.calls.map(([message]) => message)
      .find((message) => message.type === "file.transfer.start");
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));

    expect(view.send.mock.calls.map(([message]) => message)).toContainEqual(expect.objectContaining({
      type: "file.transfer.cancel", requestId: start?.operationId,
    }));
    expect(screen.queryByRole("button", { name: "Cancel screenshot transfer" })).toBeNull();
  });

  it("reports an unconfirmed canceled capture immediately if the stop cannot be sent", async () => {
    const view = await openView(true, false);
    view.send.mockImplementation((message) => message.type !== "screen.view.stop");
    await advance(30_000);

    expect(screen.getByRole("status").textContent).toContain("Reconnect before trying again");
    expect(view.video.srcObject).toBeNull();
  });

  it("cancels an accepted initial screen offer if its browser peer never connects", async () => {
    const view = await openView(true, false);

    await advance(29_999);
    expect(view.stops()).toHaveLength(0);
    await advance(1);

    expect(view.stops()).toHaveLength(1);
    expect(view.original.close).toHaveBeenCalledOnce();
    expect(view.video.srcObject).toBeNull();
    expect(screen.getByRole("status").textContent).toContain("screen connection timed out");
    act(() => view.original.connect());
    expect(view.video.srcObject).toBeNull();
  });

  it("explains how to recover when the PC does not confirm a canceled capture", async () => {
    const view = await openView(true, false);
    await advance(30_000);
    const stop = view.stops()[0]?.[0];
    if (stop?.type !== "screen.view.stop") {
      throw new Error("The controller did not cancel the pending capture.");
    }

    await advance(10_000);

    expect(screen.getByRole("status").textContent).toContain("Reconnect before trying again");
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Start" }).disabled).toBe(true);
    act(() => publishScreenViewResult({
      type: "screen.view.stop.result",
      operationId: stop.operationId,
      succeeded: true,
      message: "Stopped",
    }));
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Start" }).disabled).toBe(false);
  });

  it.each([true, false])(
    "keeps a manually stopped mirror idle after reconnect and display refresh (renewal: %s)",
    async (renewalSupported) => {
      const view = await openView(renewalSupported);
      fireEvent.click(screen.getByRole("button", { name: "Stop" }));
      act(() => view.setConnection("connecting", 1));
      act(() => view.setConnection("paired", 2));
      const source = view.send.mock.calls
        .map(([message]) => message)
        .reverse()
        .find((message) => message.type === "screen.view.sources.get")!;
      if (source.type !== "screen.view.sources.get") {
        throw new Error("Missing refreshed sources request");
      }
      act(() =>
        publishScreenViewResult({
          type: "screen.view.sources.result",
          operationId: source.operationId,
          succeeded: true,
          message: "Ready",
          sources: [
            { id: "display-1", label: "Display", width: 1920, height: 1080, isPrimary: true },
          ],
        }),
      );
      await advance(15 * 60_000);

      expect(view.starts()).toHaveLength(1);
      expect(view.video.srcObject).toBeNull();
      expect(screen.getByText("Your PC display appears here")).toBeTruthy();
      expect(screen.getByRole("button", { name: "Start" }).hasAttribute("disabled")).toBe(false);

      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      expect(view.starts()).toHaveLength(2);
      await view.accept();
      act(() => {
        const peer = Peer.instances.at(-1)!;
        peer.connect();
        peer.video.unmute();
        fireEvent.loadedData(view.video);
      });
      expect(screen.getByText("Live - Encrypted WebRTC")).toBeTruthy();
    },
  );

  it("cancels a queued fresh recovery start when the user presses Stop", async () => {
    const view = await openView();
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));
    const renewal = view.starts().at(-1)!;
    act(() =>
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: renewal.operationId,
        displayId: renewal.displayId,
        succeeded: false,
        code: "renewal-unavailable",
        message: "The current screen connection cannot be renewed.",
      }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await advance(15 * 60_000);

    expect(view.starts()).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Screen viewing stopped.");
  });

  it("automatically renews the active mirror after the Relay control connection changes", async () => {
    const view = await openView();
    const originalOperation = view.starts()[0]!.operationId;
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
      await Promise.resolve();
    });
    view.video.muted = false;

    act(() => view.setConnection("connecting", 1));
    expect(view.original.close).toHaveBeenCalledOnce();
    expect(recording.stop).toHaveBeenCalledWith(
      "Screen viewing was interrupted. Recording is ready.",
    );
    expect(view.stops()).toHaveLength(0);

    act(() => view.setConnection("paired", 2));
    expect(view.starts().at(-1)!.renewalOf).toBe(originalOperation);
    await view.accept();
    const replacement = Peer.instances.at(-1)!;
    await act(async () => {
      replacement.connect();
      replacement.audio.unmute();
      replacement.video.unmute();
      fireEvent.loadedData(view.video);
      await Promise.resolve();
    });

    expect(view.video.muted).toBe(false);
    expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive");
    expect(screen.getByText("Live - Encrypted WebRTC")).toBeTruthy();
  });

  it("falls back once to a fresh start when the interrupted host session is already gone", async () => {
    const view = await openView();
    const originalOperation = view.starts()[0]!.operationId;
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));
    const renewal = view.starts().at(-1)!;
    expect(renewal.renewalOf).toBe(originalOperation);

    act(() =>
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: renewal.operationId,
        displayId: renewal.displayId,
        succeeded: false,
        code: "renewal-unavailable",
        message: "The current screen connection cannot be renewed.",
      }),
    );
    await advance(0);

    expect(view.starts()).toHaveLength(3);
    expect(view.starts().at(-1)!.renewalOf).toBeUndefined();
  });

  it("ends the previous host session when a recovery start is rejected", async () => {
    const view = await openView();
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));
    const renewal = view.starts().at(-1)!;

    act(() =>
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: renewal.operationId,
        displayId: renewal.displayId,
        succeeded: false,
        code: "turn-unavailable",
        message: "Relay screen viewing is temporarily unavailable.",
      }),
    );

    expect(view.stops()).toHaveLength(1);
    expect(screen.getByText("Relay screen viewing is temporarily unavailable.")).toBeTruthy();
  });

  it("ends the previous host session when a recovery start does not respond", async () => {
    const view = await openView();
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));

    await advance(25_000);

    expect(view.stops()).toHaveLength(1);
    expect(
      screen.getByText(
        "The PC did not respond while reconnecting Screen View. Ending the previous session...",
      ),
    ).toBeTruthy();
  });

  it("ends the previous host session when a recovery answer is rejected", async () => {
    const view = await openView();
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));

    await view.accept(undefined, true, false);

    expect(view.stops()).toHaveLength(1);
    expect(Peer.instances.at(-1)!.close).toHaveBeenCalledOnce();
    expect(
      screen.getByText(
        "The screen connection could not be restored. Ending the previous session...",
      ),
    ).toBeTruthy();
  });

  it("ends the previous host session when a recovery peer does not become ready", async () => {
    const view = await openView();
    act(() => view.setConnection("connecting", 1));
    act(() => view.setConnection("paired", 2));
    await view.accept();

    await advance(30_000);

    expect(view.stops()).toHaveLength(1);
    expect(Peer.instances.at(-1)!.close).toHaveBeenCalledOnce();
    expect(
      screen.getByText(
        "The screen connection could not be restored. Ending the previous session...",
      ),
    ).toBeTruthy();
  });

  it("keeps the live view, sound and fullscreen while replacing only a ready connection, repeatedly", async () => {
    const view = await openView();
    const originalOperation = view.starts()[0]!.operationId;
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
      await Promise.resolve();
    });
    view.video.muted = false;
    const stoppedRecordings = recording.stop.mock.calls.length;
    for (let index = 0; index < 2; index++) {
      const oldStream = view.video.srcObject;
      const oldPeer = Peer.instances.at(-1)!;
      await advance(14 * 60_000);
      expect(view.starts().at(-1)!.renewalOf).toBe(originalOperation);
      expect(view.stops()).toHaveLength(0);
      await view.accept();
      const next = Peer.instances.at(-1)!;
      act(() => {
        next.connect();
        next.audio.unmute();
      });
      expect(view.video.srcObject).toBe(oldStream);
      expect(oldPeer.close).not.toHaveBeenCalled();
      act(() => next.video.unmute());
      expect(view.video.srcObject).not.toBe(oldStream);
      expect(oldPeer.close).toHaveBeenCalledOnce();
      expect(view.video.muted).toBe(false);
      expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive");
      expect(screen.queryByText("Your PC display appears here")).toBeNull();
      expect(recording.stop.mock.calls.length).toBe(stoppedRecordings);
    }
    expect(view.stops()).toHaveLength(0);
    view.unmount();
    expect(Peer.instances.at(-1)!.close).toHaveBeenCalledOnce();
  });

  it.each(["signature", "connection", "timeout"])(
    "keeps the old stream on %s failure, with bounded expiry recovery",
    async (failure) => {
      const view = await openView();
      const oldStream = view.video.srcObject;
      await advance(14 * 60_000);
      await view.accept(undefined, failure !== "signature");
      if (failure === "connection") {
        act(() => {
          const peer = Peer.instances.at(-1)!;
          peer.connectionState = "failed";
          peer.dispatchEvent(new Event("connectionstatechange"));
        });
      }
      await advance(30_000);
      expect(view.stops()).toHaveLength(0);
      expect(view.video.srcObject).toBe(oldStream);
      expect(view.original.close).not.toHaveBeenCalled();
      await advance(25_000);
      expect(view.stops()).toHaveLength(1);
      await advance(250);
      expect(view.starts().at(-1)!.renewalOf).toBeUndefined();
    },
  );

  it("closes both connections on Stop and ignores late candidate tracks", async () => {
    const view = await openView();
    await advance(14 * 60_000);
    await view.accept();
    const candidate = Peer.instances.at(-1)!;
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    act(() => {
      candidate.connect();
      candidate.video.unmute();
    });
    expect(view.video.srcObject).toBeNull();
    expect(candidate.close).toHaveBeenCalledOnce();
    expect(view.original.close).toHaveBeenCalledOnce();
    const calls = view.send.mock.calls.length;
    await advance(15 * 60_000);
    expect(view.send.mock.calls).toHaveLength(calls);
  });

  it("keeps the current stream when the renewal stream cannot be constructed", async () => {
    const view = await openView();
    const currentStream = view.video.srcObject;
    await advance(14 * 60_000);
    vi.stubGlobal("MediaStream", class {
      constructor() {
        throw new Error("The browser could not create a stream");
      }
    });
    await view.accept();
    expect(Peer.instances).toEqual([view.original]);
    expect(view.original.close).not.toHaveBeenCalled();
    expect(view.video.srcObject).toBe(currentStream);
  });

  it.each([false, true])("retires pending SDP when the viewer leaves (renewal: %s)", async (renewing) => {
    const existingView = renewing ? await openView() : undefined;
    if (existingView) {
      await advance(14 * 60_000);
    }
    let finishDescription!: () => void;
    const pendingDescription = new Promise<void>((resolve) => {
      finishDescription = resolve;
    });
    const setDescription = vi.spyOn(Peer.prototype, "setRemoteDescription").mockReturnValueOnce(pendingDescription);
    const createAnswer = vi.spyOn(Peer.prototype, "createAnswer");
    const view = existingView ?? (await openView(true, false));
    if (existingView) {
      await existingView.accept();
    }
    const operation = view.starts().at(-1)!;
    expect(setDescription).toHaveBeenCalledOnce();
    const candidate = Peer.instances.at(-1)!;
    view.unmount();
    expect(candidate.close).toHaveBeenCalledOnce();
    await act(() => {
      finishDescription();
      return pendingDescription;
    });
    expect(createAnswer).not.toHaveBeenCalled();
    expect(view.send).not.toHaveBeenCalledWith(expect.objectContaining({
      type: "screen.view.answer",
      operationId: operation.operationId,
    }));
  });

  it("releases the recording request when its Relay renewal fails before handoff", async () => {
    const view = await openView();
    const liveStream = view.video.srcObject;
    await advance(9 * 60_000);
    fireEvent.click(screen.getByRole("button", { name: "Start screen recording" }));
    const failed = view.starts().at(-1)!;
    act(() => publishScreenViewResult({
      type: "screen.view.start.result",
      operationId: failed.operationId,
      displayId: failed.displayId,
      succeeded: false,
      code: "renewal-unavailable",
      message: "The replacement was unavailable.",
    }));
    expect(screen.getByRole("status").textContent).toContain("could not renew for recording");
    expect(recording.start).not.toHaveBeenCalled();
    expect(view.original.close).not.toHaveBeenCalled();
    expect(view.video.srcObject).toBe(liveStream);
    fireEvent.click(screen.getByRole("button", { name: "Start screen recording" }));
    expect(view.starts().at(-1)!.operationId).not.toBe(failed.operationId);
  });

  it("renews before starting a recording that could overlap credential expiry", async () => {
    const view = await openView();
    await advance(9 * 60_000);
    fireEvent.click(screen.getByRole("button", { name: "Start screen recording" }));
    const camera = screen.getByRole<HTMLButtonElement>("button", { name: "Capture PC screenshot" });
    const record = screen.getByRole<HTMLButtonElement>("button", { name: "Start screen recording" });
    expect(camera.disabled).toBe(true);
    expect(record.disabled).toBe(true);
    fireEvent.click(camera);
    fireEvent.click(record);
    expect(view.send.mock.calls.some(([message]) => message.type === "file.transfer.start")).toBe(false);
    expect(view.starts()).toHaveLength(2);
    expect(recording.start).not.toHaveBeenCalled();
    expect(view.starts().at(-1)!.renewalOf).toBe(view.starts()[0]!.operationId);
    await view.accept();
    act(() => {
      const peer = Peer.instances.at(-1)!;
      peer.connect();
      peer.video.unmute();
    });
    expect(recording.start).toHaveBeenCalledWith(view.video.srcObject, false);
    expect(camera.disabled).toBe(false);
    await advance(5 * 60_000);
    expect(view.stops()).toHaveLength(0);
    expect(view.starts()).toHaveLength(2);
  });

  it("uses the existing restart for a host without renewal capability", async () => {
    const view = await openView(false);
    await advance(14 * 60_000 + 250);
    expect(view.stops()).toHaveLength(1);
    expect(view.starts()).toHaveLength(2);
    expect(view.starts()[1]!.renewalOf).toBeUndefined();
  });
});
