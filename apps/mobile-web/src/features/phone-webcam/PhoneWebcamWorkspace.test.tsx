import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createPairingKeyMaterial,
  signPrivateKeyPayload,
} from "../../foundation/connection/pairingCredentials";
import { publishPhoneWebcamResult } from "../../foundation/connection/phoneWebcamResultBus";
import type {
  ClientMessage,
  PhoneWebcamCapability,
  PhoneWebcamStartResultMessage,
} from "../../foundation/protocol/messages";
import { hashSessionDescription } from "../../foundation/webrtc/sessionCrypto";
import PhoneWebcamWorkspace from "./PhoneWebcamWorkspace";

const capability: PhoneWebcamCapability = {
  enabled: true,
  permissionGranted: true,
  canUse: true,
  requiresRepair: false,
  microphoneAvailable: false,
  maxWidth: 1920,
  maxHeight: 1080,
  maxFramesPerSecond: 30,
};

const pcId = "http://192.168.1.10:51396";
const defaultHostIdentity = createPairingKeyMaterial()?.reconnectPublicKey;

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
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  vi.stubGlobal("RTCPeerConnection", class {});
});

afterEach(() => {
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("PhoneWebcamWorkspace", () => {
  it("does not enumerate or revive a permission request after unmount", async () => {
    const pending = createDeferred<MediaStream>();
    const track = createTrack("permission", 640, 480);
    stubMediaDevices(vi.fn().mockReturnValue(pending.promise));
    const view = renderWorkspace(vi.fn());
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    view.unmount();
    await act(async () => {
      await Promise.resolve(pending.resolve(createStream(track)));
    });
    expect(track.stop).toHaveBeenCalled();
    expect(navigator.mediaDevices.enumerateDevices).not.toHaveBeenCalled();
  });

  it.each(["hidden", "disconnect", "revoked"])(
    "releases a permission probe during %s even while enumeration is pending",
    async (reason) => {
      const pending = createDeferred<MediaDeviceInfo[]>();
      const track = createTrack("permission", 640, 480);
      stubMediaDevices(vi.fn().mockResolvedValue(createStream(track)));
      vi.mocked(navigator.mediaDevices.enumerateDevices).mockReturnValue(pending.promise);
      const send = vi.fn();
      const hostKey = createPairingKeyMaterial()?.reconnectPublicKey;
      const view = renderWorkspace(send, hostKey);
      fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
      await waitFor(() => expect(navigator.mediaDevices.enumerateDevices).toHaveBeenCalled());
      if (reason === "hidden") {
        Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
        fireEvent(document, new Event("visibilitychange"));
      } else {
        view.rerender(
          workspace(
            send,
            hostKey,
            "secure-direct",
            reason === "disconnect" ? "connecting" : "paired",
            1,
            reason === "revoked" ? { ...capability, canUse: false } : capability,
          ),
        );
      }
      expect(track.stop).toHaveBeenCalled();
      await act(async () => {
        await Promise.resolve(
          pending.resolve([
            { kind: "videoinput", deviceId: "late", label: "Late camera" } as MediaDeviceInfo,
          ]),
        );
      });
      expect(screen.queryByLabelText("Camera")).toBeNull();
    },
  );

  it.each(["missing", "ended", "settings"])(
    "releases every acquired track when selected camera video is %s",
    async (failure) => {
      const permission = createTrack("permission", 640, 480);
      const video = createTrack("front", 1920, 1080);
      const audio = createAudioTrack();
      if (failure === "ended") {
        Object.assign(video, { readyState: "ended" });
      }
      if (failure === "settings") {
        video.getSettings = () => {
          throw new Error("Camera disappeared");
        };
      }
      const stream =
        failure === "missing" ? createAudioStream(audio) : createAvStream(video, audio);
      stubMediaDevices(
        vi.fn().mockResolvedValueOnce(createStream(permission)).mockResolvedValueOnce(stream),
      );
      storeReconnectKey();
      const send = vi.fn();
      renderWorkspace(send);
      fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
      await screen.findByLabelText("Camera");
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      await screen.findByText("The selected camera could not be started.");
      expect(audio.stop).toHaveBeenCalled();
      if (failure !== "missing") {
        expect(video.stop).toHaveBeenCalled();
      }
      expect(send).not.toHaveBeenCalled();
    },
  );

  it("starts transport without waiting for a stalled local preview play promise", async () => {
    vi.mocked(HTMLMediaElement.prototype.play).mockReturnValue(new Promise<void>(() => undefined));
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(createTrack("front", 1920, 1080))),
    );
    storeReconnectKey();
    const send = vi.fn();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await findStart(send, 1);
  });

  it("does not open a video fallback after an audio acquisition is canceled", async () => {
    const pending = createDeferred<MediaStream>();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
      .mockReturnValueOnce(pending.promise);
    stubMediaDevices(getUserMedia);
    storeReconnectKey();
    renderWorkspace(vi.fn(), undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(3));
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await act(async () => {
      await Promise.resolve(pending.reject(new DOMException("Canceled", "AbortError")));
    });
    expect(getUserMedia).toHaveBeenCalledTimes(3);
  });

  it("ignores an old microphone denial after a newer permission succeeds", async () => {
    const pending = createDeferred<MediaStream>();
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockReturnValueOnce(pending.promise)
        .mockResolvedValueOnce(createAudioStream(createAudioTrack())),
    );
    const send = vi.fn();
    const key = createPairingKeyMaterial()?.reconnectPublicKey;
    const withMic = { ...capability, microphoneAvailable: true };
    const view = renderWorkspace(send, key, "secure-direct", "paired", 1, withMic);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    view.rerender(workspace(send, key, "secure-direct", "paired", 1, capability));
    await act(async () => {
      await Promise.resolve(undefined);
    });
    view.rerender(workspace(send, key, "secure-direct", "paired", 1, withMic));
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
    await act(async () => {
      await Promise.resolve(pending.reject(new DOMException("Denied", "NotAllowedError")));
    });
    expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true);
    expect(
      screen.queryByText("Microphone access was not allowed or no microphone is available."),
    ).toBeNull();
  });

  it("rejects an already-ended microphone permission probe", async () => {
    const audio = createAudioTrack();
    Object.assign(audio, { readyState: "ended" });
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createAudioStream(audio)),
    );
    renderWorkspace(vi.fn(), undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await screen.findByText("Microphone access was not allowed or no microphone is available.");
    expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(false);
    expect(audio.stop).toHaveBeenCalled();
  });

  it("falls back to video when the requested microphone is already ended", async () => {
    const deadAudio = createAudioTrack();
    Object.assign(deadAudio, { readyState: "ended" });
    const failedVideo = createTrack("front", 1920, 1080);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
      .mockResolvedValueOnce(createAvStream(failedVideo, deadAudio))
      .mockResolvedValueOnce(createStream(createTrack("front", 1920, 1080)));
    stubMediaDevices(getUserMedia);
    storeReconnectKey();
    const send = vi.fn();
    renderWorkspace(send, undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect((await findStart(send, 1)).useMicrophone).toBe(false);
    expect(deadAudio.stop).toHaveBeenCalled();
    expect(failedVideo.stop).toHaveBeenCalled();
  });

  it.each(["ended", "ends-during-attach", "settings-failure"])(
    "keeps the current camera when replacement has %s",
    async (failure) => {
      const first = createTrack("front", 1920, 1080);
      const next = createTrack("back", 1920, 1080);
      const deferred = createDeferred<void>();
      const sender = new FakeSender();
      if (failure === "ended") {
        Object.assign(next, { readyState: "ended" });
      }
      if (failure === "settings-failure") {
        next.getSettings = () => {
          throw new Error("Camera unavailable");
        };
      }
      if (failure === "ends-during-attach") {
        sender.replaceTrack.mockResolvedValueOnce(undefined).mockReturnValueOnce(deferred.promise);
      }
      await establishStreamingSession(
        vi
          .fn()
          .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
          .mockResolvedValueOnce(createStream(first))
          .mockResolvedValueOnce(createStream(next)),
        sender,
      );
      fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
      if (failure === "ends-during-attach") {
        await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(next));
        Object.assign(next, { readyState: "ended" });
        await act(async () => {
          await Promise.resolve(deferred.resolve());
        });
      }
      await waitFor(() => expect(next.stop).toHaveBeenCalled());
      expect(first.stop).not.toHaveBeenCalled();
      if (failure === "ends-during-attach") {
        expect(sender.replaceTrack).toHaveBeenLastCalledWith(first);
      }
      expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("front");
    },
  );

  it("releases both camera streams when a dead replacement cannot restore the sender", async () => {
    const first = createTrack("front", 1920, 1080);
    const next = createTrack("back", 1920, 1080);
    const deferred = createDeferred<void>();
    const sender = new FakeSender();
    sender.replaceTrack
      .mockResolvedValueOnce(undefined)
      .mockReturnValueOnce(deferred.promise)
      .mockRejectedValueOnce(new Error("Sender closed during rollback"));
    const { send } = await establishStreamingSession(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(first))
        .mockResolvedValueOnce(createStream(next)),
      sender,
    );
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(next));
    Object.assign(next, { readyState: "ended" });
    await act(async () => {
      deferred.resolve();
      await Promise.resolve();
    });
    await screen.findByText("The camera connection could not be restored.");
    expect(first.stop).toHaveBeenCalledOnce();
    expect(next.stop).toHaveBeenCalledOnce();
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
  });

  it("allows a retry after initial camera capture times out and stops any late input", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const nativeCapture = createDeferred<MediaStream>();
    const lateTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockReturnValueOnce(nativeCapture.promise)
      .mockResolvedValueOnce(createStream(createTrack("front", 1920, 1080)));
    stubMediaDevices(getUserMedia);
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial()!;
    const send = vi.fn();
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(screen.getByRole("status").textContent).toContain("The camera did not start in time.");
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.start" }));
    await act(() => {
      nativeCapture.resolve(createStream(lateTrack));
      return nativeCapture.promise;
    });
    expect(lateTrack.stop).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await findStart(send, 1);
    vi.useRealTimers();
  });

  it("ignores duplicate webcam offers and retires stalled setup even if close fires a state event", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const constructed = vi.fn();
    const released = vi.fn();
    const activeTrack = createTrack("front", 1920, 1080);
    const remoteDescription = createDeferred<void>();
    class PendingPeer extends EventTarget {
      connectionState = "new";
      constructor() {
        super();
        constructed();
      }
      setRemoteDescription() {
        return remoteDescription.promise;
      }
      close() {
        released();
        this.connectionState = "closed";
        this.dispatchEvent(new Event("connectionstatechange"));
      }
    }
    vi.stubGlobal("RTCPeerConnection", PendingPeer);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(activeTrack)),
    );
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial()!;
    const send = vi.fn();
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const start = await findStart(send, 1);
    publishSignedOffer(start.operationId, hostKey.privateKey);
    publishSignedOffer(start.operationId, hostKey.privateKey);
    expect(constructed).toHaveBeenCalledOnce();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    await act(() => {
      remoteDescription.reject(new Error("Browser finished closing"));
      return Promise.resolve();
    });
    expect(released).toHaveBeenCalledOnce();
    expect(activeTrack.stop).toHaveBeenCalledOnce();
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.answer" }));
    expect(screen.getByRole("status").textContent).toContain("webcam connection timed out");
    vi.useRealTimers();
  });

  it("does not attach the microphone if Stop wins while the video sender is pending", async () => {
    const videoAttached = createDeferred<void>();
    const sender = new FakeSender();
    sender.replaceTrack.mockReturnValueOnce(videoAttached.promise);
    const audioSender = new FakeSender();
    const addTrack = vi.fn(() => audioSender);
    class PendingPeer extends EventTarget {
      setRemoteDescription() {
        return Promise.resolve();
      }
      getTransceivers() {
        return [
          { receiver: { track: { kind: "video" } }, sender, direction: "recvonly" },
          { receiver: { track: { kind: "audio" } }, sender: audioSender, direction: "recvonly" },
        ];
      }
      addTrack = addTrack;
      close() {
        /* No external resources in this fake. */
      }
    }
    vi.stubGlobal("RTCPeerConnection", PendingPeer);
    const video = createTrack("front", 1920, 1080);
    const audio = createAudioTrack();
    stubMediaDevices(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
        .mockResolvedValueOnce(createAvStream(video, audio)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial()!;
    renderWorkspace(send, hostKey.reconnectPublicKey, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await screen.findByText("Phone microphone is ready. Start when you are ready.");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const start = await findStart(send, 1);
    publishSignedOffer(start.operationId, hostKey.privateKey, true);
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(video));

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await act(async () => {
      videoAttached.resolve();
      await videoAttached.promise;
    });

    expect(addTrack).not.toHaveBeenCalled();
    expect(video.stop).toHaveBeenCalledOnce();
    expect(audio.stop).toHaveBeenCalledOnce();
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.answer" }));
  });

  it.each(["phone.webcam.start", "phone.webcam.answer"] as const)(
    "releases the selected camera when the transport refuses %s",
    async (messageType) => {
      const permission = createTrack("permission", 640, 480);
      const first = createTrack("front", 1920, 1080);
      const retry = createTrack("front", 1920, 1080);
      const getUserMedia = vi.fn()
        .mockResolvedValueOnce(createStream(permission))
        .mockResolvedValueOnce(createStream(first))
        .mockResolvedValueOnce(createStream(retry));
      const { send, hostPrivateKey } = await establishStreamingSession(getUserMedia, new FakeSender());
      fireEvent.click(screen.getByRole("button", { name: "Stop" }));
      send.mockImplementation((message) => message.type !== messageType);
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      const start = await findStart(send, 2);
      if (messageType === "phone.webcam.answer") {
        publishSignedOffer(start.operationId, hostPrivateKey);
      }

      const status = await screen.findByText(/could not be sent/);
      expect(status.textContent).toContain("could not be sent");
      expect(retry.stop).toHaveBeenCalledOnce();
      expect(screen.getByText("Reconnecting")).toBeTruthy();
    },
  );

  it("reports a bounded browser negotiation error without exposing SDP", async () => {
    class RejectingPeerConnection {
      readonly iceGatheringState: RTCIceGatheringState = "new";
      connectionState: RTCPeerConnectionState = "new";
      addEventListener() {
        /* This rejection-only peer emits no events. */
      }
      setRemoteDescription() {
        return Promise.reject(
          new DOMException("Unsupported bundled audio offer.\n", "OperationError"),
        );
      }
      close() {
        this.connectionState = "closed";
      }
    }
    vi.stubGlobal(
      "RTCPeerConnection",
      RejectingPeerConnection as unknown as typeof RTCPeerConnection,
    );
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial();
    if (!hostKey) {
      throw new Error("Host test key is unavailable.");
    }
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const start = await findStart(send, 1);
    publishSignedOffer(start.operationId, hostKey.privateKey);

    expect(
      await screen.findByText(
        "This browser could not finish Phone webcam while applying the PC media offer (OperationError: Unsupported bundled audio offer.).",
      ),
    ).toBeTruthy();
  });

  it("expands and restores the camera view without ending its session", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    fireEvent.click(screen.getByRole("button", { name: "Expand camera view" }));

    expect(
      screen.getByLabelText("Camera view").closest(".phone-webcam-workspace")?.className,
    ).toContain("camera-view-expanded");
    fireEvent(window, new Event("resize"));
    expect(screen.getByRole("button", { name: "Restore camera view" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Restore camera view" }));

    expect(
      screen.getByLabelText("Camera view").closest(".phone-webcam-workspace")?.className,
    ).not.toContain("camera-view-expanded");
    expect(send).not.toHaveBeenCalled();
  });

  it("releases the permission probe and lets the user choose a camera before capture starts", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("back", 1920, 1080);
    const getUserMedia = vi
      .fn()
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
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(getUserMedia.mock.calls[1]?.[0]).toMatchObject({
      audio: false,
      video: { deviceId: { exact: "back" } },
    });
    const start = send.mock.calls[0]?.[0];
    expect(start?.type).toBe("phone.webcam.start");
    if (start?.type === "phone.webcam.start") {
      expect(start).toMatchObject({
        captureWidth: 1920,
        captureHeight: 1080,
        captureFps: 30,
        useMicrophone: false,
      });
    }
  });

  it("refreshes the available cameras without opening capture and keeps duplicate device IDs out", async () => {
    const devices = new EventTarget();
    const getUserMedia = vi.fn().mockResolvedValue(createStream(createTrack("probe", 640, 480)));
    const front = { kind: "videoinput", deviceId: "front", label: "Front Ultra Wide Camera" };
    const rear = { kind: "videoinput", deviceId: "rear", label: "Rear Camera" };
    const enumerateDevices = vi.fn().mockResolvedValueOnce([front, front]).mockResolvedValueOnce([rear]);
    vi.stubGlobal("navigator", { mediaDevices: Object.assign(devices, { getUserMedia, enumerateDevices }) });
    renderWorkspace(vi.fn());
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    expect(screen.getAllByRole("option", { name: "Front Ultra Wide Camera" })).toHaveLength(1);
    act(() => {
      devices.dispatchEvent(new Event("devicechange"));
    });
    await screen.findByRole("option", { name: "Rear Camera" });
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("rear");
    expect(getUserMedia).toHaveBeenCalledOnce();
  });

  it.each(["while streaming", "while hidden"])("requires a fresh Start when the camera is removed %s", async (when) => {
    const active = createTrack("front", 1080, 1920);
    const replacement = createTrack("back", 1920, 1080);
    const getUserMedia = vi.fn<(constraints: MediaStreamConstraints) => Promise<MediaStream>>()
      .mockResolvedValueOnce(createStream(createTrack("probe", 640, 480)))
      .mockResolvedValueOnce(createStream(active))
      .mockResolvedValueOnce(createStream(replacement));
    const { send } = await establishStreamingSession(getUserMedia, new FakeSender());
    const mediaDevices = navigator.mediaDevices;
    const current = await mediaDevices.enumerateDevices();
    vi.mocked(mediaDevices.enumerateDevices).mockResolvedValue(
      current.filter(({ deviceId }) => deviceId !== "front"),
    );
    vi.useFakeTimers();
    await act(() => {
      if (when === "while hidden") {
        fireEvent(window, new Event("pagehide"));
        fireEvent(window, new Event("pageshow"));
      } else {
        mediaDevices.dispatchEvent(new Event("devicechange"));
        active.end();
      }
      return Promise.resolve();
    });
    expect(screen.queryByRole("option", { name: "Front Camera" })).toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(250));

    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("status").textContent).toContain("The selected camera is unavailable.");
    expect(screen.getByLabelText<HTMLSelectElement>("Camera").value).toBe("back");
    await act(() => {
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      return Promise.resolve();
    });
    expect(getUserMedia.mock.lastCall?.[0]?.video).toMatchObject({ deviceId: { exact: "back" } });
    expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"))
      .toHaveLength(2);
  });

  it("keeps microphone mute available if device discovery temporarily loses the camera list", async () => {
    const microphone = createAudioTrack();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("probe", 640, 480)))
      .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
      .mockResolvedValueOnce(createAvStream(createTrack("front", 1080, 1920), microphone));
    await establishStreamingSession(getUserMedia, new FakeSender(), undefined, true);
    const mediaDevices = navigator.mediaDevices;
    vi.mocked(mediaDevices.enumerateDevices).mockResolvedValue([]);
    await act(() => {
      mediaDevices.dispatchEvent(new Event("devicechange"));
      return Promise.resolve();
    });

    expect(screen.queryByLabelText("Camera")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Mute" }));
    expect(microphone.enabled).toBe(false);
    expect(screen.getByRole("button", { name: "Unmute" })).toBeDefined();
  });

  it("signs valid capture settings when the browser omits usable dimensions", async () => {
    const active = createTrack("front", Number.NaN, 0);
    stubMediaDevices(vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("probe", 640, 480)))
      .mockResolvedValueOnce(createStream(active)));
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect(await findStart(send, 1)).toMatchObject({ captureWidth: 1920, captureHeight: 1080, captureFps: 30 });
  });

  it("keeps microphone off by default and exposes Mute only after permission and capture", async () => {
    const cameraProbe = createTrack("permission", 640, 480);
    const microphoneProbe = createAudioTrack();
    const camera = createTrack("front", 1920, 1080);
    const microphone = createAudioTrack();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(cameraProbe))
      .mockResolvedValueOnce(createAudioStream(microphoneProbe))
      .mockResolvedValueOnce(createAvStream(camera, microphone));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send, undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(false);
    expect(screen.queryByRole("button", { name: "Mute" })).toBeNull();

    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
    expect(microphoneProbe.stop).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(send.mock.calls[0]?.[0]).toMatchObject({
      type: "phone.webcam.start",
      useMicrophone: true,
    });
    rendered.rerender(
      workspace(send, undefined, "secure-direct", "paired", 1, {
        ...capability,
        microphoneAvailable: false,
      }),
    );
    expect(screen.getByRole("button", { name: "Mute" })).toBeTruthy();
    expect(screen.queryByLabelText("Use microphone")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Mute" }));
    expect(microphone.enabled).toBe(false);
    expect(screen.getByRole("button", { name: "Unmute" })).toBeTruthy();
  });

  it("resets microphone off after permission denial", async () => {
    const cameraProbe = createTrack("permission", 640, 480);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(cameraProbe))
      .mockRejectedValueOnce(new DOMException("denied", "NotAllowedError"));
    stubMediaDevices(getUserMedia);
    renderWorkspace(vi.fn(), undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));

    await screen.findByText("Microphone access was not allowed or no microphone is available.");
    expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(false);
    expect(screen.queryByRole("button", { name: "Mute" })).toBeNull();
  });

  it("reports microphone capture fallback while starting video only", async () => {
    const cameraProbe = createTrack("permission", 640, 480);
    const microphoneProbe = createAudioTrack();
    const camera = createTrack("front", 1920, 1080);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(cameraProbe))
      .mockResolvedValueOnce(createAudioStream(microphoneProbe))
      .mockRejectedValueOnce(new DOMException("busy", "NotReadableError"))
      .mockResolvedValueOnce(createStream(camera));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send, undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));

    expect(
      await screen.findByText("The microphone could not be opened. Starting encrypted video only…"),
    ).toBeTruthy();
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(send.mock.calls[0]?.[0]).toMatchObject({
      type: "phone.webcam.start",
      useMicrophone: false,
    });
  });

  it("clears an idle microphone selection when the host target becomes unavailable", async () => {
    const cameraProbe = createTrack("permission", 640, 480);
    const microphoneProbe = createAudioTrack();
    const camera = createTrack("front", 1920, 1080);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(cameraProbe))
      .mockResolvedValueOnce(createAudioStream(microphoneProbe))
      .mockResolvedValueOnce(createStream(camera));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send, undefined, "secure-direct", "paired", 1, {
      ...capability,
      microphoneAvailable: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );

    rendered.rerender(
      workspace(send, undefined, "secure-direct", "paired", 1, {
        ...capability,
        microphoneAvailable: false,
      }),
    );
    await waitFor(() => expect(screen.queryByLabelText("Use microphone")).toBeNull());
    fireEvent.click(screen.getByRole("button", { name: "Start" }));

    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(getUserMedia.mock.calls[2]?.[0]).toMatchObject({ audio: false });
    expect(send.mock.calls[0]?.[0]).toMatchObject({
      type: "phone.webcam.start",
      useMicrophone: false,
    });
  });

  it("disables the microphone permission probe when the host blocks Phone Webcam", async () => {
    const getUserMedia = vi.fn().mockResolvedValue(createStream(createTrack("probe", 640, 480)));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    const withMicrophone = { ...capability, microphoneAvailable: true };
    const rendered = renderWorkspace(send, undefined, "secure-direct", "paired", 1, withMicrophone);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");

    rendered.rerender(workspace(send, undefined, "secure-direct", "paired", 1, {
      ...withMicrophone,
      permissionGranted: false,
      canUse: false,
    }));

    const microphone = screen.getByRole<HTMLInputElement>("checkbox", { name: "Use microphone" });
    expect(microphone.disabled).toBe(true);
    microphone.click();
    expect(getUserMedia).toHaveBeenCalledOnce();
  });

  it("stops the camera immediately when Stop is pressed during signaling", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.stop")).toBe(true);
    expect(screen.getByText("Ready to start.")).toBeTruthy();
  });

  it("invalidates a pending camera acquisition when the page is hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const pendingTrack = createTrack("front", 1920, 1080);
    const pendingCamera = createDeferred<MediaStream>();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockReturnValueOnce(pendingCamera.promise);
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });
    await act(async () => {
      pendingCamera.resolve(createStream(pendingTrack));
      await pendingCamera.promise;
    });

    expect(pendingTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.start")).toBe(false);
    expect(
      screen.getByText("Camera released while Voltura Air is in the background."),
    ).toBeTruthy();
  });

  it("does not revive signaling when a deferred preview completes after hiding", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const previewStarted = createDeferred<void>();
    vi.mocked(HTMLMediaElement.prototype.play).mockReturnValueOnce(previewStarted.promise);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    await findStart(send, 1);
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });
    await act(async () => {
      previewStarted.resolve();
      await previewStarted.promise;
    });

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
    ).toHaveLength(1);
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.stop")).toBe(true);
    expect(
      screen.getByText("Camera released while Voltura Air is in the background."),
    ).toBeTruthy();
  });

  it("invalidates a pending camera acquisition when the PC connection is lost", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const pendingTrack = createTrack("front", 1920, 1080);
    const pendingCamera = createDeferred<MediaStream>();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockReturnValueOnce(pendingCamera.promise);
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(2));

    rendered.rerender(workspace(send, undefined, "secure-direct", "connecting", 2));
    await act(async () => {
      pendingCamera.resolve(createStream(pendingTrack));
      await pendingCamera.promise;
    });

    expect(pendingTrack.stop).toHaveBeenCalledOnce();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.start")).toBe(false);
    expect(screen.getByText("Waiting for the PC connection…")).toBeTruthy();
  });

  it("waits for current host capability before opening the same camera after reconnect", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const first = createTrack("front", 1080, 1920);
    const reopened = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(first))
      .mockResolvedValueOnce(createStream(reopened));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const firstStart = await findStart(send, 1);

    rendered.rerender(workspace(send, undefined, "secure-direct", "paired", 2, capability, pcId, false));
    expect(first.stop).toHaveBeenCalledOnce();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(getUserMedia).toHaveBeenCalledTimes(2);

    rendered.rerender(workspace(send, undefined, "secure-direct", "paired", 2, capability, pcId, true));
    await act(() => vi.advanceTimersByTimeAsync(250));
    const secondStart = await findStart(send, 2);
    expect(secondStart.operationId).not.toBe(firstStart.operationId);
    expect(getUserMedia.mock.lastCall?.[0]).toMatchObject({ video: { deviceId: { exact: "front" } } });
  });

  it("releases active capture when the page is hidden", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(selectedTrack));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(
      screen.getByText("Camera released while Voltura Air is in the background."),
    ).toBeTruthy();
  });

  it("stops the owned session when the selected camera track ends", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());

    act(() => {
      selectedTrack.end();
    });

    expect(selectedTrack.stop).toHaveBeenCalledOnce();
    expect(screen.getByText("The selected camera stopped.")).toBeTruthy();
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.stop")).toBe(true);
  });

  it.each(["visibilitychange", "pageshow", "muted pageshow", "re-hide"])(
    "starts one fresh session only in the foreground after %s",
    async (returnMode) => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const permissionTrack = createTrack("permission", 640, 480);
      const firstTrack = createTrack("front", 1920, 1080);
      const resumedTrack = createTrack("front", 1920, 1080);
      const getUserMedia = vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(firstTrack))
        .mockResolvedValueOnce(createStream(resumedTrack));
      stubMediaDevices(getUserMedia);
      const send = vi.fn<(message: ClientMessage) => void>();
      storeReconnectKey();
      renderWorkspace(send);
      fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
      await screen.findByLabelText("Camera");
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      await waitFor(() =>
        expect(
          send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
        ).toHaveLength(1),
      );

      const setVisibility = (value: DocumentVisibilityState) => {
        Object.defineProperty(document, "visibilityState", { configurable: true, value });
        fireEvent(document, new Event("visibilitychange"));
      };
      if (returnMode === "pageshow" || returnMode === "muted pageshow") {
        if (returnMode === "muted pageshow") {
          act(() => firstTrack.setMuted(true));
        }
        fireEvent(window, new Event("pagehide"));
        fireEvent(window, new Event("pageshow"));
      } else {
        setVisibility("hidden");
        setVisibility("visible");
        if (returnMode === "re-hide") {
          setVisibility("hidden");
          fireEvent.click(screen.getByRole("button", { name: "Start" }));
          await act(() => vi.advanceTimersByTimeAsync(500));
          expect(getUserMedia).toHaveBeenCalledTimes(2);
          setVisibility("visible");
        }
      }
      await act(() => vi.advanceTimersByTimeAsync(250));

      await waitFor(
        () =>
          expect(
            send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
          ).toHaveLength(2),
        { timeout: 1500 },
      );
      expect(getUserMedia).toHaveBeenCalledTimes(3);
      expect(firstTrack.stop).toHaveBeenCalledOnce();
      vi.useRealTimers();
    },
  );

  it("reports camera permission denial without starting a session", async () => {
    const getUserMedia = vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"));
    stubMediaDevices(getUserMedia);
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));

    expect(await screen.findByText("Camera access was not allowed.")).toBeTruthy();
    expect(send).not.toHaveBeenCalled();
  });

  it.each(["same PC", "different PC"])("retires capture when reconnecting to a %s", async (target) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
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
    const hostKey = createPairingKeyMaterial()!.reconnectPublicKey;
    const view = renderWorkspace(send, hostKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const first = await findStart(send, 1);

    const nextId = target === "same PC" ? pcId : "http://192.0.2.10:51396";
    view.rerender(workspace(send, hostKey, "secure-direct", "paired", 2, capability, nextId));
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    await act(() => vi.advanceTimersByTimeAsync(500));

    if (target === "same PC") {
      const resumed = await findStart(send, 2);
      expect(resumed.operationId).not.toBe(first.operationId);
      expect(getUserMedia).toHaveBeenCalledTimes(3);
    } else {
      expect(getUserMedia).toHaveBeenCalledTimes(2);
      expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
    }
    vi.useRealTimers();
  });

  it("stops capture when the host offers unexpected media", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const start = await waitFor(() => {
      const message = send.mock.calls
        .map(([entry]) => entry)
        .find((entry) => entry.type === "phone.webcam.start");
      if (message?.type !== "phone.webcam.start") {
        throw new Error("Start was not sent.");
      }
      return message;
    });

    act(() => {
      publishPhoneWebcamResult({
        type: "phone.webcam.start.result",
        operationId: start.operationId,
        succeeded: true,
        code: "accepted",
        message: "ready",
        offerSdp:
          "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\n",
        hostSignature: "unused",
      });
    });

    expect(await screen.findByText("The PC offered unexpected Phone webcam media.")).toBeTruthy();
    expect(selectedTrack.stop).toHaveBeenCalledOnce();
  });

  it.each([
    "v=0\r\nm=video 0 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\n",
    "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=inactive\r\n",
    "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\na=inactive\r\n",
    "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\na=sendonly\r\n",
  ])("rejects a host offer that does not send the requested video", async (offerSdp) => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const start = await findStart(send, 1);

    act(() => {
      publishPhoneWebcamResult({
        type: "phone.webcam.start.result",
        operationId: start.operationId,
        succeeded: true,
        code: "accepted",
        message: "ready",
        offerSdp,
        hostSignature: "unused",
      });
    });

    expect(await screen.findByText("The PC offered unexpected Phone webcam media.")).toBeTruthy();
    expect(selectedTrack.stop).toHaveBeenCalledOnce();
  });

  it.each([
    {
      label: "Enhanced Direct",
      transportMode: "secure-direct" as const,
      policy: "all" as RTCIceTransportPolicy,
      candidate: "host",
      maximumBitrate: 12_000_000,
    },
    {
      label: "Relay",
      transportMode: "relay" as const,
      policy: "relay" as RTCIceTransportPolicy,
      candidate: "relay",
      maximumBitrate: 2_000_000,
    },
  ])(
    "negotiates $label H.264 after Safari publishes its sender encoding and replaces the camera on the same peer",
    async ({ label, transportMode, policy, candidate, maximumBitrate }) => {
      let senderEncodingAvailable = false;
      const sender = new FakeSender(() => (senderEncodingAvailable ? 1 : 0));
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
        addEventListener(type: string, listener: () => void) {
          this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
        }
        setRemoteDescription(description: RTCSessionDescriptionInit) {
          this.remoteDescription = description;
          return Promise.resolve();
        }
        getTransceivers() {
          return [{ receiver: { track: { kind: "video" } }, sender, direction: "recvonly" }];
        }
        createAnswer() {
          return Promise.resolve({
            type: "answer" as RTCSdpType,
            sdp: `v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ ${candidate}\r\n`,
          });
        }
        setLocalDescription(description: RTCSessionDescriptionInit) {
          this.localDescription = description;
          senderEncodingAvailable = true;
          return Promise.resolve();
        }
        getStats() {
          return Promise.resolve(new Map());
        }
        close() {
          this.connectionState = "closed";
        }
        connect() {
          this.connectionState = "connected";
          for (const listener of this.listeners.get("connectionstatechange") ?? []) {
            listener();
          }
        }
      }
      vi.stubGlobal("RTCPeerConnection", FakePeerConnection as unknown as typeof RTCPeerConnection);
      const permissionTrack = createTrack("permission", 640, 480);
      const firstTrack = createTrack("front", 1920, 1080);
      const replacementTrack = createTrack("back", 1920, 1080);
      stubMediaDevices(
        vi
          .fn()
          .mockResolvedValueOnce(createStream(permissionTrack))
          .mockResolvedValueOnce(createStream(firstTrack))
          .mockResolvedValueOnce(createStream(replacementTrack)),
      );
      const send = vi.fn<(message: ClientMessage) => void>();
      const clientKey = storeReconnectKey();
      const hostKey = createPairingKeyMaterial();
      if (!hostKey) {
        throw new Error("Host test key is unavailable.");
      }
      renderWorkspace(send, hostKey.reconnectPublicKey, transportMode);
      fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
      await screen.findByLabelText("Camera");
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      const start = await waitFor(() => {
        const message = send.mock.calls
          .map(([entry]) => entry)
          .find((entry) => entry.type === "phone.webcam.start");
        if (message?.type !== "phone.webcam.start") {
          throw new Error("Start was not sent.");
        }
        return message;
      });
      const offerSdp =
        "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\n";
      const offerHash = hashSessionDescription(offerSdp);
      const transcript = `VolturaAir phone-webcam:offer:v2:client-test:${start.operationId}:${offerHash}`;
      const hostSignature = signPrivateKeyPayload(
        hostKey.privateKey,
        new TextEncoder().encode(transcript),
      );
      act(() => {
        publishPhoneWebcamResult({
          type: "phone.webcam.start.result",
          operationId: start.operationId,
          succeeded: true,
          code: "accepted",
          message: "ready",
          offerSdp,
          hostSignature,
          ...(transportMode === "relay"
            ? {
                iceServers: [
                  {
                    urls: ["turns:turn.example.test:5349?transport=tcp"],
                    username: "user",
                    credential: "secret",
                  },
                ],
              }
            : {}),
          maximumBitrate,
        });
      });

      await waitFor(() =>
        expect(send.mock.calls.some(([entry]) => entry.type === "phone.webcam.answer")).toBe(true),
      );
      expect(clientKey.privateKey).toBeTruthy();
      expect(FakePeerConnection.configuration?.iceTransportPolicy).toBe(policy);
      expect(sender.replaceTrack).toHaveBeenCalledWith(firstTrack);
      expect(sender.setParameters).toHaveBeenCalledWith(
        expect.objectContaining({
          degradationPreference: "maintain-resolution",
          encodings: [expect.objectContaining({ maxBitrate: maximumBitrate, maxFramerate: 30 })],
        }),
      );
      act(() => {
        FakePeerConnection.instance?.connect();
      });
      expect(await screen.findByText(`Streaming through ${label}`)).toBeTruthy();

      fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
      await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
      expect(send.mock.calls.filter(([entry]) => entry.type === "phone.webcam.start")).toHaveLength(
        1,
      );
      expect(firstTrack.stop).toHaveBeenCalledOnce();
    },
  );

  it("does not let a stale offer failure stop a replacement session", async () => {
    const firstNegotiation = createDeferred<void>();
    const peers: { close: ReturnType<typeof vi.fn> }[] = [];
    class DeferredPeerConnection {
      readonly iceGatheringState: RTCIceGatheringState = "complete";
      readonly sender = new FakeSender();
      readonly close = vi.fn();
      localDescription: RTCSessionDescriptionInit | null = null;
      constructor() {
        peers.push(this);
      }
      addEventListener() {
        return;
      }
      setRemoteDescription() {
        return peers.length === 1 ? firstNegotiation.promise : Promise.resolve();
      }
      getTransceivers() {
        return [
          { receiver: { track: { kind: "video" } }, sender: this.sender, direction: "recvonly" },
        ];
      }
      createAnswer() {
        return Promise.resolve({
          type: "answer" as RTCSdpType,
          sdp: "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ host\r\n",
        });
      }
      setLocalDescription(description: RTCSessionDescriptionInit) {
        this.localDescription = description;
        return Promise.resolve();
      }
      getStats() {
        return Promise.resolve(new Map());
      }
    }
    vi.stubGlobal(
      "RTCPeerConnection",
      DeferredPeerConnection as unknown as typeof RTCPeerConnection,
    );
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const replacementTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(firstTrack))
        .mockResolvedValueOnce(createStream(replacementTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial();
    if (!hostKey) {
      throw new Error("Host test key is unavailable.");
    }
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const firstStart = await findStart(send, 1);
    publishSignedOffer(firstStart.operationId, hostKey.privateKey);
    await waitFor(() => expect(peers).toHaveLength(1));

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await waitFor(() =>
      expect((screen.getByRole("button", { name: "Start" }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const secondStart = await findStart(send, 2);
    publishSignedOffer(secondStart.operationId, hostKey.privateKey);
    await waitFor(() =>
      expect(
        send.mock.calls.filter(([message]) => message.type === "phone.webcam.answer"),
      ).toHaveLength(1),
    );

    await act(async () => {
      firstNegotiation.reject(new Error("Old negotiation failed."));
      await Promise.resolve();
    });

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
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(replacementTrack));
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });

    expect(replacementTrack.stop).toHaveBeenCalledOnce();
    await act(async () => {
      replacementApplied.resolve();
      await replacementApplied.promise;
    });
    expect(replacementTrack.stop).toHaveBeenCalledOnce();
  });

  it("keeps the existing peer when iOS ends the old track while opening a replacement camera", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const replacementTrack = createTrack("back", 1920, 1080);
    const replacementCamera = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockReturnValueOnce(replacementCamera.promise);
    const { send } = await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    act(() => {
      firstTrack.end();
    });

    expect(screen.queryByText("The selected camera stopped.")).toBeNull();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(0);
    await act(async () => {
      replacementCamera.resolve(createStream(replacementTrack));
      await replacementCamera.promise;
    });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    expect(screen.getByText("Streaming through Enhanced Direct")).toBeTruthy();
  });

  it("retries the requested iOS camera on the existing peer after the handoff temporarily fails", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("back", 1920, 1080);
    const replacementTrack = createTrack("front", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"))
      .mockResolvedValueOnce(createStream(replacementTrack));
    const { send } = await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    act(() => {
      firstTrack.end();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });

    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    expect(sender.replaceTrack).toHaveBeenCalledWith(null);
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("front");
    expect(screen.getByText("Streaming through Enhanced Direct")).toBeTruthy();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
    ).toHaveLength(1);
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(0);
    vi.useRealTimers();
  });

  it("ends microphone capture when an exclusive camera handoff cannot recover", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const microphoneProbe = createAudioTrack();
    const firstTrack = createTrack("back", 1920, 1080);
    const microphone = createAudioTrack();
    const replacementAudio = createAudioTrack();
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createAudioStream(microphoneProbe))
      .mockResolvedValueOnce(createAvStream(firstTrack, microphone))
      .mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"))
      .mockResolvedValueOnce(createAudioStream(replacementAudio));
    const { send } = await establishStreamingSession(getUserMedia, sender, undefined, true);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });

    await waitFor(() =>
      expect(screen.getByText("The active camera stopped while switching cameras.")).toBeTruthy(),
    );
    expect(microphone.stop).toHaveBeenCalledOnce();
    expect(replacementAudio.stop).toHaveBeenCalledOnce();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(1);
    vi.useRealTimers();
  });

  it("does not reopen a replacement camera after Stop during the iOS retry delay", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("back", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"));
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(3));
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });

    expect(getUserMedia).toHaveBeenCalledTimes(3);
    expect(screen.getByText("Ready to start.")).toBeTruthy();
    vi.useRealTimers();
  });

  it("stops a dead active camera when a newer concurrent replacement fails", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const staleTrack = createTrack("back", 1920, 1080);
    const staleCamera = createDeferred<MediaStream>();
    const newestCamera = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockReturnValueOnce(staleCamera.promise)
      .mockReturnValueOnce(newestCamera.promise);
    const { send } = await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    act(() => {
      firstTrack.end();
    });
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await act(async () => {
      newestCamera.reject(new DOMException("Unavailable", "OverconstrainedError"));
      await Promise.resolve();
    });

    await waitFor(() =>
      expect(screen.getByText("The active camera stopped while switching cameras.")).toBeTruthy(),
    );
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(1);
    expect((screen.getByRole("button", { name: "Start" }) as HTMLButtonElement).disabled).toBe(
      true,
    );

    await act(async () => {
      staleCamera.resolve(createStream(staleTrack));
      await staleCamera.promise;
    });
    expect(staleTrack.stop).toHaveBeenCalledOnce();
  });

  it("keeps the newest camera when concurrent replacements complete in reverse order", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const olderTrack = createTrack("back", 1920, 1080);
    const newerTrack = createTrack("front-new", 1920, 1080);
    const olderCamera = createDeferred<MediaStream>();
    const newerCamera = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockReturnValueOnce(olderCamera.promise)
      .mockReturnValueOnce(newerCamera.promise);
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await act(async () => {
      newerCamera.resolve(createStream(newerTrack));
      await newerCamera.promise;
    });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(newerTrack));
    await act(async () => {
      olderCamera.resolve(createStream(olderTrack));
      await olderCamera.promise;
    });

    expect(olderTrack.stop).toHaveBeenCalledOnce();
    expect(newerTrack.stop).not.toHaveBeenCalled();
  });

  it.each([false, true])(
    "restores the committed sender before the newest switch settles (new camera opens: %s)",
    async (opensNewest) => {
      const first = createTrack("front", 1920, 1080);
      const superseded = createTrack("back", 1920, 1080);
      const newest = createTrack("front-new", 1920, 1080);
      const attaching = createDeferred<void>();
      const restoring = createDeferred<void>();
      const sender = new FakeSender();
      sender.replaceTrack.mockResolvedValueOnce(undefined)
        .mockReturnValueOnce(attaching.promise)
        .mockReturnValueOnce(restoring.promise);
      const getUserMedia = vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(first))
        .mockResolvedValueOnce(createStream(superseded));
      if (opensNewest) {
        getUserMedia.mockResolvedValueOnce(createStream(newest));
      } else {
        getUserMedia.mockRejectedValueOnce(new DOMException("Camera denied", "NotAllowedError"));
      }
      const { send } = await establishStreamingSession(getUserMedia, sender);
      fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
      await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(superseded));
      await act(() => {
        fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
        return Promise.resolve();
      });
      expect(sender.replaceTrack).toHaveBeenCalledTimes(2);
      expect(superseded.stop).not.toHaveBeenCalled();
      await act(() => {
        attaching.resolve();
        return attaching.promise;
      });
      await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(first));
      expect(sender.replaceTrack).toHaveBeenCalledTimes(3);
      await act(() => {
        restoring.resolve();
        return restoring.promise;
      });
      await waitFor(() => expect(superseded.stop).toHaveBeenCalledOnce());
      if (opensNewest) {
        await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(newest));
        expect(first.stop).toHaveBeenCalledOnce();
      } else {
        expect(sender.replaceTrack).toHaveBeenCalledTimes(3);
        expect(first.stop).not.toHaveBeenCalled();
      }
      expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    },
  );

  it("ends microphone capture if a newer camera request fails after exclusive detach begins", async () => {
    const mic = createAudioTrack();
    const active = createTrack("front", 1920, 1080);
    const detach = createDeferred<void>();
    const sender = new FakeSender();
    sender.replaceTrack.mockResolvedValueOnce(undefined).mockReturnValueOnce(detach.promise);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
      .mockResolvedValueOnce(createAvStream(active, mic))
      .mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"))
      .mockRejectedValueOnce(new DOMException("Camera denied", "NotAllowedError"));
    const { send } = await establishStreamingSession(getUserMedia, sender, undefined, true);
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(null));
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    await screen.findByText("The selected camera could not be opened after releasing the previous camera.");
    expect(mic.stop).toHaveBeenCalledOnce();
    expect(active.stop).toHaveBeenCalledOnce();
    await act(() => {
      detach.resolve();
      return detach.promise;
    });
    expect(getUserMedia).toHaveBeenCalledTimes(5);
    expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop")).toHaveLength(1);
  });

  it("lets a newer switch finish when a previously replaced camera ends", async () => {
    const first = createTrack("front", 1920, 1080);
    const second = createTrack("back", 1920, 1080);
    const third = createTrack("front-new", 1920, 1080);
    const opening = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const { send } = await establishStreamingSession(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(first))
        .mockResolvedValueOnce(createStream(second))
        .mockReturnValueOnce(opening.promise),
      sender,
    );
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(first.stop).toHaveBeenCalledOnce());
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });
    act(() => {
      second.end();
    });
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    await act(() => {
      opening.resolve(createStream(third));
      return opening.promise;
    });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(third));
    await screen.findByText("Streaming through Enhanced Direct");
    expect(third.stop).not.toHaveBeenCalled();
  });

  it("does not display the previous camera's quality for a new camera without settings", async () => {
    const first = createTrack("front", 1920, 1080);
    const next = createTrack("back", 1280, 720);
    Object.defineProperty(next, "getSettings", { value: () => ({ deviceId: "back" }) });
    await establishStreamingSession(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(first))
        .mockResolvedValueOnce(createStream(next)),
      new FakeSender(),
    );
    expect(screen.getByRole("status").textContent).toContain("1920×1080");
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(first.stop).toHaveBeenCalledOnce());
    expect(screen.getByRole("status").textContent).not.toContain("1920×1080");
  });

  it("keeps the active camera selected when its replacement is rejected", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockRejectedValueOnce(new DOMException("Unavailable", "OverconstrainedError"));
    await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });

    expect(
      await screen.findByText("The selected camera could not replace the active camera."),
    ).toBeTruthy();
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("front");
    expect(firstTrack.stop).not.toHaveBeenCalled();
  });

  it("keeps the original stream when opening a replacement stalls and stops the late camera", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1920, 1080);
    const lateTrack = createTrack("back", 1920, 1080);
    const opening = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const { send } = await establishStreamingSession(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(active))
        .mockReturnValueOnce(opening.promise),
      sender,
    );
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(screen.getByRole("status").textContent).toContain("The selected camera did not open in time.");
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("front");
    await act(() => {
      opening.resolve(createStream(lateTrack));
      return opening.promise;
    });
    expect(lateTrack.stop).toHaveBeenCalledOnce();
    expect(active.stop).not.toHaveBeenCalled();
    expect(sender.replaceTrack).toHaveBeenCalledOnce();
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    vi.useRealTimers();
  });

  it.each(["attach", "detach", "rollback"] as const)(
    "closes the current peer and releases the camera when a sender %s stalls",
    async (step) => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const active = createTrack("front", 1920, 1080);
      const next = createTrack("back", 1920, 1080);
      const update = createDeferred<void>();
      const sender = new FakeSender();
      sender.replaceTrack.mockResolvedValueOnce(undefined);
      if (step === "rollback") {
        sender.replaceTrack.mockImplementationOnce(() => {
          Object.assign(next, { readyState: "ended" });
          return Promise.resolve();
        });
      }
      sender.replaceTrack.mockReturnValueOnce(update.promise);
      const getUserMedia = vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(active));
      if (step === "detach") {
        getUserMedia.mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"));
      } else {
        getUserMedia.mockResolvedValueOnce(createStream(next));
      }
      const { send } = await establishStreamingSession(getUserMedia, sender);
      fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
      await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledTimes(step === "rollback" ? 3 : 2));
      await act(() => vi.advanceTimersByTimeAsync(5_000));
      expect(screen.getByRole("status").textContent).toContain("The camera switch timed out.");
      expect(active.stop).toHaveBeenCalledOnce();
      if (step !== "detach") {
        expect(next.stop).toHaveBeenCalledOnce();
      }
      expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop")).toHaveLength(1);
      await act(() => {
        update.resolve();
        return update.promise;
      });
      expect(screen.getByRole("status").textContent).toContain("The camera switch timed out.");
      expect(getUserMedia).toHaveBeenCalledTimes(3);
      vi.useRealTimers();
    },
  );

  it("keeps the Stop status when a canceled rollback finishes late", async () => {
    const active = createTrack("front", 1920, 1080);
    const next = createTrack("back", 1920, 1080);
    const rollback = createDeferred<void>();
    const sender = new FakeSender();
    sender.replaceTrack.mockResolvedValueOnce(undefined).mockImplementationOnce(() => {
      Object.assign(next, { readyState: "ended" });
      return Promise.resolve();
    }).mockReturnValueOnce(rollback.promise);
    const { send } = await establishStreamingSession(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(active))
        .mockResolvedValueOnce(createStream(next)),
      sender,
    );
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledTimes(3));
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    const stoppedStatus = screen.getByRole("status").textContent;
    await act(() => {
      rollback.reject(new Error("Browser completed after Stop"));
      return Promise.resolve();
    });
    expect(stoppedStatus).toContain("Ready to start.");
    expect(screen.getByRole("status").textContent).toBe(stoppedStatus);
    expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop")).toHaveLength(1);
    expect(active.stop).toHaveBeenCalledOnce();
    expect(next.stop).toHaveBeenCalledOnce();
  });

  it("refreshes the selected camera on the existing peer after orientation changes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const refreshedTrack = createTrack("front", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(refreshedTrack));
    const { send } = await establishStreamingSession(getUserMedia, sender);

    act(() => {
      window.dispatchEvent(new Event("orientationchange"));
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(refreshedTrack));
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
    ).toHaveLength(1);
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(0);
    vi.useRealTimers();
  });

  it("does not let a queued orientation refresh override a newer camera selection", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const replacementTrack = createTrack("back", 1920, 1080);
    const replacementCamera = createDeferred<MediaStream>();
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockReturnValueOnce(replacementCamera.promise);
    await establishStreamingSession(getUserMedia, sender);

    act(() => {
      window.dispatchEvent(new Event("orientationchange"));
    });
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(getUserMedia).toHaveBeenCalledTimes(3);
    await act(async () => {
      replacementCamera.resolve(createStream(replacementTrack));
      await replacementCamera.promise;
    });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("back");
    vi.useRealTimers();
  });

  it("ignores rotation that occurs after a manual camera switch has begun", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const replacement = createDeferred<MediaStream>();
    const next = createTrack("back", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(createTrack("front", 1920, 1080)))
      .mockReturnValueOnce(replacement.promise);
    await establishStreamingSession(getUserMedia, sender);
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    act(() => {
      window.dispatchEvent(new Event("orientationchange"));
    });
    await act(() => vi.advanceTimersByTimeAsync(500));
    expect(getUserMedia).toHaveBeenCalledTimes(3);
    await act(() => {
      replacement.resolve(createStream(next));
      return replacement.promise;
    });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenLastCalledWith(next));
    expect((screen.getByLabelText("Camera") as HTMLSelectElement).value).toBe("back");
    vi.useRealTimers();
  });

  it.each(["front", ""])("restores a stalled camera on the existing peer with a reported device ID of %j", async (reportedId) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack(reportedId, 1080, 1920);
    const restoredTrack = createTrack("front", 1080, 1920);
    const sender = new FakeSender();
    const getStats = vi.fn(() =>
      Promise.resolve(
        new Map([
          [
            "video",
            {
              type: "outbound-rtp",
              kind: "video",
              bytesSent: 10_000,
              framesEncoded: 300,
            },
          ],
        ]),
      ),
    );
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(restoredTrack));
    const { send } = await establishStreamingSession(getUserMedia, sender, getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(4_000);
    });

    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(restoredTrack));
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
    ).toHaveLength(1);
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(0);
    vi.useRealTimers();
  });

  it("does not refresh a camera whose encoded frames keep advancing", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const sender = new FakeSender();
    let sample = 0;
    const getStats = vi.fn(() => {
      sample += 1;
      return Promise.resolve(
        new Map([
          [
            "video",
            {
              type: "outbound-rtp",
              kind: "video",
              bytesSent: 10_000 + sample * 1_000,
              framesEncoded: 300 + sample * 30,
            },
          ],
        ]),
      );
    });
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack));
    await establishStreamingSession(getUserMedia, sender, getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(12_000);
    });

    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(firstTrack.stop).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it.each(["pending", "completed"])(
    "pauses quality polling across a disconnect with a %s sample",
    async (sampleStatus) => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const permissionTrack = createTrack("permission", 640, 480);
      const activeTrack = createTrack("front", 1080, 1920);
      const report = new Map<string, unknown>();
      const sample = createDeferred<Map<string, unknown>>();
      const getStats = vi.fn<() => Promise<Map<string, unknown>>>()
        .mockReturnValueOnce(sampleStatus === "pending" ? sample.promise : Promise.resolve(report))
        .mockResolvedValue(report);
      const getUserMedia = vi.fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(activeTrack));
      const { setPeerState } = await establishStreamingSession(
        getUserMedia, new FakeSender(), getStats,
      );
      await act(() => vi.advanceTimersByTimeAsync(1_000));
      expect(getStats).toHaveBeenCalledOnce();

      act(() => setPeerState("disconnected"));
      await act(() => vi.advanceTimersByTimeAsync(4_000));
      expect(getStats).toHaveBeenCalledOnce();
      act(() => setPeerState("connected"));
      await act(() => vi.advanceTimersByTimeAsync(1_000));
      if (sampleStatus === "pending") {
        expect(getStats).toHaveBeenCalledOnce();
        await act(async () => {
          sample.resolve(report);
          await sample.promise;
        });
        await act(() => vi.advanceTimersByTimeAsync(1_000));
      }
      expect(getStats).toHaveBeenCalledTimes(2);
      expect(activeTrack.stop).not.toHaveBeenCalled();
      vi.useRealTimers();
    },
  );

  it("stops local capture when the webcam media peer cannot recover", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const activeTrack = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(activeTrack));
    const { send, setPeerState } = await establishStreamingSession(getUserMedia, new FakeSender());
    vi.useFakeTimers();
    act(() => setPeerState("disconnected"));
    await act(() => vi.advanceTimersByTimeAsync(7_999));
    expect(activeTrack.stop).not.toHaveBeenCalled();

    await act(() => vi.advanceTimersByTimeAsync(1));
    expect(activeTrack.stop).toHaveBeenCalledOnce();
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    expect(screen.getByText("Reconnecting")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
    vi.useRealTimers();
  });

  it.each(["secure-direct", "relay"] as const)("resumes an expanded %s camera view after the host ends the current transport", async (transportMode) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const first = createTrack("front", 1080, 1920);
    const resumed = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(first))
      .mockResolvedValueOnce(createStream(resumed));
    const { send, hostPrivateKey, setPeerState } = await establishStreamingSession(
      getUserMedia,
      new FakeSender(),
      undefined,
      false,
      transportMode,
    );
    const initial = await findStart(send, 1);
    fireEvent.click(screen.getByRole("button", { name: "Expand camera view" }));
    act(() => {
      publishPhoneWebcamResult({ type: "phone.webcam.ended", operationId: initial.operationId,
        reason: "transport-lost", message: "Video transport interrupted." });
    });
    expect(screen.getByRole("status").textContent).toContain("Video transport interrupted.");
    expect(first.stop).toHaveBeenCalledOnce();
    await act(() => vi.advanceTimersByTimeAsync(250));
    const restart = await findStart(send, 2);
    expect(restart.operationId).not.toBe(initial.operationId);
    expect(getUserMedia.mock.lastCall?.[0]).toMatchObject({
      audio: false, video: { deviceId: { exact: "front" } },
    });
    publishSignedOffer(restart.operationId, hostPrivateKey, false, transportMode === "relay" ? relayOffer : {});
    await waitFor(() => expect(send).toHaveBeenCalledWith(expect.objectContaining({
      type: "phone.webcam.answer", operationId: restart.operationId,
    })));
    act(() => setPeerState("connected", 1));
    expect(screen.getByRole("button", { name: "Restore camera view" })).toBeTruthy();
    expect(screen.queryByText("Video transport interrupted.")).toBeNull();
    expect(resumed.stop).not.toHaveBeenCalled();
  });

  it("uses the Relay credential lifetime when the phone clock is ahead", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date("2040-01-01T01:00:00Z"));
    const first = createTrack("front", 1080, 1920);
    const resumed = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(first))
      .mockResolvedValueOnce(createStream(resumed));
    const { send } = await establishStreamingSession(getUserMedia, new FakeSender(), undefined, false, "relay", {
      relayUsageCheckedAt: "2040-01-01T00:00:00Z",
      turnExpiresAt: "2040-01-01T00:01:15Z",
    });
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(first.stop).not.toHaveBeenCalled();
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    await act(() => vi.advanceTimersByTimeAsync(5_000));
    expect(first.stop).toHaveBeenCalledOnce();
    await act(() => vi.advanceTimersByTimeAsync(250));
    await findStart(send, 2);
    expect(getUserMedia).toHaveBeenCalledTimes(3);
  });

  it.each([
    { gathering: "gathering", delay: 10_000, message: "The browser timed out finding a route" },
    { gathering: "complete", delay: 0, message: "The browser could not find a Relay route" },
  ] as const)("retries fullscreen Relay recovery when ICE is $gathering without any candidates", async ({ gathering, delay, message }) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permission = createTrack("probe", 640, 480);
    const original = createTrack("front", 1080, 1920);
    const interrupted = createTrack("front", 1080, 1920);
    const restored = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(permission))
      .mockResolvedValueOnce(createStream(original))
      .mockResolvedValueOnce(createStream(interrupted))
      .mockResolvedValueOnce(createStream(restored));
    const { send, hostPrivateKey, setPeerState, setNextPeerRoute } = await establishStreamingSession(
      getUserMedia, new FakeSender(), undefined, false, "relay",
    );
    fireEvent.click(screen.getByRole("button", { name: "Expand camera view" }));
    setNextPeerRoute(gathering, null);
    act(() => setPeerState("failed"));
    await act(() => vi.advanceTimersByTimeAsync(250));
    const failedRestart = await findStart(send, 2);
    publishSignedOffer(failedRestart.operationId, hostPrivateKey, false, relayOffer);
    await act(() => vi.advanceTimersByTimeAsync(delay));
    expect(screen.getByRole("status").textContent).toContain(message);
    expect(interrupted.stop).toHaveBeenCalledOnce();
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({
      type: "phone.webcam.answer", operationId: failedRestart.operationId,
    }));

    setNextPeerRoute("complete", "relay");
    await act(() => vi.advanceTimersByTimeAsync(1_000));
    const nextRestart = await findStart(send, 3);
    publishSignedOffer(nextRestart.operationId, hostPrivateKey, false, relayOffer);
    await waitFor(() => expect(send).toHaveBeenCalledWith(expect.objectContaining({
      type: "phone.webcam.answer", operationId: nextRestart.operationId,
    })));
    act(() => setPeerState("connected", 2));
    expect(screen.getByRole("button", { name: "Restore camera view" })).toBeTruthy();
    expect(restored.stop).not.toHaveBeenCalled();
  });

  it("respects a browser camera pause and resumes after unmute without reopening capture", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active));
    const { send } = await establishStreamingSession(getUserMedia, new FakeSender());
    fireEvent.click(screen.getByRole("button", { name: "Expand camera view" }));
    act(() => active.setMuted(true));
    await act(() => vi.advanceTimersByTimeAsync(14_000));
    expect(screen.getByRole("status").textContent).toBe("Camera paused by the browser.");
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(active.stop).not.toHaveBeenCalled();
    act(() => active.setMuted(false));
    expect(screen.queryByRole("status")).toBeNull();
    expect(send.mock.calls.filter(([message]) => message.type === "phone.webcam.start")).toHaveLength(1);
  });

  it("stops retrying after four failed automatic capture attempts", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active))
      .mockRejectedValue(new DOMException("Busy camera", "NotReadableError"));
    await establishStreamingSession(getUserMedia, new FakeSender());
    act(() => active.end());
    for (const delay of [250, 1000, 2500, 5000]) {
      await act(() => vi.advanceTimersByTimeAsync(delay));
    }
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    expect(getUserMedia).toHaveBeenCalledTimes(6);
    expect(screen.getByRole("status").textContent).toContain("Tap Start to try again.");
  });

  it("does not overlap automatic capture when the browser camera request times out", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1080, 1920);
    const late = createTrack("front", 1080, 1920);
    const nativeCapture = createDeferred<MediaStream>();
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active))
      .mockReturnValueOnce(nativeCapture.promise);
    await establishStreamingSession(getUserMedia, new FakeSender());
    act(() => active.end());
    await act(() => vi.advanceTimersByTimeAsync(250));
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(getUserMedia).toHaveBeenCalledTimes(3);
    expect(screen.getByRole("status").textContent).toContain("Tap Start to try again.");
    await act(() => {
      nativeCapture.resolve(createStream(late));
      return nativeCapture.promise;
    });
    expect(late.stop).toHaveBeenCalledOnce();
  });

  it.each(["backoff", "replacement", "stable replacement"] as const)("honors a delayed Windows Stop during %s", async (stage) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1080, 1920);
    const replacement = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active))
      .mockResolvedValueOnce(createStream(replacement));
    const { send, hostPrivateKey, setPeerState } = await establishStreamingSession(getUserMedia, new FakeSender());
    const initial = await findStart(send, 1);
    act(() => setPeerState("failed"));
    act(() => publishPhoneWebcamResult({
      type: "phone.webcam.ended", operationId: initial.operationId, reason: "stopped", message: "Phone webcam stopped.",
    }));
    if (stage !== "backoff") {
      await act(() => vi.advanceTimersByTimeAsync(250));
      const restarted = await findStart(send, 2);
      if (stage === "stable replacement") {
        publishSignedOffer(restarted.operationId, hostPrivateKey);
        await waitFor(() => expect(send).toHaveBeenCalledWith(expect.objectContaining({
          type: "phone.webcam.answer", operationId: restarted.operationId,
        })));
        act(() => setPeerState("connected", 1));
        await act(() => vi.advanceTimersByTimeAsync(16_000));
      }
    }
    act(() => publishPhoneWebcamResult({
      type: "phone.webcam.ended", operationId: initial.operationId, reason: "host-stopped", message: "Stopped on the PC.",
    }));
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(getUserMedia).toHaveBeenCalledTimes(stage === "backoff" ? 2 : 3);
    expect(replacement.stop).toHaveBeenCalledTimes(stage === "backoff" ? 0 : 1);
    expect(screen.getByRole("status").textContent).toContain("Stopped on the PC.");
  });

  it("cancels queued webcam recovery from the expanded preview", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1920, 1080);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active));
    const { setPeerState } = await establishStreamingSession(getUserMedia, new FakeSender());
    fireEvent.click(screen.getByRole("button", { name: "Expand camera view" }));
    act(() => setPeerState("failed"));
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("status").textContent).toBe("Ready to start.");
  });

  it.each(["host-stopped", "permission-revoked", "pairing-revoked"] as const)(
    "does not resume after the host reports %s",
    async (reason) => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const active = createTrack("front", 1920, 1080);
      const getUserMedia = vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(active));
      const { send } = await establishStreamingSession(getUserMedia, new FakeSender());
      const initial = await findStart(send, 1);
      act(() => {
        publishPhoneWebcamResult({ type: "phone.webcam.ended", operationId: initial.operationId,
          reason, message: "The PC stopped Phone webcam." });
      });
      await act(() => vi.advanceTimersByTimeAsync(30_000));
      expect(getUserMedia).toHaveBeenCalledTimes(2);
      expect(active.stop).toHaveBeenCalledOnce();
    },
  );

  it("keeps the microphone muted when recovery opens fresh audio", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const originalMic = createAudioTrack();
    const resumedMic = createAudioTrack();
    const { send, hostPrivateKey, setPeerState } = await establishStreamingSession(
      vi.fn()
        .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
        .mockResolvedValueOnce(createAvStream(createTrack("front", 1920, 1080), originalMic))
        .mockResolvedValueOnce(createAvStream(createTrack("front", 1920, 1080), resumedMic)),
      new FakeSender(), undefined, true,
    );
    fireEvent.click(screen.getByRole("button", { name: "Mute" }));
    expect(originalMic.enabled).toBe(false);
    act(() => setPeerState("failed"));
    await act(() => vi.advanceTimersByTimeAsync(250));
    const restart = await findStart(send, 2);
    expect(restart.useMicrophone).toBe(true);
    expect(resumedMic.enabled).toBe(false);
    publishSignedOffer(restart.operationId, hostPrivateKey, true);
    await waitFor(() => expect(send).toHaveBeenCalledWith(expect.objectContaining({
      type: "phone.webcam.answer", operationId: restart.operationId,
    })));
    expect(screen.getByRole("button", { name: "Unmute" })).toBeTruthy();
  });

  it.each(["phone", "PC"] as const)("restores video without reopening the microphone after %s audio loss", async (side) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const microphone = createAudioTrack();
    const restored = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
      .mockResolvedValueOnce(createAvStream(createTrack("front", 1080, 1920), microphone))
      .mockResolvedValueOnce(createStream(restored));
    const { send } = await establishStreamingSession(getUserMedia, new FakeSender(), undefined, true);
    const initial = await findStart(send, 1);
    act(() => {
      if (side === "phone") {
        microphone.end();
      } else {
        publishPhoneWebcamResult({ type: "phone.webcam.ended", operationId: initial.operationId,
          reason: "audio-failed", message: "The PC microphone output stopped." });
      }
    });
    expect(microphone.stop).toHaveBeenCalledOnce();
    await act(() => vi.advanceTimersByTimeAsync(250));
    const restart = await findStart(send, 2);
    expect(restart.useMicrophone).toBe(false);
    expect(getUserMedia.mock.lastCall?.[0]).toMatchObject({ audio: false });
  });

  it.each(["before retry", "during retry", "after reconnect"] as const)(
    "handles a delayed PC microphone failure %s",
    async (stage) => {
      const retryMicrophone = createAudioTrack();
      const getUserMedia = vi.fn<(constraints: MediaStreamConstraints) => Promise<MediaStream>>()
        .mockResolvedValueOnce(createStream(createTrack("probe", 640, 480)))
        .mockResolvedValueOnce(createAudioStream(createAudioTrack()))
        .mockResolvedValueOnce(createAvStream(createTrack("front", 1080, 1920), createAudioTrack()))
        .mockImplementation(({ audio }) => {
          const camera = createTrack("front", 1080, 1920);
          return Promise.resolve(audio ? createAvStream(camera, retryMicrophone) : createStream(camera));
        });
      const { send, hostPrivateKey, setPeerState } = await establishStreamingSession(
        getUserMedia,
        new FakeSender(),
        undefined,
        true,
      );
      const initial = await findStart(send, 1);
      const starts = () => send.mock.calls
        .map(([message]) => message)
        .filter((message) => message.type === "phone.webcam.start");
      vi.useFakeTimers();
      act(() => setPeerState("failed"));

      if (stage !== "before retry") {
        await act(() => vi.advanceTimersByTimeAsync(250));
        const retry = starts()[1];
        if (!retry) {
          throw new Error(`Missing initial webcam retry. ${screen.getByRole("status").textContent}`);
        }
        expect(retry.useMicrophone).toBe(true);
        if (stage === "after reconnect") {
          await act(() => {
            publishSignedOffer(retry.operationId, hostPrivateKey, true);
            return Promise.resolve();
          });
          act(() => setPeerState("connected", 1));
        }
      }
      act(() => publishPhoneWebcamResult({
        type: "phone.webcam.ended",
        operationId: initial.operationId,
        reason: "audio-failed",
        message: "The PC microphone output stopped.",
      }));

      if (stage === "after reconnect") {
        await act(() => vi.advanceTimersByTimeAsync(1_000));
        expect(retryMicrophone.stop).not.toHaveBeenCalled();
        expect(getUserMedia).toHaveBeenCalledTimes(4);
        expect(screen.getByText("Streaming through Enhanced Direct")).toBeTruthy();
        return;
      }
      await act(() => vi.advanceTimersByTimeAsync(stage === "before retry" ? 250 : 1_000));
      const restart = starts()[stage === "before retry" ? 1 : 2];
      expect(restart).toMatchObject({ useMicrophone: false });
      expect(getUserMedia.mock.lastCall?.[0]?.audio).toBe(false);
      if (stage === "during retry") {
        expect(retryMicrophone.stop).toHaveBeenCalledOnce();
      }
    },
  );

  it("starts a new stall-detection interval when the encoder resets its counters", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const activeTrack = createTrack("front", 1080, 1920);
    const sender = new FakeSender();
    const frameCounts = [300, 300, 300, 20, 25];
    const getStats = vi.fn(() => {
      const framesEncoded = frameCounts.shift() ?? 30;
      return Promise.resolve(
        new Map([
          [
            "video",
            { type: "outbound-rtp", kind: "video", bytesSent: framesEncoded * 100, framesEncoded },
          ],
        ]),
      );
    });
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(activeTrack));
    await establishStreamingSession(getUserMedia, sender, getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(activeTrack.stop).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("continues quality polling after a synchronous browser statistics failure", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const activeTrack = createTrack("front", 1080, 1920);
    const getStats = vi
      .fn<() => Promise<Map<string, unknown>>>()
      .mockImplementationOnce(() => {
        throw new Error("Statistics temporarily unavailable");
      })
      .mockResolvedValue(
        new Map([
          ["video", { type: "outbound-rtp", kind: "video", bytesSent: 5_000, frameWidth: 640, frameHeight: 360 }],
        ]),
      );
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(activeTrack));
    await establishStreamingSession(getUserMedia, new FakeSender(), getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000);
    });

    expect(getStats).toHaveBeenCalledTimes(2);
    expect(screen.getByText(/640×360/)).toBeTruthy();
    vi.useRealTimers();
  });

  it("shows encoded quality from browser reports using the legacy video media type", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const activeTrack = createTrack("front", 1080, 1920);
    const getStats = vi.fn(() =>
      Promise.resolve(
        new Map([
          ["video", { type: "outbound-rtp", mediaType: "video", bytesSent: 5_000, frameWidth: 640, frameHeight: 360 }],
        ]),
      ),
    );
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(activeTrack));
    await establishStreamingSession(getUserMedia, new FakeSender(), getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000);
    });

    expect(screen.getByText(/640×360/)).toBeTruthy();
    vi.useRealTimers();
  });

  it("shows dimensions and FPS from one outbound video row and skips invalid counters", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const active = createTrack("front", 1080, 1920);
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
      .mockResolvedValueOnce(createStream(active));
    const outbound = { type: "outbound-rtp", kind: "video" };
    const rows = new Map<string, unknown>([
      ["portrait", { ...outbound, frameWidth: 1080, frameHeight: 1920, framesPerSecond: 20, bytesSent: 100_000 }],
      ["landscape", { ...outbound, frameWidth: 1920, frameHeight: 1080, framesPerSecond: 30, bytesSent: 30_000 }],
      ["invalid", { ...outbound, frameWidth: Infinity, frameHeight: Infinity, bytesSent: NaN }],
    ]);
    await establishStreamingSession(getUserMedia, new FakeSender(), () => Promise.resolve(rows));
    await act(() => vi.advanceTimersByTimeAsync(1_000));
    expect(screen.getByRole("status").textContent).toContain("1080×1920 at 20.0 fps");
  });

  it("treats multiple outbound RTP rows as one stalled report sample", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const restoredTrack = createTrack("front", 1080, 1920);
    const sender = new FakeSender();
    const getStats = vi.fn(() =>
      Promise.resolve(
        new Map([
          [
            "primary",
            {
              type: "outbound-rtp",
              kind: "video",
              active: true,
              bytesSent: 10_000,
              framesEncoded: 300,
            },
          ],
          [
            "secondary",
            {
              type: "outbound-rtp",
              kind: "video",
              active: true,
              bytesSent: 5_000,
              framesEncoded: 150,
            },
          ],
        ]),
      ),
    );
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(restoredTrack));
    await establishStreamingSession(getUserMedia, sender, getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(4_000);
    });

    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(restoredTrack));
    expect(getStats).toHaveBeenCalledTimes(4);
    vi.useRealTimers();
  });

  it("expires stalled quality readings without overlapping native polls or stopping video", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const pending = createDeferred<Map<string, unknown>>();
    const active = createTrack("front", 1920, 1080);
    const sample = (width: number, bytesSent: number) => new Map<string, unknown>([
      ["video", { type: "outbound-rtp", kind: "video", frameWidth: width, frameHeight: 360, framesPerSecond: 24, bytesSent }],
    ]);
    const getStats = vi.fn()
      .mockResolvedValueOnce(sample(640, 1_000))
      .mockResolvedValueOnce(sample(640, 100_000))
      .mockReturnValueOnce(pending.promise)
      .mockResolvedValue(sample(800, 200_000));
    const { send } = await establishStreamingSession(
      vi.fn().mockResolvedValueOnce(createStream(createTrack("permission", 640, 480)))
        .mockResolvedValueOnce(createStream(active)),
      new FakeSender(),
      getStats,
    );
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    expect(screen.getByRole("status").textContent).toMatch(/24.0 fps.*Mbps/);
    await act(() => vi.advanceTimersByTimeAsync(5_500));
    expect(screen.getByRole("status").textContent).not.toMatch(/fps|Mbps/);
    expect(getStats).toHaveBeenCalledTimes(3);
    await act(() => {
      pending.resolve(sample(320, 99_000_000));
      return pending.promise;
    });
    expect(screen.getByRole("status").textContent).not.toContain("320×360");
    await act(() => vi.advanceTimersByTimeAsync(1_000));
    expect(screen.getByRole("status").textContent).toContain("800×360");
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "phone.webcam.stop" }));
    expect(active.stop).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("serializes stats polls and ignores a poll completed after camera replacement", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const replacementTrack = createTrack("back", 1920, 1080);
    const deferredStats = createDeferred<Map<string, unknown>>();
    const sender = new FakeSender();
    let laterSample = 0;
    const getStats = vi
      .fn()
      .mockReturnValueOnce(deferredStats.promise)
      .mockImplementation(() => {
        laterSample += 1;
        return Promise.resolve(
          new Map<string, unknown>([
            [
              "video",
              {
                type: "outbound-rtp",
                kind: "video",
                bytesSent: 10_000 + laterSample * 1_000,
                framesEncoded: 300 + laterSample * 30,
              },
            ],
          ]),
        );
      });
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockResolvedValueOnce(createStream(replacementTrack));
    await establishStreamingSession(getUserMedia, sender, getStats);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(getStats).toHaveBeenCalledOnce();
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(replacementTrack));
    await act(async () => {
      deferredStats.resolve(
        new Map<string, unknown>([
          [
            "video",
            {
              type: "outbound-rtp",
              kind: "video",
              bytesSent: 9_000_000,
              framesEncoded: 90_000,
            },
          ],
        ]),
      );
      await deferredStats.promise;
      await vi.advanceTimersByTimeAsync(12_000);
    });

    expect(getUserMedia).toHaveBeenCalledTimes(3);
    expect(replacementTrack.stop).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("ignores a stats poll completed after stopping and starting a new session", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const deferredStats = createDeferred<Map<string, unknown>>();
    const peers: RestartPeerConnection[] = [];
    let currentSample = 0;
    class RestartPeerConnection {
      readonly iceGatheringState: RTCIceGatheringState = "complete";
      readonly listeners = new Map<string, (() => void)[]>();
      readonly sender = new FakeSender();
      readonly sessionIndex: number;
      connectionState: RTCPeerConnectionState = "new";
      localDescription: RTCSessionDescriptionInit | null = null;
      constructor() {
        this.sessionIndex = peers.length;
        peers.push(this);
      }
      addEventListener(type: string, listener: () => void) {
        this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
      }
      setRemoteDescription() {
        return Promise.resolve();
      }
      getTransceivers() {
        return [
          { receiver: { track: { kind: "video" } }, sender: this.sender, direction: "recvonly" },
        ];
      }
      createAnswer() {
        return Promise.resolve({
          type: "answer" as RTCSdpType,
          sdp: "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\na=candidate:1 1 udp 1 192.0.2.20 5000 typ host\r\n",
        });
      }
      setLocalDescription(description: RTCSessionDescriptionInit) {
        this.localDescription = description;
        return Promise.resolve();
      }
      getStats() {
        if (this.sessionIndex === 0) {
          return deferredStats.promise;
        }
        currentSample += 1;
        return Promise.resolve(
          new Map<string, unknown>([
            [
              "video",
              {
                type: "outbound-rtp",
                kind: "video",
                bytesSent: currentSample * 1_000,
                framesEncoded: currentSample * 30,
              },
            ],
          ]),
        );
      }
      close() {
        this.connectionState = "closed";
      }
      connect() {
        this.connectionState = "connected";
        for (const listener of this.listeners.get("connectionstatechange") ?? []) {
          listener();
        }
      }
    }
    vi.stubGlobal(
      "RTCPeerConnection",
      RestartPeerConnection as unknown as typeof RTCPeerConnection,
    );
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1080, 1920);
    const restartedTrack = createTrack("front", 1080, 1920);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(firstTrack))
        .mockResolvedValueOnce(createStream(restartedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const hostKey = createPairingKeyMaterial();
    if (!hostKey) {
      throw new Error("Host test key is unavailable.");
    }
    renderWorkspace(send, hostKey.reconnectPublicKey);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const firstStart = await findStart(send, 1);
    publishSignedOffer(firstStart.operationId, hostKey.privateKey);
    await waitFor(() => expect(peers).toHaveLength(1));
    act(() => {
      peers[0]?.connect();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000);
    });

    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const secondStart = await findStart(send, 2);
    publishSignedOffer(secondStart.operationId, hostKey.privateKey);
    await waitFor(() => expect(peers).toHaveLength(2));
    act(() => {
      peers[1]?.connect();
    });
    await act(async () => {
      deferredStats.resolve(
        new Map<string, unknown>([
          [
            "video",
            {
              type: "outbound-rtp",
              kind: "video",
              bytesSent: 9_000_000,
              framesEncoded: 90_000,
            },
          ],
        ]),
      );
      await deferredStats.promise;
      await vi.advanceTimersByTimeAsync(12_000);
    });

    expect(restartedTrack.stop).not.toHaveBeenCalled();
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.start"),
    ).toHaveLength(2);
    vi.useRealTimers();
  });

  it("ends the webcam session when a superseding switch fails after the active camera was released", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const permissionTrack = createTrack("permission", 640, 480);
    const firstTrack = createTrack("front", 1920, 1080);
    const sender = new FakeSender();
    const getUserMedia = vi
      .fn()
      .mockResolvedValueOnce(createStream(permissionTrack))
      .mockResolvedValueOnce(createStream(firstTrack))
      .mockRejectedValueOnce(new DOMException("Camera busy", "NotReadableError"))
      .mockRejectedValueOnce(new DOMException("Unavailable", "OverconstrainedError"));
    const { send } = await establishStreamingSession(getUserMedia, sender);

    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "back" } });
    await waitFor(() => expect(sender.replaceTrack).toHaveBeenCalledWith(null));
    fireEvent.change(screen.getByLabelText("Camera"), { target: { value: "front" } });

    await waitFor(() =>
      expect(
        screen.getByText(
          "The selected camera could not be opened after releasing the previous camera.",
        ),
      ).toBeTruthy(),
    );
    expect(
      send.mock.calls.filter(([message]) => message.type === "phone.webcam.stop"),
    ).toHaveLength(1);
    expect((screen.getByRole("button", { name: "Start" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
    vi.useRealTimers();
  });

  it("ignores a terminal event for an older operation", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await findStart(send, 1);

    act(() => {
      publishPhoneWebcamResult({
        type: "phone.webcam.ended",
        operationId: "older-operation",
        reason: "transport-lost",
        message: "Old session ended.",
      });
    });

    expect(selectedTrack.stop).not.toHaveBeenCalled();
    expect(screen.queryByText("Old session ended.")).toBeNull();
  });

  it("stops capture and disables restart when the live host capability becomes unavailable", async () => {
    const permissionTrack = createTrack("permission", 640, 480);
    const selectedTrack = createTrack("front", 1920, 1080);
    stubMediaDevices(
      vi
        .fn()
        .mockResolvedValueOnce(createStream(permissionTrack))
        .mockResolvedValueOnce(createStream(selectedTrack)),
    );
    const send = vi.fn<(message: ClientMessage) => void>();
    storeReconnectKey();
    const rendered = renderWorkspace(send);
    fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
    await screen.findByLabelText("Camera");
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await findStart(send, 1);

    const unavailable = { ...capability, enabled: false, canUse: false };
    rendered.rerender(workspace(send, undefined, "secure-direct", "paired", 1, unavailable));

    await waitFor(() => expect(selectedTrack.stop).toHaveBeenCalledOnce());
    expect((screen.getByRole("button", { name: "Start" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
    expect(screen.getByText("Enable Phone webcam in the Windows app first.")).toBeTruthy();
  });
});

function renderWorkspace(
  send: (message: ClientMessage) => void,
  hostIdentityPublicKey = defaultHostIdentity,
  transportMode: "relay" | "secure-direct" = "secure-direct",
  state: "paired" | "connecting" = "paired",
  connectionEpoch = 1,
  currentCapability = capability,
) {
  return render(
    workspace(
      send,
      hostIdentityPublicKey,
      transportMode,
      state,
      connectionEpoch,
      currentCapability,
    ),
  );
}

function workspace(
  send: (message: ClientMessage) => void,
  hostIdentityPublicKey = defaultHostIdentity,
  transportMode: "relay" | "secure-direct" = "secure-direct",
  state: "paired" | "connecting" = "paired",
  connectionEpoch = 1,
  currentCapability = capability,
  activePcId = pcId,
  capabilityConfirmed = true,
) {
  return (
    <PhoneWebcamWorkspace
      activePc={{
        customName: false,
        id: activePcId,
        name: "PC",
        url: activePcId,
        hostIdentityPublicKey,
        transportMode,
      }}
      capability={currentCapability}
      capabilityConfirmed={capabilityConfirmed}
      clientId="client-test"
      connectionEpoch={connectionEpoch}
      onBack={vi.fn()}
      send={send}
      state={state}
    />
  );
}

async function establishStreamingSession(
  getUserMedia: ReturnType<typeof vi.fn>,
  sender: FakeSender,
  getStats: (() => Promise<Map<string, unknown>>) | undefined = undefined,
  useMicrophone = false,
  transportMode: "secure-direct" | "relay" = "secure-direct",
  relayTimes: Pick<PhoneWebcamStartResultMessage, "turnExpiresAt" | "relayUsageCheckedAt"> = {},
) {
  const readStats = getStats ?? (() => Promise.resolve(new Map<string, unknown>()));
  const peers: StreamingPeerConnection[] = [];
  const audioSender = new FakeSender();
  const addAudioTrack = vi.fn(() => audioSender as unknown as RTCRtpSender);
  let nextGathering: RTCIceGatheringState = "complete";
  let nextCandidate: "relay" | "host" | null = transportMode === "relay" ? "relay" : "host";
  class StreamingPeerConnection {
    readonly iceGatheringState = nextGathering;
    readonly candidate = nextCandidate;
    readonly listeners = new Map<string, (() => void)[]>();
    connectionState: RTCPeerConnectionState = "new";
    localDescription: RTCSessionDescriptionInit | null = null;
    constructor() {
      peers.push(this);
    }
    addEventListener(type: string, listener: () => void) {
      this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
    }
    removeEventListener(type: string, listener: () => void) {
      this.listeners.set(type, (this.listeners.get(type) ?? []).filter((entry) => entry !== listener));
    }
    setRemoteDescription() {
      return Promise.resolve();
    }
    getTransceivers() {
      return [
        { receiver: { track: { kind: "video" } }, sender, direction: "recvonly" },
        ...(useMicrophone
          ? [{ receiver: { track: { kind: "audio" } }, sender: audioSender, direction: "recvonly" }]
          : []),
      ];
    }
    addTrack = addAudioTrack;
    createAnswer() {
      return Promise.resolve({
        type: "answer" as RTCSdpType,
        sdp:
          "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\n" +
          (useMicrophone
            ? "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=sendonly\r\n"
            : "") +
          (this.candidate ? `a=candidate:1 1 udp 1 192.0.2.20 5000 typ ${this.candidate}\r\n` : ""),
      });
    }
    setLocalDescription(description: RTCSessionDescriptionInit) {
      this.localDescription = description;
      return Promise.resolve();
    }
    getStats() {
      return readStats();
    }
    close() {
      this.connectionState = "closed";
    }
    setConnectionState(state: RTCPeerConnectionState) {
      this.connectionState = state;
      for (const listener of this.listeners.get("connectionstatechange") ?? []) {
        listener();
      }
    }
  }
  vi.stubGlobal(
    "RTCPeerConnection",
    StreamingPeerConnection as unknown as typeof RTCPeerConnection,
  );
  stubMediaDevices(getUserMedia);
  const send = vi.fn<(message: ClientMessage) => void>();
  storeReconnectKey();
  const hostKey = createPairingKeyMaterial();
  if (!hostKey) {
    throw new Error("Host test key is unavailable.");
  }
  renderWorkspace(
    send,
    hostKey.reconnectPublicKey,
    transportMode,
    "paired",
    1,
    useMicrophone ? { ...capability, microphoneAvailable: true } : capability,
  );
  fireEvent.click(screen.getByRole("button", { name: "Allow camera access" }));
  await screen.findByLabelText("Camera");
  if (useMicrophone) {
    fireEvent.click(screen.getByLabelText("Use microphone"));
    await waitFor(() =>
      expect((screen.getByLabelText("Use microphone") as HTMLInputElement).checked).toBe(true),
    );
  }
  fireEvent.click(screen.getByRole("button", { name: "Start" }));
  const start = await findStart(send, 1);
  publishSignedOffer(start.operationId, hostKey.privateKey, useMicrophone,
    transportMode === "relay" ? { ...relayOffer, ...relayTimes } : {});
  await waitFor(() =>
    expect(send.mock.calls.some(([message]) => message.type === "phone.webcam.answer")).toBe(true),
  );
  if (useMicrophone) {
    expect(addAudioTrack).toHaveBeenCalledOnce();
    expect(addAudioTrack).toHaveBeenCalledWith(
      expect.objectContaining({ enabled: true }),
      expect.anything(),
    );
    expect(audioSender.replaceTrack).not.toHaveBeenCalled();
  }
  act(() => {
    peers[0]?.setConnectionState("connected");
  });
  await screen.findByText(transportMode === "relay" ? "Streaming through Relay" : "Streaming through Enhanced Direct");
  return {
    send,
    hostPrivateKey: hostKey.privateKey,
    setPeerState: (state: RTCPeerConnectionState, index = 0) => peers[index]?.setConnectionState(state),
    setNextPeerRoute: (gathering: RTCIceGatheringState, candidate: "relay" | "host" | null) => {
      nextGathering = gathering;
      nextCandidate = candidate;
    },
  };
}

async function findStart(send: ReturnType<typeof vi.fn>, count: number) {
  return waitFor(() => {
    const starts = send.mock.calls
      .map(([entry]) => entry as ClientMessage)
      .filter((entry) => entry.type === "phone.webcam.start");
    const message = starts[count - 1];
    if (message?.type !== "phone.webcam.start") {
      throw new Error(`Start ${count} was not sent.`);
    }
    return message;
  });
}

const relayOffer = {
  iceServers: [{ urls: "turns:turn.example.test:5349?transport=tcp", username: "test", credential: "test" }],
  maximumBitrate: 2_000_000,
};

function publishSignedOffer(
  operationId: string,
  hostPrivateKey: string,
  useMicrophone = false,
  overrides: Pick<PhoneWebcamStartResultMessage, "iceServers" | "maximumBitrate" | "turnExpiresAt" | "relayUsageCheckedAt"> = {},
) {
  const offerSdp =
    "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=recvonly\r\n" +
    (useMicrophone
      ? "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=recvonly\r\n"
      : "");
  const offerHash = hashSessionDescription(offerSdp);
  const transcript = `VolturaAir phone-webcam:offer:v2:client-test:${operationId}:${offerHash}`;
  const hostSignature = signPrivateKeyPayload(hostPrivateKey, new TextEncoder().encode(transcript));
  act(() => {
    publishPhoneWebcamResult({
      type: "phone.webcam.start.result",
      operationId,
      succeeded: true,
      code: "accepted",
      message: "ready",
      offerSdp,
      hostSignature,
      maximumBitrate: 12_000_000,
      ...overrides,
    });
  });
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
  if (!key) {
    throw new Error("Test key generation is unavailable.");
  }
  localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);
  return key;
}

class FakeSender {
  private lastReturnedEncodingCount: number | null = null;

  constructor(private readonly getEncodingCount: () => number = () => 1) {}

  readonly replaceTrack = vi
    .fn<(track: MediaStreamTrack | null) => Promise<void>>()
    .mockResolvedValue(undefined);
  readonly getParameters = vi.fn(() => {
    const encodingCount = this.getEncodingCount();
    this.lastReturnedEncodingCount = encodingCount;
    return {
      encodings: Array.from({ length: encodingCount }, () => ({})),
      transactionId: "fake-sender-parameters",
    } as unknown as RTCRtpSendParameters;
  });
  readonly setParameters = vi
    .fn<(parameters: RTCRtpSendParameters) => Promise<void>>()
    .mockImplementation((parameters) => {
      if (
        this.lastReturnedEncodingCount === null ||
        parameters.encodings.length !== this.lastReturnedEncodingCount
      ) {
        return Promise.reject(
          new DOMException("parameters are not valid", "InvalidModificationError"),
        );
      }
      this.lastReturnedEncodingCount = null;
      return Promise.resolve();
    });
  track: MediaStreamTrack | null = null;
}

function stubMediaDevices(getUserMedia: ReturnType<typeof vi.fn>) {
  vi.stubGlobal("navigator", {
    mediaDevices: Object.assign(new EventTarget(), {
      getUserMedia,
      enumerateDevices: vi.fn().mockResolvedValue([
        {
          kind: "videoinput",
          deviceId: "front",
          label: "Front Camera",
          groupId: "group",
          toJSON: () => ({}),
        },
        {
          kind: "videoinput",
          deviceId: "back",
          label: "Back Camera",
          groupId: "group",
          toJSON: () => ({}),
        },
      ]),
    }),
  });
}

function createTrack(deviceId: string, width: number, height: number) {
  const events = new EventTarget();
  const track = {
    contentHint: "",
    muted: false,
    stop: vi.fn(),
    getSettings: () => ({ deviceId, width, height, frameRate: 30 }),
    addEventListener: vi.fn(events.addEventListener.bind(events)),
    end: () => {
      events.dispatchEvent(new Event("ended"));
    },
    setMuted: (muted: boolean) => {
      track.muted = muted;
      events.dispatchEvent(new Event(muted ? "mute" : "unmute"));
    },
  };
  return track;
}

function createStream(track: ReturnType<typeof createTrack>): MediaStream {
  return {
    getTracks: () => [track],
    getVideoTracks: () => [track],
    getAudioTracks: () => [],
    addTrack: vi.fn(),
  } as unknown as MediaStream;
}

function createAudioTrack() {
  return { ...createTrack("microphone", 0, 0), enabled: true };
}

function createAudioStream(track: ReturnType<typeof createAudioTrack>): MediaStream {
  return {
    getTracks: () => [track],
    getVideoTracks: () => [],
    getAudioTracks: () => [track],
  } as unknown as MediaStream;
}

function createAvStream(
  video: ReturnType<typeof createTrack>,
  audio: ReturnType<typeof createAudioTrack>,
): MediaStream {
  return {
    getTracks: () => [video, audio],
    getVideoTracks: () => [video],
    getAudioTracks: () => [audio],
    addTrack: vi.fn(),
  } as unknown as MediaStream;
}
