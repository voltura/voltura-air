import { afterEach, describe, expect, it, vi } from "vitest";
import { IceGatheringTimeoutError, waitForIceGathering } from "./iceGathering";

class GatheringPeer extends EventTarget {
  iceGatheringState: RTCIceGatheringState = "gathering";
  localDescription = { type: "answer", sdp: "v=0\r\n" };

  candidate(port: number) {
    const candidate = `candidate:relay${port} 1 udp 1 192.0.2.1 ${port} typ relay`;
    this.localDescription.sdp += `a=${candidate}\r\n`;
    this.dispatchEvent(
      Object.assign(new Event("icecandidate"), {
        candidate: { type: "relay", candidate },
      }),
    );
  }

  complete() {
    this.iceGatheringState = "complete";
    this.dispatchEvent(new Event("icegatheringstatechange"));
  }

  wait(relay = true) {
    return waitForIceGathering(this as unknown as RTCPeerConnection, relay);
  }
}

afterEach(() => vi.useRealTimers());

describe("complete ICE answer snapshot", () => {
  it("includes a later TURN candidate instead of signing an incomplete answer after 350ms", async () => {
    vi.useFakeTimers();
    const peer = new GatheringPeer();
    let answer: string | undefined;
    const gathered = peer.wait().then(() => {
      answer = peer.localDescription.sdp;
    });
    peer.candidate(50000);
    await vi.advanceTimersByTimeAsync(700);
    expect(answer).toBeUndefined();
    peer.candidate(50001);
    peer.complete();
    await gathered;
    expect(answer).toContain("50000 typ relay");
    expect(answer).toContain("50001 typ relay");
    expect(vi.getTimerCount()).toBe(0);
  });

  it("uses gathered relay candidates at the existing deadline when completion is not reported", async () => {
    vi.useFakeTimers();
    const peer = new GatheringPeer();
    let ready = false;
    const gathered = peer.wait().then(() => {
      ready = true;
    });
    peer.candidate(50000);
    await vi.advanceTimersByTimeAsync(9999);
    expect(ready).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    await gathered;
    expect(ready).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
  });

  it.each([false, true])("rejects an incomplete empty candidate set (relay=%s)", async (relay) => {
    vi.useFakeTimers();
    const peer = new GatheringPeer();
    const rejected = expect(peer.wait(relay)).rejects.toBeInstanceOf(IceGatheringTimeoutError);
    await vi.advanceTimersByTimeAsync(10000);
    await rejected;
  });

  it("still waits for completion in Direct mode", async () => {
    vi.useFakeTimers();
    const peer = new GatheringPeer();
    const rejected = expect(peer.wait(false)).rejects.toBeInstanceOf(IceGatheringTimeoutError);
    peer.candidate(50000);
    await vi.advanceTimersByTimeAsync(10000);
    await rejected;
  });

  it("does not use a mixed candidate set at the Relay deadline", async () => {
    vi.useFakeTimers();
    const peer = new GatheringPeer();
    const rejected = expect(peer.wait()).rejects.toBeInstanceOf(IceGatheringTimeoutError);
    peer.candidate(50000);
    peer.localDescription.sdp += "a=candidate:host 1 udp 1 192.0.2.2 50002 typ host\r\n";
    await vi.advanceTimersByTimeAsync(10000);
    await rejected;
  });
});
