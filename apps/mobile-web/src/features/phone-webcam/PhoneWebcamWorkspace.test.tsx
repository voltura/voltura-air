import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPairingKeyMaterial, signPrivateKeyPayload } from "../../foundation/connection/pairingCredentials";
import { publishPhoneWebcamResult } from "../../foundation/connection/phoneWebcamResultBus";
import type { ClientMessage, PhoneWebcamCapability } from "../../foundation/protocol/messages";
import { hashSessionDescription } from "../../foundation/webrtc/sessionCrypto";
import PhoneWebcamWorkspace from "./PhoneWebcamWorkspace";

const capability: PhoneWebcamCapability = {
  enabled: true,
  permissionGranted: true,
  canUse: true,
  requiresRepair: false,
  videoOnly: true,
  maxWidth: 1920,
  maxHeight: 1080,
  maxFramesPerSecond: 30
};

const pcId = "http://192.168.1.10:51396";

beforeEach(() => {
  const items = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    get length() {return items.size;},
    clear: () => {items.clear();},
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => {items.delete(key);},
    setItem: (key: string, value: string) => {items.set(key, String(value));}
  } satisfies Storage);
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  vi.stubGlobal("RTCPeerConnection", class {});
});

afterEach(() => {
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("PhoneWebcamWorkspace", () => {
  it("releases the permission probe and lets the user choose a camera before capture starts", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("back", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByRole("option", { name: "Front Camera" });
    expect(permissionTrack.stop).toHaveBeenCalledOnce();
    expect(getUserMedia.mock.calls[0]?.[0]).toEqual({ audio: false, video: true });
    expect(send).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(getUserMedia.mock.calls[1]?.[0]).toMatchObject({ audio: false, video: { deviceId: { exact: "back" } } });
    const start = send.mock.calls[0]?.[0];
    expect(start?.type).toBe("phone.webcam.start");
    if (start?.type === "phone.webcam.start") {
      expect(start).toMatchObject({ captureWidth: 1920, captureHeight: 1080, captureFps: 30 });
    }
  });

  it("stops the camera immediately when Stop webcam is pressed during signaling", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    fireEvent.click(screen.getByRole("button", { name: "Stop webcam" }));

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.stop")).toBe(true);
    expect(screen.getByText("Phone webcam stopped.")).toBeTruthy();
  });

  it("invalidates a pending camera acquisition when the page is hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const pendingTrack = createTrack("front", 1920, 1080);
    const pendingCamera = createDeferred<MediaStream>();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockReturnValueOnce(pendingCamera.promise);
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});
    await act(async () => {pendingCamera.resolve(createStream(pendingTrack)); await pendingCamera.promise;});

    expect(pendingTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.start")).toBe(false);
    expect(screen.getByText("Camera released while Voltura Air is in the background.")).toBeTruthy();
  });

  it("does not signal a camera whose deferred preview start completed after the page was hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const previewStarted = createDeferred<void>();
    vi.mocked(HTMLMediaElement.prototype.play).mockReturnValueOnce(previewStarted.promise);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});
    await act(async () => {previewStarted.resolve(); await previewStarted.promise;});

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.start")).toBe(false);
    expect(screen.getByText("Camera released while Voltura Air is in the background.")).toBeTruthy();
  });

  it("invalidates a pending camera acquisition when the PC connection is lost", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const pendingTrack = createTrack("front", 1920, 1080);
    const pendingCamera = createDeferred<MediaStream>();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockReturnValueOnce(pendingCamera.promise);
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    rendered.rerender(workspace(send, undefined, "secure-direct", "connecting", 2));
    await act(async () => {pendingCamera.resolve(createStream(pendingTrack)); await pendingCamera.promise;});

    expect(pendingTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.start")).toBe(false);
    expect(screen.getByText("Waiting for the PC connection…")).toBeTruthy();
  });

  it("releases active capture when the page is hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(screen.getByText("Camera released while Voltura Air is in the background.")).toBeTruthy();
  });

  it("stops the owned session when the selected camera track ends", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    act(() => {selectedTrack.end();});

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(screen.getByText("The selected camera stopped.")).toBeTruthy();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.stop")).toBe(true);
  });

  it("starts one fresh session after returning to the foreground", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const resumedTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(resumedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await waitFor(() => expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.start")).toHaveLength(1));

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});

    await waitFor(() => expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.start")).toHaveLength(2), { timeout: 1500 });
    expect(getUserMedia).toHaveBeenCalledTimes(3);
    expect(firstTrack.stop).toHaveBeenCalledOnce();
  });

  it("reports camera permission denial without starting a session", async () => {
    const getUserMedia = vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));

    expect(await screen.findByText("Camera access was not allowed.")).toBeTruthy();
    expect(send).not.toHaveBeenCalled();
  });

  it("stops capture when the host does not offer video-only H.264", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    const start = await waitFor(() => {
      const message = send.mock.calls.map(([entry]) => entry).find((entry) => entry.type === "phone.webcam.start");
      if (message?.type !== "phone.webcam.start") {throw new Error("Start was not sent.");}
      return message;
    });

    act(() => {publishPhoneWebcamResult({
      type: "phone.webcam.start.result",
      operationId: start.operationId,
      succeeded: true,
      code: "accepted",
      message: "ready",
      offerSdp: "v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\n",
      hostSignature: "unused"
    });});

    expect(await screen.findByText("The PC did not offer a video-only H.264 webcam connection.")).toBeTruthy();
    expect(selectedTrack.stop).toHaveBeenCalledOnce();
  });

  it.each([
    { label: "Enhanced Direct", transportMode: "secure-direct" as const, policy: "all" as RTCIceTransportPolicy, candidate: "host", maximumBitrate: 12_000_000 },
    { label: "Relay", transportMode: "relay" as const, policy: "relay" as RTCIceTransportPolicy, candidate: "relay", maximumBitrate: 2_000_000 }
  ])("negotiates $label H.264 and replaces the camera on the same peer", async ({ label, transportMode, policy, candidate, maximumBitrate }) => {
    const sender = new FakeSender();
    class FakePeerConnection {
      static instance: FakePeerConnection | null = null;
      static configuration: RTCConfiguration | undefined;
      readonly iceGatheringState: RTCIceGatheringState = "complete";
      readonly listeners = new Map<string, (() => void)[]>();
      connectionState: RTCPeerConnectionState = "new";
      localDescription: RTCSessionDescriptionInit | null = null;
      remoteDescription: RTCSessionDescriptionInit | null = null;
      constructor(configuration?: RTCConfiguration) {
        FakePeerConnection.instance = this;
        FakePeerConnection.configuration = configuration;
      }
      addEventListener(type: string, listener: () => void) {this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);}
      setRemoteDescription(description: RTCSessionDescriptionInit) {this.remoteDescription = description; return Promise.resolve();}
      getTransceivers() {return [{ receiver: { track: { kind: "video" } }, sender, direction: "recvonly" }];}
      createAnswer() {return Promise.resolve({ type: "answer" as RTCSdpType, sdp: `v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ ${candidate}\r\n` });}
      setLocalDescription(description: RTCSessionDescriptionInit) {this.localDescription = description; return Promise.resolve();}
      getStats() {return Promise.resolve(new Map());}
      close() {this.connectionState = "closed";}
      connect() {this.connectionState = "connected"; for (const listener of this.listeners.get("connectionstatechange") ?? []) {listener();}}
    }
    vi.stubGlobal("RTCPeerConnection", FakePeerConnection as unknown as typeof RTCPeerConnection);
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const replacementTrack = createTrack("back", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(replacementTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    const clientKey = storeReconnectKey();
    const hostKey = createPairingKeyMaterial();
    if (!hostKey) {throw new Error("Host test key is unavailable.");}
    renderWorkspace(send, hostKey.reconnectPublicKey, transportMode);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    const start = await waitFor(() => {
      const message = send.mock.calls.map(([entry]) => entry).find((entry) => entry.type === "phone.webcam.start");
      if (message?.type !== "phone.webcam.start") {throw new Error("Start was not sent.");}
      return message;
    });
    const offerSdp = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\n";
    const offerHash = hashSessionDescription(offerSdp);
    const transcript = `VolturaAir phone-webcam:offer:v1:client-test:${start.operationId}:${offerHash}`;
    const hostSignature = signPrivateKeyPayload(hostKey.privateKey, new TextEncoder().encode(transcript));
    act(() => {publishPhoneWebcamResult({
      type: "phone.webcam.start.result",
      operationId: start.operationId,
      succeeded: true,
      code: "accepted",
      message: "ready",
      offerSdp,
      hostSignature,
      ...(transportMode === "relay" ? { iceServers: [{ urls: ["turns:turn.example.test:5349?transport=tcp"], username: "user", credential: "secret" }] } : {}),
      maximumBitrate
    });});

    await waitFor(() => expect(send.mock.calls.some(([entry]) => entry.type === "phone.webcam.answer")).toBe(true));
    expect(clientKey.privateKey).toBeTruthy();
    expect(FakePeerConnection.configuration?.iceTransportPolicy).toBe(policy);
    expect(sender.replaceTrack).toHaveBeenCalledWith(firstTrack);
    expect(sender.setParameters).toHaveBeenCalledWith(expect.objectContaining({
      degradationPreference: "maintain-resolution",
      encodings: [expect.objectContaining({ maxBitrate: maximumBitrate, maxFramerate: 30 })]
    }));
    act(() => {FakePeerConnection.instance?.connect();});
    expect(await screen.findByText(`Streaming through ${label}`)).toBeTruthy();

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    expect(send.mock.calls.filter(([entry]) => entry.type === "phone.webcam.start")).toHaveLength(1);
    expect(firstTrack.stop).toHaveBeenCalledOnce();
  });

  it("does not let a stale offer failure stop a replacement session", async () => {
    const firstNegotiation = createDeferred<void>();
    const peers: { close: ReturnType<typeof vi.fn> }[] = [];
    class DeferredPeerConnection {
      readonly iceGatheringState: RTCIceGatheringState = "complete";
      readonly sender = new FakeSender();
      readonly close = vi.fn();
      localDescription: RTCSessionDescriptionInit | null = null;
      constructor() {peers.push(this);}
      addEventListener() {return;}
      setRemoteDescription() {
        return peers.length === 1 ? firstNegotiation.promise : Promise.resolve();
      }
      getTransceivers() {return [{ receiver: { track: { kind: "video" } }, sender: this.sender, direction: "recvonly" }];}
      createAnswer() {return Promise.resolve({ type: "answer" as RTCSdpType, sdp: "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ host\r\n" });}
      setLocalDescription(description: RTCSessionDescriptionInit) {this.localDescription = description; return Promise.resolve();}
      getStats() {return Promise.resolve(new Map());}
    }
    vi.stubGlobal("RTCPeerConnection", DeferredPeerConnection as unknown as typeof RTCPeerConnection);
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const replacementTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(replacementTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial();
    if (!hostKey) {throw new Error("Host test key is unavailable.");}
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    const firstStart = await findStart(send, 1);
    publishSignedOffer(firstStart.operationId, hostKey.privateKey);
    await waitFor(() => expect(peers).toHaveLength(1));

    fireEvent.click(screen.getByRole("button", { name: "Stop webcam" }));
    await waitFor(() => expect((screen.getByRole("button", { name: "Start webcam" }) as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    const secondStart = await findStart(send, 2);
    publishSignedOffer(secondStart.operationId, hostKey.privateKey);
    await waitFor(() => expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.answer")).toHaveLength(1));

    await act(async () => {firstNegotiation.reject(new Error("Old negotiation failed.")); await Promise.resolve();});

    expect(replacementTrack.stop).not.toHaveBeenCalled();
    expect(peers[1]?.close).not.toHaveBeenCalled();
  });

  it("stops a pending replacement camera immediately when the page is hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const replacementTrack = createTrack("back", 1920, 1080);
    const replacementApplied = createDeferred<void>();
    const sender = new FakeSender();
    sender.replaceTrack
      .mockResolvedValueOnce(undefined)
      .mockReturnValueOnce(replacementApplied.promise);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(replacementTrack));
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {document.dispatchEvent(new Event("visibilitychange"));});

    expect(replacementTrack.stop).toHaveBeenCalledOnce();
    await act(async () => {replacementApplied.resolve(); await replacementApplied.promise;});
    expect(replacementTrack.stop).toHaveBeenCalledOnce();
  });

  it("keeps the newest camera when concurrent replacements complete in reverse order", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const olderTrack = createTrack("back", 1920, 1080);
    const newerTrack = createTrack("front-new", 1920, 1080);
    const olderCamera = createDeferred<MediaStream>();
    const newerCamera = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockReturnValueOnce(olderCamera.promise)
      .mockReturnValueOnce(newerCamera.promise);
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await act(async () => {newerCamera.resolve(createStream(newerTrack)); await newerCamera.promise;});
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(newerTrack));
    await act(async () => {olderCamera.resolve(createStream(olderTrack)); await olderCamera.promise;});

    expect(olderTrack.stop).toHaveBeenCalledOnce();
    expect(newerTrack.stop).not.toHaveBeenCalled();
  });

  it("keeps the active camera selected when its replacement is rejected", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockRejectedValueOnce(new DOMException("Unavailable", "NotReadableError"));
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });

    expect(await screen.findByText("The selected camera could not replace the active camera.")).toBeTruthy();
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("front");
    expect(firstTrack.stop).not.toHaveBeenCalled();
  });

  it("ignores a terminal event for an older operation", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await findStart(send, 1);

    act(() => {publishPhoneWebcamResult({
      type: "phone.webcam.ended",
      operationId: "older-operation",
      reason: "transport-lost",
      message: "Old session ended."
    });});

    expect(selectedTrack.stop).not.toHaveBeenCalled();
    expect(screen.queryByText("Old session ended.")).toBeNull();
  });

  it("stops capture and disables restart when the live host capability becomes unavailable", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
    await findStart(send, 1);

    const unavailable = { ...capability, enabled: false, canUse: false };
    rendered.rerender(workspace(send, undefined, "secure-direct", "paired", 1, unavailable));

    await waitFor(() => expect(selectedTrack.stop).toHaveBeenCalledOnce());
    expect((screen.getByRole("button", { name: "Start webcam" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Enable Phone webcam in the Windows app first.")).toBeTruthy();
  });
});

function renderWorkspace(
  send: (message: ClientMessage) => void,
  hostIdentityPublicKey = createPairingKeyMaterial()?.reconnectPublicKey,
  transportMode: "relay" | "secure-direct" = "secure-direct",
  state: "paired" | "connecting" = "paired",
  connectionEpoch = 1,
  currentCapability = capability
) {
  return render(workspace(send, hostIdentityPublicKey, transportMode, state, connectionEpoch, currentCapability));
}

function workspace(
  send: (message: ClientMessage) => void,
  hostIdentityPublicKey = createPairingKeyMaterial()?.reconnectPublicKey,
  transportMode: "relay" | "secure-direct" = "secure-direct",
  state: "paired" | "connecting" = "paired",
  connectionEpoch = 1,
  currentCapability = capability
) {
  return <PhoneWebcamWorkspace
    activePc={{ customName: false, id: pcId, name: "PC", url: pcId, hostIdentityPublicKey, transportMode }}
    capability={currentCapability}
    clientId="client-test"
    connectionEpoch={connectionEpoch}
    onBack={vi.fn()}
    send={send}
    state={state}
  />;
}

async function establishStreamingSession(getUserMedia: ReturnType<typeof vi.fn>, sender: FakeSender) {
  const peers: StreamingPeerConnection[] = [];
  class StreamingPeerConnection {
    readonly iceGatheringState: RTCIceGatheringState = "complete";
    readonly listeners = new Map<string, (() => void)[]>();
    connectionState: RTCPeerConnectionState = "new";
    localDescription: RTCSessionDescriptionInit | null = null;
    constructor() {peers.push(this);}
    addEventListener(type: string, listener: () => void) {this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);}
    setRemoteDescription() {return Promise.resolve();}
    getTransceivers() {return [{ receiver: { track: { kind: "video" } }, sender, direction: "recvonly" }];}
    createAnswer() {return Promise.resolve({ type: "answer" as RTCSdpType, sdp: "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ host\r\n" });}
    setLocalDescription(description: RTCSessionDescriptionInit) {this.localDescription = description; return Promise.resolve();}
    getStats() {return Promise.resolve(new Map());}
    close() {this.connectionState = "closed";}
    connect() {this.connectionState = "connected"; for (const listener of this.listeners.get("connectionstatechange") ?? []) {listener();}}
  }
  vi.stubGlobal("RTCPeerConnection", StreamingPeerConnection as unknown as typeof RTCPeerConnection);
  stubMediaDevices(getUserMedia);
  const send = vi.fn<(message: ClientMessage) => void>();
  storeReconnectKey();
  const hostKey = createPairingKeyMaterial();
  if (!hostKey) {throw new Error("Host test key is unavailable.");}
  renderWorkspace(send, hostKey.reconnectPublicKey);
  fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
  await screen.findByLabelText("Camera");
  fireEvent.click(screen.getByRole("button", { name: "Start webcam" }));
  const start = await findStart(send, 1);
  publishSignedOffer(start.operationId, hostKey.privateKey);
  await waitFor(() => expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.answer")).toBe(true));
  act(() => {peers[0]?.connect();});
  await screen.findByText("Streaming through Enhanced Direct");
}

async function findStart(send: ReturnType<typeof vi.fn>, count: number) {
  return waitFor(() => {
    const starts = send.mock.calls.map(([entry]) => entry as ClientMessage)
      .filter((entry) => entry.type === "phone.webcam.start");
    const message = starts[count - 1];
    if (message?.type !== "phone.webcam.start") {throw new Error(`Start ${count} was not sent.`);}
    return message;
  });
}

function publishSignedOffer(operationId: string, hostPrivateKey: string) {
  const offerSdp = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\n";
  const offerHash = hashSessionDescription(offerSdp);
  const transcript = `VolturaAir phone-webcam:offer:v1:client-test:${operationId}:${offerHash}`;
  const hostSignature = signPrivateKeyPayload(hostPrivateKey, new TextEncoder().encode(transcript));
  act(() => {publishPhoneWebcamResult({
    type: "phone.webcam.start.result",
    operationId,
    succeeded: true,
    code: "accepted",
    message: "ready",
    offerSdp,
    hostSignature,
    maximumBitrate: 12_000_000
  });});
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

function storeReconnectKey() {
  const key = createPairingKeyMaterial();
  if (!key) {throw new Error("Test key generation is unavailable.");}
  localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);
  return key;
}

class FakeSender {
  readonly replaceTrack = vi.fn<(track: MediaStreamTrack | null) => Promise<void>>().mockResolvedValue(undefined);
  readonly getParameters = vi.fn(() => ({ encodings: [] }) as unknown as RTCRtpSendParameters);
  readonly setParameters = vi.fn<(parameters: RTCRtpSendParameters) => Promise<RTCRtpSendParameters>>()
    .mockImplementation((parameters) => Promise.resolve(parameters));
  track: MediaStreamTrack | null = null;
}

function stubMediaDevices(getUserMedia: ReturnType<typeof vi.fn>) {
  vi.stubGlobal("navigator", {
    mediaDevices: {
      getUserMedia,
      enumerateDevices: vi.fn().mockResolvedValue([
        { kind: "videoinput", deviceId: "front", label: "Front Camera", groupId: "group", toJSON: () => ({}) },
        { kind: "videoinput", deviceId: "back", label: "Back Camera", groupId: "group", toJSON: () => ({}) }
      ])
    }
  });
}

function createTrack(deviceId: string, width: number, height: number) {
  let ended: (() => void) | undefined;
  return {
    contentHint: "",
    stop: vi.fn(),
    getSettings: () => ({ deviceId, width, height, frameRate: 30 }),
    addEventListener: vi.fn((type: string, listener: EventListenerOrEventListenerObject) => {
      if (type === "ended") {
        ended = typeof listener === "function" ? () => listener(new Event("ended")) : () => listener.handleEvent(new Event("ended"));
      }
    }),
    end: () => ended?.()
  };
}

function createStream(track: ReturnType<typeof createTrack>): MediaStream {
  return {
    getTracks: () => [track],
    getVideoTracks: () => [track]
  } as unknown as MediaStream;
}
