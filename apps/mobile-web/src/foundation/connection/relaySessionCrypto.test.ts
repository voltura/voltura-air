import { describe, expect, it } from "vitest";
import { RelayEncryptedChannel } from "./relaySessionCrypto";

const secret = Uint8Array.from({ length: 32 }, (_, index) => index);
const transcript = new TextEncoder().encode(
  "voltura-air-relay-session-v1\nroute\nclient\nhost\nhost-key\nclient-key\nnonce",
);

describe("RelayEncryptedChannel", () => {
  it("round trips direction-specific encrypted frames", async () => {
    const host = RelayEncryptedChannel.createHostForConformance(secret, transcript);
    const device = RelayEncryptedChannel.create(secret, transcript);

    const hostFrame = await sendFrame(host, "host message");
    const deviceFrame = await sendFrame(device, "device message");

    expect(toHex(hostFrame)).toBe(
      "0101000000000000000193026d69e19b495057ed0c29c7590a361aac6f23d2317ee556962bd8",
    );
    expect(toHex(deviceFrame)).toBe(
      "010200000000000000010d50fe8f07881b182fa5fa6faa44624e03f59260b2824659bff232ad75e0",
    );
    expect(await device.decryptText(hostFrame)).toBe("host message");
    expect(await host.decryptText(deviceFrame)).toBe("device message");
  });

  it("rejects tampering, replay, and the wrong direction", async () => {
    const host = RelayEncryptedChannel.createHostForConformance(secret, transcript);
    const device = RelayEncryptedChannel.create(secret, transcript);
    const valid = await sendFrame(host, "message");
    const altered = valid.slice(0);
    const alteredBytes = new Uint8Array(altered);
    alteredBytes[12] = alteredBytes[12]! ^ 1;

    expect(await RelayEncryptedChannel.create(secret, transcript).decryptText(altered)).toBeNull();
    expect(await device.decryptText(valid)).toBe("message");
    expect(await device.decryptText(valid)).toBeNull();
    expect(await host.decryptText(await sendFrame(host, "wrong direction"))).toBeNull();
  });

  it("decrypts back-to-back inbound frames in arrival order", async () => {
    const host = RelayEncryptedChannel.createHostForConformance(secret, transcript);
    const device = RelayEncryptedChannel.create(secret, transcript);
    const first = await sendFrame(host, "first");
    const second = await sendFrame(host, "second");

    expect(await Promise.all([device.decryptText(first), device.decryptText(second)])).toEqual([
      "first",
      "second",
    ]);
  });

  it("coalesces adjacent relative movement before encryption", async () => {
    const device = RelayEncryptedChannel.create(secret, transcript);
    const host = RelayEncryptedChannel.createHostForConformance(secret, transcript);
    const frames: ArrayBuffer[] = [];

    const first = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "health.ping" }),
    );
    const second = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "pointer.move", seq: 7, dx: 2, dy: 3 }),
    );
    const third = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "pointer.move", dx: 4, dy: -1 }),
    );
    await Promise.all([first, second, third]);

    expect(frames).toHaveLength(2);
    expect(await host.decryptText(frames[0]!)).toBe(JSON.stringify({ type: "health.ping" }));
    expect(JSON.parse((await host.decryptText(frames[1]!))!)).toEqual({
      type: "pointer.move",
      seq: 7,
      dx: 6,
      dy: 2,
    });
  });

  it("preserves matching input context and never merges different contexts", async () => {
    const device = RelayEncryptedChannel.create(secret, transcript);
    const host = RelayEncryptedChannel.createHostForConformance(secret, transcript);
    const frames: ArrayBuffer[] = [];

    const barrier = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "health.ping" }),
    );
    const first = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "pointer.move", inputContext: "trackpad", dx: 2, dy: 3 }),
    );
    const second = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "pointer.move", inputContext: "trackpad", dx: 4, dy: -1 }),
    );
    const third = device.send(
      (frame) => {
        frames.push(frame);
      },
      JSON.stringify({ type: "pointer.move", inputContext: "gyro-mouse", dx: 1, dy: 1 }),
    );
    await Promise.all([barrier, first, second, third]);

    expect(frames).toHaveLength(3);
    expect(await host.decryptText(frames[0]!)).toBe(JSON.stringify({ type: "health.ping" }));
    expect(JSON.parse((await host.decryptText(frames[1]!))!)).toEqual({
      type: "pointer.move",
      inputContext: "trackpad",
      dx: 6,
      dy: 2,
    });
    expect(JSON.parse((await host.decryptText(frames[2]!))!)).toEqual({
      type: "pointer.move",
      inputContext: "gyro-mouse",
      dx: 1,
      dy: 1,
    });
  });

  it("rejects queue growth instead of accumulating stale commands", async () => {
    const device = RelayEncryptedChannel.create(secret, transcript);
    const sends = Array.from({ length: 40 }, (_, index) =>
      device.send(() => undefined, JSON.stringify({ type: "status.get", request: index })),
    );

    const results = await Promise.allSettled(sends);
    expect(results.some((result) => result.status === "rejected")).toBe(true);
  });
});

async function sendFrame(channel: RelayEncryptedChannel, message: string): Promise<ArrayBuffer> {
  let frame: ArrayBuffer | null = null;
  await channel.send((value) => {
    frame = value;
  }, message);
  return frame!;
}

function toHex(value: ArrayBuffer): string {
  return [...new Uint8Array(value)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
