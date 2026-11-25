import { describe, expect, it, vi } from "vitest";
import { runWebRtcSetupStep } from "./peerSetup";

describe("WebRTC setup lifetime", () => {
  it("does not start a native step after cancellation", async () => {
    const step = vi.fn(() => Promise.resolve("answer"));
    const reason = new Error("Setup already stopped");
    await expect(runWebRtcSetupStep(AbortSignal.abort(reason), step)).rejects.toBe(reason);
    expect(step).not.toHaveBeenCalled();
  });

  it("rejects before a pending native step settles and absorbs a late native rejection", async () => {
    const setup = new AbortController();
    let rejectNative!: (reason: Error) => void;
    const native = new Promise<void>((_resolve, reject) => {
      rejectNative = reject;
    });
    const step = runWebRtcSetupStep(setup.signal, () => native);
    const reason = new Error("Setup stopped");
    setup.abort(reason);
    await expect(step).rejects.toBe(reason);
    rejectNative(new Error("Peer closed"));
    await Promise.resolve();
  });

  it("detaches its listener after a successful native step", async () => {
    const setup = new AbortController();
    const removeListener = vi.spyOn(setup.signal, "removeEventListener");
    await expect(runWebRtcSetupStep(setup.signal, () => Promise.resolve("answer"))).resolves.toBe("answer");
    expect(removeListener).toHaveBeenCalledWith("abort", expect.any(Function));
  });

  it("detaches its listener when the native call throws synchronously", async () => {
    const setup = new AbortController();
    const removeListener = vi.spyOn(setup.signal, "removeEventListener");
    const reason = new Error("Peer unavailable");
    await expect(runWebRtcSetupStep(setup.signal, () => {
      throw reason;
    })).rejects.toBe(reason);
    expect(removeListener).toHaveBeenCalledWith("abort", expect.any(Function));
  });
});
