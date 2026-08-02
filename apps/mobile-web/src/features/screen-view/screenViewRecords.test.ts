import { describe, expect, it } from "vitest";
import { parseScreenPlaintextRecord } from "./screenViewRecords";

describe("screen WebRTC event records", () => {
  it("parses bounded cursor position and shape data", () => {
    const bytes = new Uint8Array(42);
    const view = new DataView(bytes.buffer);
    bytes[0] = 4;
    view.setBigInt64(1, 10n, false);
    bytes[9] = 1;
    view.setInt32(10, 320, false);
    view.setInt32(14, 240, false);
    view.setInt32(18, 2, false);
    view.setInt32(22, 3, false);
    view.setInt32(26, 16, false);
    view.setInt32(30, 24, false);
    bytes[34] = 2;
    view.setInt32(35, 3, false);
    bytes.set([6, 7, 8], 39);

    expect(parseScreenPlaintextRecord(bytes)).toMatchObject({
      type: "cursor", visible: true, x: 320, y: 240, width: 16, height: 24
    });
  });

  it("parses bounded capture status and rejects former image/video records", () => {
    const code = new TextEncoder().encode("capture-stopped");
    const message = new TextEncoder().encode("Capture stopped.");
    const bytes = new Uint8Array(4 + code.length + message.length);
    bytes[0] = 5;
    bytes[1] = code.length;
    new DataView(bytes.buffer).setUint16(2, message.length, false);
    bytes.set(code, 4);
    bytes.set(message, 4 + code.length);

    expect(parseScreenPlaintextRecord(bytes)).toEqual({ type: "status", code: "capture-stopped", message: "Capture stopped." });
    expect(() => parseScreenPlaintextRecord(Uint8Array.of(1))).toThrow("Unknown screen event type");
    expect(() => parseScreenPlaintextRecord(Uint8Array.of(6))).toThrow("Unknown screen event type");
  });
});
