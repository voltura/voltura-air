import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createPairingKeyMaterial,
  signPrivateKeyPayload,
} from "../../foundation/connection/pairingCredentials";
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
  maxFramesPerSecond: 30,
};

beforeEach(() => {
  const items = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    get length() {
      return items.size;
    },
    clear: () => {
      items.clear();
    },
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => {
      items.delete(key);
    },
    setItem: (key: string, value: string) => {
      items.set(key, String(value));
    },
  } satisfies Storage);
  vi.stubGlobal("RTCPeerConnection", class {});
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function sourceRequestId(send: ReturnType<typeof vi.fn<(message: ClientMessage) => void>>): string {
  const request = [...send.mock.calls]
    .reverse()
    .find(([message]) => message.type === "screen.view.sources.get")?.[0];
  if (request?.type !== "screen.view.sources.get") {
    throw new Error("Screen source request was not sent.");
  }
  return request.operationId;
}

describe("ScreenViewWorkspace", () => {
  it("keeps the active pointer overlay in the StrictMode browser preview", () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(
        () =>
          ({
            matches: true,
            media: "(any-pointer: fine) and (any-hover: hover)",
            onchange: null,
            addEventListener: vi.fn(),
            removeEventListener: vi.fn(),
            addListener: vi.fn(),
            removeListener: vi.fn(),
            dispatchEvent: vi.fn(),
          }) satisfies MediaQueryList,
      ),
    );

    const { container } = render(
      <StrictMode>
        <ScreenViewWorkspace
          activePc={{ customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" }}
          browserPreviewState="active"
          capability={{ ...capability, directPointer: { permissionGranted: true } }}
          clientId="preview-client"
          onBack={vi.fn()}
          onOpenKeyboard={vi.fn()}
          send={vi.fn()}
          state="paired"
          trackpadSettings={defaultTrackpadSettings}
        />
      </StrictMode>,
    );

    expect(container.querySelector(".screen-view-direct-pointer.active")).not.toBeNull();
  });

  it("shows an accessible camera action only for a live capable view", () => {
    render(
      <ScreenViewWorkspace
        activePc={{ customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" }}
        browserPreviewState="active"
        capability={{
          ...capability,
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
    expect(camera).toBeTruthy();
    expect(camera.hasAttribute("disabled")).toBe(true);
    expect(camera.getAttribute("title")).toContain("Save or Share");
  });

  it("opens and requests sources without requiring secure-context randomUUID", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const originalRandomUuid = crypto.randomUUID;
    Object.defineProperty(crypto, "randomUUID", { configurable: true, value: undefined });

    try {
      render(
        <ScreenViewWorkspace
          activePc={{
            customName: false,
            id: "http://192.168.1.10:51396",
            name: "PC",
            url: "http://192.168.1.10:51396",
          }}
          capability={capability}
          clientId="client-test"
          onBack={vi.fn()}
          onOpenKeyboard={vi.fn()}
          send={send}
          state="paired"
          trackpadSettings={defaultTrackpadSettings}
        />,
      );

      expect(screen.getByText("Live mirror")).toBeTruthy();
      await waitFor(() => expect(send).toHaveBeenCalled());
      const request = send.mock.calls[0]?.[0];
      expect(request?.type).toBe("screen.view.sources.get");
      if (request?.type !== "screen.view.sources.get") {
        throw new Error("Screen source request was not sent.");
      }
      expect(request.operationId).toMatch(/^[a-z0-9-]+$/);
    } finally {
      Object.defineProperty(crypto, "randomUUID", {
        configurable: true,
        value: originalRandomUuid,
      });
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

    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: pcId,
          name: "PC",
          url: pcId,
          hostIdentityPublicKey: key.reconnectPublicKey,
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    await waitFor(() => expect(send).toHaveBeenCalled());
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      });
    });

    await waitFor(() => {
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.start")).toBe(true);
    });
    const startRequest = send.mock.calls
      .map(([message]) => message)
      .find((message) => message.type === "screen.view.start");
    expect(startRequest?.type).toBe("screen.view.start");
    if (startRequest?.type !== "screen.view.start") {
      throw new Error("Screen start request was not sent.");
    }
    expect(startRequest.displayId).toBe("display-1");
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it.each([
    ["secure-direct", 10_000],
    ["relay", 20_000],
  ] as const)(
    "allows the full %s preparation window before timing out",
    async (transportMode, hostPreparationWindowMs) => {
      vi.useFakeTimers();
      const send = vi.fn<(message: ClientMessage) => void>();
      const pcId = "http://192.168.1.10:51396";
      const key = createPairingKeyMaterial();
      if (!key) {
        throw new Error("Test key generation is unavailable.");
      }
      localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);
      render(
        <ScreenViewWorkspace
          activePc={{
            customName: false,
            id: pcId,
            name: "PC",
            url: pcId,
            hostIdentityPublicKey: key.reconnectPublicKey,
            transportMode,
          }}
          capability={capability}
          clientId="client-test"
          onBack={vi.fn()}
          onOpenKeyboard={vi.fn()}
          send={send}
          state="paired"
          trackpadSettings={defaultTrackpadSettings}
        />,
      );
      act(() => {
        publishScreenViewResult({
          type: "screen.view.sources.result",
          operationId: sourceRequestId(send),
          succeeded: true,
          message: "Displays are available.",
          sources: [
            { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
          ],
        });
      });

      await act(() => vi.advanceTimersByTime(hostPreparationWindowMs));
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.stop")).toBe(false);
      expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");

      await act(() => vi.advanceTimersByTime(5_000));

      const stopRequest = [...send.mock.calls]
        .reverse()
        .find(([message]) => message.type === "screen.view.stop")?.[0];
      if (stopRequest?.type !== "screen.view.stop") {
        throw new Error("Timed-out capture was not stopped.");
      }
      expect((screen.getByRole("button", { name: /Start/u }) as HTMLButtonElement).disabled).toBe(
        true,
      );
      act(() =>
        publishScreenViewResult({
          type: "screen.view.stop.result",
          operationId: stopRequest.operationId,
          succeeded: true,
          message: "Screen viewing stopped.",
        }),
      );
      expect((screen.getByRole("button", { name: /Start/u }) as HTMLButtonElement).disabled).toBe(
        false,
      );
    },
  );

  it("stops the host capture before allowing retry when a successful offer is rejected", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const key = createPairingKeyMaterial();
    if (!key) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: pcId,
          name: "PC",
          url: pcId,
          hostIdentityPublicKey: key.reconnectPublicKey,
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    act(() =>
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      }),
    );
    const startRequest = [...send.mock.calls]
      .reverse()
      .find(([message]) => message.type === "screen.view.start")?.[0];
    if (startRequest?.type !== "screen.view.start") {
      throw new Error("Screen start request was not sent.");
    }

    act(() =>
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: startRequest.operationId,
        succeeded: true,
        message: "Started.",
        displayId: "display-1",
        offerSdp: "m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 VP8/90000\r\n",
        hostSignature: "invalid",
      }),
    );

    const stopRequest = [...send.mock.calls]
      .reverse()
      .find(([message]) => message.type === "screen.view.stop")?.[0];
    if (stopRequest?.type !== "screen.view.stop") {
      throw new Error("Rejected host capture was not stopped.");
    }
    expect((screen.getByRole("button", { name: /Start/u }) as HTMLButtonElement).disabled).toBe(
      true,
    );
    act(() =>
      publishScreenViewResult({
        type: "screen.view.stop.result",
        operationId: stopRequest.operationId,
        succeeded: true,
        message: "Capture ended.",
      }),
    );
    expect((screen.getByRole("button", { name: /Start/u }) as HTMLButtonElement).disabled).toBe(
      false,
    );
  });

  it("keeps the acknowledged display and direct pointer target when a display switch fails", () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(
        () =>
          ({
            matches: true,
            media: "(any-pointer: fine) and (any-hover: hover)",
            onchange: null,
            addEventListener: vi.fn(),
            removeEventListener: vi.fn(),
            addListener: vi.fn(),
            removeListener: vi.fn(),
            dispatchEvent: vi.fn(),
          }) satisfies MediaQueryList,
      ),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        browserPreviewState="active"
        capability={{ ...capability, directPointer: { permissionGranted: true } }}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-a", label: "Main display", width: 1920, height: 1080, isPrimary: true },
          { id: "display-b", label: "Second display", width: 1920, height: 1080, isPrimary: false },
        ],
      });
    });

    const selector = screen.getByLabelText("Display") as HTMLSelectElement;
    expect(selector.value).toBe("display-a");
    fireEvent.change(selector, { target: { value: "display-b" } });
    const switchRequest = send.mock.calls
      .map(([message]) => message)
      .find((message) => message.type === "screen.view.source.set");
    if (switchRequest?.type !== "screen.view.source.set") {
      throw new Error("Screen source switch was not sent.");
    }
    expect(selector.value).toBe("display-a");

    act(() => {
      publishScreenViewResult({
        type: "screen.view.source.result",
        operationId: switchRequest.operationId,
        displayId: "display-b",
        succeeded: false,
        code: "capture-unavailable",
        message: "Windows desktop capture is unavailable.",
      });
    });

    expect(selector.value).toBe("display-a");
    expect(screen.getByRole("status").textContent).toBe("Windows desktop capture is unavailable.");
    fireEvent.click(screen.getByRole("button", { name: "Mouse and keyboard control" }));
    const surface = document.querySelector<HTMLElement>(".screen-view-direct-pointer");
    if (!surface) {
      throw new Error("Direct pointer surface was not rendered.");
    }
    Object.defineProperty(surface, "getBoundingClientRect", {
      configurable: true,
      value: () => ({
        left: 0,
        top: 0,
        right: 100,
        bottom: 100,
        width: 100,
        height: 100,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }),
    });
    fireEvent.mouseDown(surface, { button: 0, clientX: 25, clientY: 25 });
    expect(
      send.mock.calls
        .map(([message]) => message)
        .find(
          (message) =>
            message.type === "screen.pointer.button" &&
            message.displayId === "display-a" &&
            message.button === "left" &&
            message.action === "down",
        ),
    ).toBeTruthy();
  });

  it("stops locally and ignores a delayed stop reply after starting again", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const key = createPairingKeyMaterial();
    if (!key) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);

    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: pcId,
          name: "PC",
          url: pcId,
          hostIdentityPublicKey: key.reconnectPublicKey,
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      });
    });
    const stopButton = await screen.findByRole("button", { name: "Stop" });
    fireEvent.click(stopButton);
    expect(screen.getByRole("status").textContent).toBe("Screen viewing stopped.");
    const stopRequests = send.mock.calls
      .map(([message]) => message)
      .filter((message) => message.type === "screen.view.stop");
    const stopRequest = stopRequests[stopRequests.length - 1];
    if (stopRequest?.type !== "screen.view.stop") {
      throw new Error("Screen stop request was not sent.");
    }

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect(await screen.findByRole("button", { name: "Stop" })).toBeTruthy();
    act(() => {
      publishScreenViewResult({
        type: "screen.view.stop.result",
        operationId: stopRequest.operationId,
        succeeded: true,
        code: "stopped",
        message: "Screen viewing stopped.",
      });
    });

    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it("ignores a failed negotiation after that peer was stopped and a new start began", async () => {
    let rejectRemoteDescription: ((reason: Error) => void) | null = null;
    class FailingStalePeerConnection {
      readonly connectionState: RTCPeerConnectionState = "new";
      readonly iceGatheringState: RTCIceGatheringState = "new";
      localDescription: RTCSessionDescriptionInit | null = null;
      addEventListener() {
        return undefined;
      }
      setRemoteDescription() {
        return new Promise<void>((_resolve, reject) => {
          rejectRemoteDescription = reject;
        });
      }
      createAnswer(): Promise<RTCSessionDescriptionInit> {
        return Promise.resolve({ type: "answer" });
      }
      setLocalDescription(description: RTCSessionDescriptionInit) {
        this.localDescription = description;
        return Promise.resolve();
      }
      close() {
        return undefined;
      }
    }
    vi.stubGlobal(
      "RTCPeerConnection",
      FailingStalePeerConnection as unknown as typeof RTCPeerConnection,
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const clientKey = createPairingKeyMaterial();
    const hostKey = createPairingKeyMaterial();
    if (!clientKey || !hostKey) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, clientKey.privateKey);
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: pcId,
          name: "PC",
          url: pcId,
          hostIdentityPublicKey: hostKey.reconnectPublicKey,
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    act(() =>
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      }),
    );
    const firstStart = [...send.mock.calls]
      .reverse()
      .find(([message]) => message.type === "screen.view.start")?.[0];
    if (firstStart?.type !== "screen.view.start") {
      throw new Error("Screen start request was not sent.");
    }
    const offerSdp = "m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n";
    const offerHash = hashScreenSdp(offerSdp);
    const transcript = `VolturaAir screen-view:offer:v2:client-test:${firstStart.operationId}:display-1:${offerHash}`;
    const hostSignature = signPrivateKeyPayload(
      hostKey.privateKey,
      new TextEncoder().encode(transcript),
    );
    act(() =>
      publishScreenViewResult({
        type: "screen.view.start.result",
        operationId: firstStart.operationId,
        displayId: "display-1",
        succeeded: true,
        message: "Started.",
        offerSdp,
        hostSignature,
      }),
    );
    await waitFor(() => expect(rejectRemoteDescription).not.toBeNull());

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    const stopCount = send.mock.calls.filter(
      ([message]) => message.type === "screen.view.stop",
    ).length;
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await act(async () => {
      rejectRemoteDescription?.(new Error("The stopped peer failed."));
      await Promise.resolve();
    });

    expect(send.mock.calls.filter(([message]) => message.type === "screen.view.stop")).toHaveLength(
      stopCount,
    );
    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it("ignores a host-ended event that does not own the current view", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    expect(screen.getByRole("button", { name: "Click" }).hasAttribute("disabled")).toBe(false);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {
      throw new Error("Screen stage was not rendered.");
    }
    Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
    Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
    expect(
      screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }),
    ).toBeTruthy();
    fireEvent.touchStart(stage, {
      targetTouches: [
        { identifier: 1, clientX: 100, clientY: 100 },
        { identifier: 2, clientX: 160, clientY: 100 },
      ],
    });
    fireEvent.touchMove(stage, {
      targetTouches: [
        { identifier: 1, clientX: 70, clientY: 100 },
        { identifier: 2, clientX: 190, clientY: 100 },
      ],
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });
    expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed");

    act(() => {
      publishScreenViewResult({
        type: "screen.view.ended",
        operationId: "stale-operation",
        reason: "permission-revoked",
        message: "The PC stopped screen viewing and disallowed this device.",
      });
    });

    expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed");
    expect(screen.getByRole("button", { name: "Click" }).hasAttribute("disabled")).toBe(false);
  });

  it("offers a working user-gesture playback retry when autoplay is blocked", async () => {
    class FakePeerConnection {
      static instance: FakePeerConnection | null = null;
      readonly listeners = new Map<string, ((event: never) => void)[]>();
      readonly iceGatheringState: RTCIceGatheringState = "gathering";
      connectionState: RTCPeerConnectionState = "new";
      localDescription: RTCSessionDescriptionInit | null = null;
      remoteDescription: RTCSessionDescriptionInit | null = null;

      constructor() {
        FakePeerConnection.instance = this;
      }
      addEventListener(type: string, listener: (event: never) => void) {
        this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
      }
      removeEventListener(type: string, listener: (event: never) => void) {
        this.listeners.set(
          type,
          (this.listeners.get(type) ?? []).filter((entry) => entry !== listener),
        );
      }
      setRemoteDescription(description: RTCSessionDescriptionInit) {
        this.remoteDescription = description;
        return Promise.resolve();
      }
      createAnswer(): Promise<RTCSessionDescriptionInit> {
        return Promise.resolve({
          type: "answer",
          sdp: "m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n",
        });
      }
      setLocalDescription(description: RTCSessionDescriptionInit) {
        this.localDescription = description;
        return Promise.resolve();
      }
      close() {
        this.connectionState = "closed";
      }
      emit(type: string, event: unknown) {
        if (
          type === "icecandidate" &&
          this.localDescription &&
          (event as RTCPeerConnectionIceEvent).candidate
        ) {
          this.localDescription = {
            ...this.localDescription,
            sdp: `${this.localDescription.sdp ?? ""}\r\na=candidate:1 1 udp 1 192.0.2.1 50000 typ relay\r\n`,
          };
        }
        for (const listener of this.listeners.get(type) ?? []) {
          listener(event as never);
        }
      }
    }

    vi.stubGlobal("RTCPeerConnection", FakePeerConnection as unknown as typeof RTCPeerConnection);
    let rejectStalePlayback: ((reason: DOMException) => void) | null = null;
    const play = vi
      .spyOn(HTMLMediaElement.prototype, "play")
      .mockRejectedValueOnce(new DOMException("Playback requires a gesture.", "NotAllowedError"))
      .mockResolvedValueOnce()
      .mockImplementationOnce(
        () =>
          new Promise<void>((_resolve, reject) => {
            rejectStalePlayback = reject;
          }),
      );
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "https://voltura.se/air/app/";
    const clientKey = createPairingKeyMaterial();
    const hostKey = createPairingKeyMaterial();
    if (!clientKey || !hostKey) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, clientKey.privateKey);

    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: pcId,
          name: "PC",
          url: pcId,
          hostIdentityPublicKey: hostKey.reconnectPublicKey,
          transportMode: "relay",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: sourceRequestId(send),
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      });
    });
    const startRequest = await waitFor(() => {
      const request = send.mock.calls
        .map(([message]) => message)
        .find((message) => message.type === "screen.view.start");
      if (request?.type !== "screen.view.start") {
        throw new Error("Screen start request was not sent.");
      }
      return request;
    });
    const offerSdp = "m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n";
    const offerHash = hashScreenSdp(offerSdp);
    const transcript = `VolturaAir screen-view:offer:v2:client-test:${startRequest.operationId}:display-1:${offerHash}`;
    const hostSignature = signPrivateKeyPayload(
      hostKey.privateKey,
      new TextEncoder().encode(transcript),
    );
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
        iceServers: [
          {
            urls: ["turns:turn.example.test:5349?transport=tcp"],
            username: "user",
            credential: "secret",
          },
        ],
      });
    });
    await waitFor(() => expect(FakePeerConnection.instance?.localDescription).not.toBeNull());
    act(() => {
      FakePeerConnection.instance?.emit("icecandidate", {
        candidate: { type: "relay", candidate: "candidate:1 1 udp 1 192.0.2.1 50000 typ relay" },
      });
    });
    await waitFor(() => {
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.answer")).toBe(true);
    });
    expect(FakePeerConnection.instance?.iceGatheringState).toBe("gathering");
    let staleMessageListener: ((event: MessageEvent) => void) | null = null;
    const staleChannel = {
      label: "screen-events",
      binaryType: "blob",
      close: vi.fn(),
      addEventListener: (type: string, listener: (event: MessageEvent) => void) => {
        if (type === "message") {
          staleMessageListener = listener;
        }
      },
    };
    act(() => {
      FakePeerConnection.instance?.emit("datachannel", { channel: staleChannel });
    });
    const duplicateChannel = {
      label: "screen-events",
      close: vi.fn(),
      addEventListener: vi.fn(),
    };
    act(() => {
      FakePeerConnection.instance?.emit("datachannel", { channel: duplicateChannel });
    });
    expect(duplicateChannel.close).toHaveBeenCalledOnce();
    expect(duplicateChannel.addEventListener).not.toHaveBeenCalled();
    const video = screen.getByLabelText("Mirrored PC display video") as HTMLVideoElement;
    Object.defineProperty(video, "srcObject", { configurable: true, writable: true, value: null });
    act(() => {
      FakePeerConnection.instance?.emit("track", { track: { kind: "video" }, streams: [{}] });
    });

    const showVideo = await screen.findByRole("button", { name: "Show video" });
    expect(screen.getByRole("status").textContent).toBe(
      "Video is ready. Tap Show video to allow playback.",
    );
    expect(screen.queryByText("Your PC display appears here")).toBeNull();
    fireEvent.click(showVideo);
    await waitFor(() => expect(play).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Show video" })).toBeNull());

    if (!FakePeerConnection.instance) {
      throw new Error("Fake peer connection was not created.");
    }
    FakePeerConnection.instance.connectionState = "disconnected";
    act(() => {
      FakePeerConnection.instance?.emit("connectionstatechange", {});
    });
    expect(screen.getByRole("status").textContent).toBe(
      "Screen video interrupted. Reconnecting for up to 8 seconds...",
    );
    expect(screen.getByText("Your PC display appears here")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    FakePeerConnection.instance.connectionState = "connected";
    act(() => {
      FakePeerConnection.instance?.emit("connectionstatechange", {});
    });
    expect(screen.getByRole("status").textContent).toBe("Live - Encrypted WebRTC");

    act(() => {
      FakePeerConnection.instance?.emit("track", { track: { kind: "video" }, streams: [{}] });
    });
    await waitFor(() => expect(play).toHaveBeenCalledTimes(3));
    FakePeerConnection.instance.connectionState = "failed";
    act(() => {
      FakePeerConnection.instance?.emit("connectionstatechange", {});
    });
    await act(async () => {
      rejectStalePlayback?.(new DOMException("The stream was closed."));
      await Promise.resolve();
    });
    expect(screen.queryByRole("button", { name: "Show video" })).toBeNull();
    expect(screen.getByText("Your PC display appears here")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe(
      "Screen video connection was lost. Tap Start to reconnect.",
    );
    act(() => {
      staleMessageListener?.(new MessageEvent("message", { data: "stale invalid record" }));
    });
    expect(screen.getByRole("status").textContent).toBe(
      "Screen video connection was lost. Tap Start to reconnect.",
    );
  });

  it("uses an in-app full-screen fallback and keeps an explicit exit control", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(
      screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }),
    );
    const fullScreenButton = screen.getByRole("button", { name: "View PC screen full screen" });
    fireEvent.touchStart(fullScreenButton, {
      targetTouches: [{ identifier: 1, clientX: 360, clientY: 80 }],
    });
    fireEvent.touchEnd(fullScreenButton, { targetTouches: [] });
    fireEvent.click(fullScreenButton);
    await waitFor(() =>
      expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"),
    );
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Exit full screen" }));
    await waitFor(() =>
      expect(document.querySelector(".screen-view-workspace")?.classList).not.toContain(
        "is-immersive",
      ),
    );
  });

  it("restores the workspace when native fullscreen ends without an orientation change", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const fullscreenDescriptor = Object.getOwnPropertyDescriptor(document, "fullscreenElement");
    let fullscreenElement: Element | null = null;
    Object.defineProperty(document, "fullscreenElement", {
      configurable: true,
      get: () => fullscreenElement,
    });

    try {
      render(
        <ScreenViewWorkspace
          activePc={{
            customName: false,
            id: "http://192.168.1.10:51396",
            name: "PC",
            url: "http://192.168.1.10:51396",
          }}
          capability={capability}
          clientId="client-test"
          onBack={vi.fn()}
          onOpenKeyboard={vi.fn()}
          send={send}
          state="paired"
          trackpadSettings={defaultTrackpadSettings}
        />,
      );

      fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
      const workspace = document.querySelector<HTMLElement>(".screen-view-workspace");
      if (!workspace) {
        throw new Error("Screen workspace was not rendered.");
      }
      Object.defineProperty(workspace, "requestFullscreen", {
        configurable: true,
        value: vi.fn(() => {
          fullscreenElement = workspace;
          return Promise.resolve();
        }),
      });

      fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
      await waitFor(() => expect(workspace.classList).toContain("is-immersive"));
      await waitFor(() => expect(fullscreenElement).toBe(workspace));

      fullscreenElement = null;
      act(() => {
        document.dispatchEvent(new Event("fullscreenchange"));
      });

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
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(screen.getByRole("button", { name: "View PC screen full screen" }));
    await waitFor(() =>
      expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"),
    );
    vi.stubGlobal("innerWidth", 844);
    vi.stubGlobal("innerHeight", 390);
    act(() => {
      window.dispatchEvent(new Event("orientationchange"));
    });
    expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive");
  });

  it("uses an unzoomed two-finger drag for remote scrolling despite finger-spacing wobble", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
      />,
    );
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {
      throw new Error("Screen stage was not rendered.");
    }
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    fireEvent.click(
      screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }),
    );
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 },
    ];
    const moved = [
      { identifier: 1, clientX: 94, clientY: 125 },
      { identifier: 2, clientX: 166, clientY: 125 },
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: moved });

    await waitFor(() =>
      expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true),
    );
    expect(document.querySelector(".screen-view-content")?.classList).not.toContain("zoomed");
  });

  it("keeps two-finger movement local after the mirror is zoomed", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {
      throw new Error("Screen stage was not rendered.");
    }
    Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
    Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    expect(
      screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }),
    ).toBeTruthy();
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 },
    ];
    const spread = [
      { identifier: 1, clientX: 90, clientY: 100 },
      { identifier: 2, clientX: 170, clientY: 100 },
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: spread });
    fireEvent.touchEnd(stage, { targetTouches: [] });
    await waitFor(() =>
      expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed"),
    );
    send.mockClear();

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 })),
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await new Promise((resolve) => window.setTimeout(resolve, 20));
    expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(false);

    const zoomedContent = document.querySelector<HTMLElement>(".screen-view-content");
    const zoomedTransform = zoomedContent?.style.transform;
    fireEvent.click(
      screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }),
    );
    expect(zoomedContent?.classList).toContain("zoomed");
    expect(zoomedContent?.style.transform).toBe(zoomedTransform);
    expect(
      screen.getByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" }),
    ).toBeTruthy();

    send.mockClear();
    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 })),
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await waitFor(() =>
      expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true),
    );
  });

  it("shows direct control only for a fine pointer and routes keyboard input while active", async () => {
    let matches = true;
    let onChange: EventListener | undefined;
    vi.stubGlobal(
      "matchMedia",
      vi.fn(
        () =>
          ({
            get matches() {
              return matches;
            },
            media: "(any-pointer: fine) and (any-hover: hover)",
            onchange: null,
            addEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
              if (typeof listener === "function") {
                onChange = listener;
              }
            },
            removeEventListener: vi.fn(),
            addListener: vi.fn(),
            removeListener: vi.fn(),
            dispatchEvent: vi.fn(),
          }) satisfies MediaQueryList,
      ),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={{ ...capability, directPointer: { permissionGranted: true } }}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: "direct-sources",
        succeeded: true,
        message: "Ready",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
        ],
      });
    });
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    const mouse = await screen.findByRole("button", { name: "Mouse and keyboard control" });
    fireEvent.click(mouse);
    expect(mouse.getAttribute("aria-pressed")).toBe("true");
    expect(
      await screen.findByText("Mouse and keyboard control the PC. Select this button to stop."),
    ).toBeTruthy();

    fireEvent.keyDown(window, { key: "a", code: "KeyA" });
    fireEvent.keyDown(window, { key: "Escape", code: "Escape" });
    fireEvent.keyDown(window, { key: "c", code: "KeyC", ctrlKey: true });
    expect(
      send.mock.calls
        .map(([message]) => message)
        .filter(
          (message) => message.type === "keyboard.text" || message.type === "keyboard.special",
        ),
    ).toEqual([
      { type: "keyboard.text", inputContext: "screen-view", text: "a" },
      { type: "keyboard.special", inputContext: "screen-view", key: "Escape" },
      { type: "keyboard.special", inputContext: "screen-view", key: "c", modifiers: ["Control"] },
    ]);
    expect(mouse.getAttribute("aria-pressed")).toBe("true");

    const keyboardMessageCount = send.mock.calls.filter(
      ([message]) => message.type === "keyboard.text" || message.type === "keyboard.special",
    ).length;
    fireEvent.click(mouse);
    expect(mouse.getAttribute("aria-pressed")).toBe("false");
    fireEvent.keyDown(window, { key: "b", code: "KeyB" });
    expect(
      send.mock.calls.filter(
        ([message]) => message.type === "keyboard.text" || message.type === "keyboard.special",
      ),
    ).toHaveLength(keyboardMessageCount);

    matches = false;
    act(() => onChange?.(new Event("change")));
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: "Mouse and keyboard control" })).toBeNull(),
    );
  });

  it("keeps supported direct mouse discoverable when its permission is blocked", () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(
        () =>
          ({
            matches: true,
            media: "(any-pointer: fine) and (any-hover: hover)",
            onchange: null,
            addEventListener: vi.fn(),
            removeEventListener: vi.fn(),
            addListener: vi.fn(),
            removeListener: vi.fn(),
            dispatchEvent: vi.fn(),
          }) satisfies MediaQueryList,
      ),
    );
    render(
      <ScreenViewWorkspace
        activePc={{
          customName: false,
          id: "http://192.168.1.10:51396",
          name: "PC",
          url: "http://192.168.1.10:51396",
        }}
        capability={{ ...capability, directPointer: { permissionGranted: false } }}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    const mouse = screen.getByRole("button", { name: "Mouse and keyboard control" });
    expect(mouse.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(mouse);
    expect(screen.getByRole("status").textContent).toContain("Allow Pointer and keyboard");
  });
});
