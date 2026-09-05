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

const offer =
  "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\n" +
  "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=sendonly\r\n" +
  "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\na=candidate:1 1 udp 1 192.0.2.1 50000 typ relay\r\n";

class Track extends EventTarget {
  muted = true;
  constructor(readonly kind: string) {
    super();
  }
  unmute() {
    this.muted = false;
    this.dispatchEvent(new Event("unmute"));
  }
}
class Stream {
  tracks: Track[] = [];
  addTrack(track: Track) {
    this.tracks.push(track);
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
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function openView(renewalSupported = true) {
  const host = createPairingKeyMaterial()!;
  const client = createPairingKeyMaterial()!;
  const send = vi.fn<(message: ClientMessage) => void>();
  const pcId = "https://example.test/d";
  localStorage.setItem(`voltura-air.reconnect-key.viewer.${pcId}`, client.privateKey);
  const view = render(
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
        ...(renewalSupported ? { relayRenewal: true } : {}),
      }}
      clientId="viewer"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />,
  );
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
      sources: [{ id: "display-1", label: "Display", width: 1920, height: 1080, isPrimary: true }],
    }),
  );
  const starts = () =>
    send.mock.calls
      .map(([message]) => message)
      .filter((message): message is ScreenViewStartMessage => message.type === "screen.view.start");
  const stops = () => send.mock.calls.filter(([message]) => message.type === "screen.view.stop");
  async function accept(request = starts().at(-1)!, validSignature = true) {
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
          succeeded: true,
          code: "accepted",
          message: "Ready",
        }),
      );
    }
  }
  await accept();
  const original = Peer.instances[0]!;
  act(() => {
    original.connect();
    original.video.unmute();
    original.audio.unmute();
  });
  const video = screen.getByLabelText("Mirrored PC display video") as HTMLVideoElement;
  fireEvent.loadedData(video);
  return { ...view, send, starts, stops, accept, original, video };
}
async function advance(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

describe("Relay screen renewal", () => {
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

  it("renews before starting a recording that could overlap credential expiry", async () => {
    const view = await openView();
    await advance(9 * 60_000);
    fireEvent.click(screen.getByRole("button", { name: "Start screen recording" }));
    expect(recording.start).not.toHaveBeenCalled();
    expect(view.starts().at(-1)!.renewalOf).toBe(view.starts()[0]!.operationId);
    await view.accept();
    act(() => {
      const peer = Peer.instances.at(-1)!;
      peer.connect();
      peer.video.unmute();
    });
    expect(recording.start).toHaveBeenCalledWith(view.video.srcObject, false);
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
