import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AppsPreviewOfferMessage } from "../../foundation/protocol/messages";
import { useAppsPreviews } from "./useAppsPreviews";

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));
vi.mock("../../foundation/webrtc/iceGathering", () => ({
  hasOnlyRelayCandidates: () => true,
  waitForIceGathering: () => Promise.resolve(),
}));
vi.mock("../../foundation/webrtc/sessionCrypto", () => ({
  hashSessionDescription: (value: string) => value,
  verifyHostSessionSignature: () => true,
}));

const peers: PreviewPeer[] = [];
const offer: AppsPreviewOfferMessage = {
  type: "apps.preview.offer",
  operationId: "list-operation",
  previewId: "11111111111111111111111111111111",
  offerSdp: "offer",
  hostSignature: "signature",
};

describe("Apps preview lifetime", () => {
  beforeEach(() => {
    peers.length = 0;
    vi.stubGlobal("RTCPeerConnection", function () {
      const peer = new PreviewPeer();
      peers.push(peer);
      return peer;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("does not create a peer for an offer delivered while hidden", async () => {
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
    const { result, send } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    expect(peers).toHaveLength(0);
    expect(send).not.toHaveBeenCalledWith(expect.objectContaining({ type: "apps.preview.answer" }));
  });

  it("closes a late data channel after the preview has been stopped", async () => {
    const { result } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    const peer = peers[0]!;
    act(() => result.current.closePreview());
    const channel = new PreviewChannel();
    act(() => peer.deliver(channel));
    expect(channel.close).toHaveBeenCalledOnce();
  });

  it("ignores messages from a replaced peer instead of closing its replacement", async () => {
    const { result } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    const oldChannel = new PreviewChannel();
    act(() => peers[0]!.deliver(oldChannel));
    const replacementId = "22222222222222222222222222222222";
    await act(() => result.current.acceptPreviewOffer({ ...offer, previewId: replacementId }));
    await act(() =>
      oldChannel.dispatchEvent(new MessageEvent("message", { data: "late-message" })),
    );
    expect(peers[1]!.close).not.toHaveBeenCalled();
    expect(result.current.ownsPreview(replacementId)).toBe(true);
  });

  it("releases the peer when preview access is revoked while Apps remains allowed", async () => {
    const { result, rerender } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    rerender({ previewAvailable: false });
    expect(peers[0]!.close).toHaveBeenCalledOnce();
    expect(result.current.ownsPreview(offer.previewId)).toBe(false);
  });

  it("releases the peer when its data channel closes", async () => {
    const { result } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    const channel = new PreviewChannel();
    act(() => peers[0]!.deliver(channel));
    await act(() => channel.dispatchEvent(new Event("close")));
    expect(peers[0]!.close).toHaveBeenCalledOnce();
    expect(result.current.ownsPreview(offer.previewId)).toBe(false);
  });

  it("closes duplicate channels without replacing the owned channel", async () => {
    const { result } = setup();
    await act(() => result.current.acceptPreviewOffer(offer));
    const channel = new PreviewChannel();
    const duplicate = new PreviewChannel();
    act(() => {
      peers[0]!.deliver(channel);
      peers[0]!.deliver(duplicate);
    });
    expect(duplicate.close).toHaveBeenCalledOnce();
    expect(channel.close).not.toHaveBeenCalled();
    act(() => result.current.closePreview());
    expect(channel.close).toHaveBeenCalledOnce();
  });
});

function setup() {
  const send = vi.fn();
  const setMessage = vi.fn();
  return {
    send,
    ...renderHook(
      ({ previewAvailable }) =>
        useAppsPreviews({
          activePc: {
            id: "pc",
            name: "PC",
            customName: false,
            url: "https://pc.local",
            hostIdentityPublicKey: "host-key",
          },
          capability: { enabled: true, permissionGranted: true, canUse: true, previewAvailable },
          clientId: "client",
          isListOperationPending: () => false,
          revision: null,
          selectedIndex: 0,
          send,
          setMessage,
          state: "paired",
          windows: [],
        }),
      { initialProps: { previewAvailable: true } },
    ),
  };
}

class PreviewChannel extends EventTarget {
  readonly label = "voltura-apps-preview";
  readonly close = vi.fn();
}

class PreviewPeer extends EventTarget {
  localDescription: RTCSessionDescriptionInit | null = null;
  readonly close = vi.fn();
  readonly setRemoteDescription = vi.fn(() => Promise.resolve());
  readonly createAnswer = vi.fn(() => Promise.resolve({ type: "answer" as const, sdp: "answer" }));
  readonly setLocalDescription = vi.fn((description: RTCSessionDescriptionInit) => {
    this.localDescription = description;
    return Promise.resolve();
  });

  deliver(channel: PreviewChannel) {
    const event = new Event("datachannel");
    Object.defineProperty(event, "channel", { value: channel });
    this.dispatchEvent(event);
  }
}
