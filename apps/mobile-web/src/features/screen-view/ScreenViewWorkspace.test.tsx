import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPairingKeyMaterial, signPrivateKeyPayload } from "../../foundation/connection/pairingCredentials";
import { publishScreenViewResult } from "../../foundation/connection/screenViewResultBus";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { ClientMessage, ScreenViewCapability } from "../../foundation/protocol/messages";
import { hashScreenSdp } from "./screenViewCrypto";
import ScreenViewWorkspace from "./ScreenViewWorkspace";

const capability: ScreenViewCapability = {
  enabled: true,
  permissionGranted: true,
  canView: true,
  requiresRepair: false,
  encrypted: true,
  maxWidth: 1920,
  maxHeight: 1080,
  maxFramesPerSecond: 30
};

beforeEach(() => {
  const items = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    get length() { return items.size; },
    clear: () => { items.clear(); },
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => { items.delete(key); },
    setItem: (key: string, value: string) => { items.set(key, String(value)); }
  } satisfies Storage);
  vi.stubGlobal("RTCPeerConnection", class {});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ScreenViewWorkspace", () => {
  it("opens and requests sources without requiring secure-context randomUUID", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const originalRandomUuid = crypto.randomUUID;
    Object.defineProperty(crypto, "randomUUID", { configurable: true, value: undefined });

    try {
      render(<ScreenViewWorkspace
        activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />);

      expect(screen.getByText("Live mirror")).toBeTruthy();
      await waitFor(() => expect(send).toHaveBeenCalled());
      const request = send.mock.calls[0]?.[0];
      expect(request?.type).toBe("screen.view.sources.get");
      if (request?.type !== "screen.view.sources.get") {
        throw new Error("Screen source request was not sent.");
      }
      expect(request.operationId).toMatch(/^[a-z0-9-]+$/);
    } finally {
      Object.defineProperty(crypto, "randomUUID", { configurable: true, value: originalRandomUuid });
    }
  });

  it("starts mirroring automatically when the PC has one display", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const key = createPairingKeyMaterial();
    if (!key) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);

    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: pcId, name: "PC", url: pcId, hostIdentityPublicKey: key.reconnectPublicKey }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    await waitFor(() => expect(send).toHaveBeenCalled());
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: "sources",
        succeeded: true,
        message: "Displays are available.",
        sources: [{ id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true }]
      });
    });

    await waitFor(() => {
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.start")).toBe(true);
    });
    const startRequest = send.mock.calls.map(([message]) => message).find((message) => message.type === "screen.view.start");
    expect(startRequest?.type).toBe("screen.view.start");
    if (startRequest?.type !== "screen.view.start") {
      throw new Error("Screen start request was not sent.");
    }
    expect(startRequest.displayId).toBe("display-1");
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it("stops locally and ignores a delayed stop reply after starting again", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const key = createPairingKeyMaterial();
    if (!key) {throw new Error("Test key generation is unavailable.");}
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);

    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: pcId, name: "PC", url: pcId, hostIdentityPublicKey: key.reconnectPublicKey }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: "sources",
        succeeded: true,
        message: "Displays are available.",
        sources: [{ id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true }]
      });
    });
    const stopButton = await screen.findByRole("button", { name: "Stop" });
    fireEvent.click(stopButton);
    expect(screen.getByRole("status").textContent).toBe("Screen viewing stopped.");
    const stopRequests = send.mock.calls.map(([message]) => message).filter((message) => message.type === "screen.view.stop");
    const stopRequest = stopRequests[stopRequests.length - 1];
    if (stopRequest?.type !== "screen.view.stop") {throw new Error("Screen stop request was not sent.");}

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect(await screen.findByRole("button", { name: "Stop" })).toBeTruthy();
    act(() => {
      publishScreenViewResult({
        type: "screen.view.stop.result",
        operationId: stopRequest.operationId,
        succeeded: true,
        code: "stopped",
        message: "Screen viewing stopped."
      });
    });

    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it("clears stale video and input when the PC stops or disallows viewing", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    expect(screen.getByRole("button", { name: "Click" }).hasAttribute("disabled")).toBe(false);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {throw new Error("Screen stage was not rendered.");}
    Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
    Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
    expect(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" })).toBeTruthy();
    fireEvent.touchStart(stage, { targetTouches: [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 }
    ] });
    fireEvent.touchMove(stage, { targetTouches: [
      { identifier: 1, clientX: 70, clientY: 100 },
      { identifier: 2, clientX: 190, clientY: 100 }
    ] });
    fireEvent.touchEnd(stage, { targetTouches: [] });
    expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed");

    act(() => {
      publishScreenViewResult({
        type: "screen.view.ended",
        reason: "permission-revoked",
        message: "The PC stopped screen viewing and disallowed this device."
      });
    });

    expect(screen.getByText("Your PC display appears here")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("The PC stopped screen viewing and disallowed this device.");
    expect(screen.getByRole("button", { name: "Click" }).hasAttribute("disabled")).toBe(true);
    expect(screen.getByRole("button", { name: "Keys" }).hasAttribute("disabled")).toBe(true);
    expect(document.querySelector(".screen-view-content")?.classList).not.toContain("zoomed");
    expect(document.querySelector<HTMLElement>(".screen-view-content")?.style.transform).toBe("");
    expect(screen.queryByRole("button", { name: "Reset screen zoom" })).toBeNull();
  });

  it("offers a working user-gesture playback retry when autoplay is blocked", async () => {
    class FakePeerConnection {
      static instance: FakePeerConnection | null = null;
      readonly listeners = new Map<string, ((event: never) => void)[]>();
      readonly iceGatheringState: RTCIceGatheringState = "gathering";
      connectionState: RTCPeerConnectionState = "new";
      localDescription: RTCSessionDescriptionInit | null = null;
      remoteDescription: RTCSessionDescriptionInit | null = null;

      constructor() {FakePeerConnection.instance = this;}
      addEventListener(type: string, listener: (event: never) => void) {
        this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
      }
      removeEventListener(type: string, listener: (event: never) => void) {
        this.listeners.set(type, (this.listeners.get(type) ?? []).filter((entry) => entry !== listener));
      }
      setRemoteDescription(description: RTCSessionDescriptionInit) {
        this.remoteDescription = description;
        return Promise.resolve();
      }
      createAnswer(): Promise<RTCSessionDescriptionInit> {return Promise.resolve({ type: "answer", sdp: "answer-sdp" });}
      setLocalDescription(description: RTCSessionDescriptionInit) {
        this.localDescription = description;
        return Promise.resolve();
      }
      close() {this.connectionState = "closed";}
      emit(type: string, event: unknown) {
        if (type === "icecandidate" && this.localDescription && (event as RTCPeerConnectionIceEvent).candidate) {
          this.localDescription = {
            ...this.localDescription,
            sdp: `${this.localDescription.sdp ?? ""}\r\na=candidate:1 1 udp 1 192.0.2.1 50000 typ relay\r\n`
          };
        }
        for (const listener of this.listeners.get(type) ?? []) {listener(event as never);}
      }
    }

    vi.stubGlobal("RTCPeerConnection", FakePeerConnection as unknown as typeof RTCPeerConnection);
    let rejectStalePlayback: ((reason: DOMException) => void) | null = null;
    const play = vi.spyOn(HTMLMediaElement.prototype, "play")
      .mockRejectedValueOnce(new DOMException("Playback requires a gesture.", "NotAllowedError"))
      .mockResolvedValueOnce()
      .mockImplementationOnce(() => new Promise<void>((_resolve, reject) => {rejectStalePlayback = reject;}));
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "https://voltura.se/air/app/";
    const clientKey = createPairingKeyMaterial();
    const hostKey = createPairingKeyMaterial();
    if (!clientKey || !hostKey) {throw new Error("Test key generation is unavailable.");}
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, clientKey.privateKey);

    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: pcId, name: "PC", url: pcId, hostIdentityPublicKey: hostKey.reconnectPublicKey, transportMode: "relay" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: "sources",
        succeeded: true,
        message: "Displays are available.",
        sources: [{ id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true }]
      });
    });
    const startRequest = await waitFor(() => {
      const request = send.mock.calls.map(([message]) => message).find((message) => message.type === "screen.view.start");
      if (request?.type !== "screen.view.start") {throw new Error("Screen start request was not sent.");}
      return request;
    });
    const offerSdp = "offer-sdp";
    const offerHash = hashScreenSdp(offerSdp);
    const transcript = `VolturaAir screen-view:offer:v2:client-test:${startRequest.operationId}:display-1:${offerHash}`;
    const hostSignature = signPrivateKeyPayload(hostKey.privateKey, new TextEncoder().encode(transcript));
    act(() => {
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: startRequest.operationId,
        displayId: "display-1",
        succeeded: true,
        code: "accepted",
        message: "The encrypted WebRTC screen connection is ready.",
        offerSdp,
        hostSignature,
        iceServers: [{ urls: ["turns:turn.example.test:5349?transport=tcp"], username: "user", credential: "secret" }]
      });
    });
    await waitFor(() => expect(FakePeerConnection.instance?.localDescription).not.toBeNull());
    act(() => {
      FakePeerConnection.instance?.emit("icecandidate", {
        candidate: { type: "relay", candidate: "candidate:1 1 udp 1 192.0.2.1 50000 typ relay" }
      });
    });
    await waitFor(() => {
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.answer")).toBe(true);
    });
    expect(FakePeerConnection.instance?.iceGatheringState).toBe("gathering");
    const video = screen.getByLabelText("Mirrored PC display video") as HTMLVideoElement;
    Object.defineProperty(video, "srcObject", { configurable: true, writable: true, value: null });
    act(() => {
      FakePeerConnection.instance?.emit("track", { track: { kind: "video" }, streams: [{}] });
    });

    const showVideo = await screen.findByRole("button", { name: "Show video" });
    expect(screen.getByRole("status").textContent).toBe("Video is ready. Tap Show video to allow playback.");
    expect(screen.queryByText("Your PC display appears here")).toBeNull();
    fireEvent.click(showVideo);
    await waitFor(() => expect(play).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Show video" })).toBeNull());

    if (!FakePeerConnection.instance) {throw new Error("Fake peer connection was not created.");}
    FakePeerConnection.instance.connectionState = "disconnected";
    act(() => {FakePeerConnection.instance?.emit("connectionstatechange", {});});
    expect(screen.getByRole("status").textContent).toBe("Screen video interrupted. Reconnecting for up to 8 seconds...");
    expect(screen.getByText("Your PC display appears here")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    FakePeerConnection.instance.connectionState = "connected";
    act(() => {FakePeerConnection.instance?.emit("connectionstatechange", {});});
    expect(screen.getByRole("status").textContent).toBe("Live - Encrypted WebRTC");

    act(() => {
      FakePeerConnection.instance?.emit("track", { track: { kind: "video" }, streams: [{}] });
    });
    await waitFor(() => expect(play).toHaveBeenCalledTimes(3));
    FakePeerConnection.instance.connectionState = "failed";
    act(() => {FakePeerConnection.instance?.emit("connectionstatechange", {});});
    await act(async () => {
      rejectStalePlayback?.(new DOMException("The stream was closed."));
      await Promise.resolve();
    });
    expect(screen.queryByRole("button", { name: "Show video" })).toBeNull();
    expect(screen.getByText("Your PC display appears here")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Screen video connection was lost. Tap Start to reconnect.");
  });

  it("uses an in-app full-screen fallback and keeps an explicit exit control", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }));
    const fullScreenButton = screen.getByRole("button", { name: "View PC screen full screen" });
    fireEvent.touchStart(fullScreenButton, {
      targetTouches: [{ identifier: 1, clientX: 360, clientY: 80 }]
    });
    fireEvent.touchEnd(fullScreenButton, { targetTouches: [] });
    fireEvent.click(fullScreenButton);
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"));
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Exit full screen" }));
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).not.toContain("is-immersive"));
  });

  it("restores the workspace when native fullscreen ends without an orientation change", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const fullscreenDescriptor = Object.getOwnPropertyDescriptor(document, "fullscreenElement");
    let fullscreenElement: Element | null = null;
    Object.defineProperty(document, "fullscreenElement", {
      configurable: true,
      get: () => fullscreenElement
    });

    try {
      render(<ScreenViewWorkspace
        activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />);

      fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
      const workspace = document.querySelector<HTMLElement>(".screen-view-workspace");
      if (!workspace) {throw new Error("Screen workspace was not rendered.");}
      Object.defineProperty(workspace, "requestFullscreen", {
        configurable: true,
        value: vi.fn(() => {
          fullscreenElement = workspace;
          return Promise.resolve();
        })
      });

      fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
      await waitFor(() => expect(workspace.classList).toContain("is-immersive"));
      await waitFor(() => expect(fullscreenElement).toBe(workspace));

      fullscreenElement = null;
      act(() => {document.dispatchEvent(new Event("fullscreenchange"));});

      await waitFor(() => expect(workspace.classList).not.toContain("is-immersive"));
    } finally {
      if (fullscreenDescriptor) {
        Object.defineProperty(document, "fullscreenElement", fullscreenDescriptor);
      } else {
        Reflect.deleteProperty(document, "fullscreenElement");
      }
    }
  });

  it("keeps full screen active when rotating between portrait and landscape", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    vi.stubGlobal("innerWidth", 390);
    vi.stubGlobal("innerHeight", 844);
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"));
    vi.stubGlobal("innerWidth", 844);
    vi.stubGlobal("innerHeight", 390);
    act(() => {window.dispatchEvent(new Event("orientationchange"));});
    expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive");
  });

  it("uses an unzoomed two-finger drag for remote scrolling despite finger-spacing wobble", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
    />);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {throw new Error("Screen stage was not rendered.");}
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }));
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 }
    ];
    const moved = [
      { identifier: 1, clientX: 94, clientY: 125 },
      { identifier: 2, clientX: 166, clientY: 125 }
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: moved });

    await waitFor(() => expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true));
    expect(document.querySelector(".screen-view-content")?.classList).not.toContain("zoomed");
  });

  it("keeps two-finger movement local after the mirror is zoomed", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {throw new Error("Screen stage was not rendered.");}
    Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
    Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    expect(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" })).toBeTruthy();
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 }
    ];
    const spread = [
      { identifier: 1, clientX: 90, clientY: 100 },
      { identifier: 2, clientX: 170, clientY: 100 }
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: spread });
    fireEvent.touchEnd(stage, { targetTouches: [] });
    await waitFor(() => expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed"));
    send.mockClear();

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 }))
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await new Promise((resolve) => window.setTimeout(resolve, 20));
    expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(false);

    const zoomedContent = document.querySelector<HTMLElement>(".screen-view-content");
    const zoomedTransform = zoomedContent?.style.transform;
    fireEvent.click(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }));
    expect(zoomedContent?.classList).toContain("zoomed");
    expect(zoomedContent?.style.transform).toBe(zoomedTransform);
    expect(screen.getByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" })).toBeTruthy();

    send.mockClear();
    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 }))
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await waitFor(() => expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true));
  });
});
