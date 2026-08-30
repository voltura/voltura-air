import { describe, expect, it } from "vitest";
import {
  AppsPreviewAssembler,
  createAppsPreviewRequest,
  parseAppsPreviewRecord,
} from "./appsPreviewRecords";

const revision = "0123456789abcdef0123456789abcdef";
const windowId = "fedcba9876543210fedcba9876543210";
const encoder = new TextEncoder();

describe("Apps preview records", () => {
  it("creates a bounded request for the centered window and neighbors", () => {
    const request = new Uint8Array(createAppsPreviewRequest(revision, [windowId, windowId]));

    expect(request).toHaveLength(66);
    expect(request[0]).toBe(0x11);
    expect(request[33]).toBe(1);
    expect(new TextDecoder().decode(request.slice(34))).toBe(windowId);
  });

  it("assembles exact sequential JPEG records", async () => {
    const content = new Uint8Array([1, 2, 3, 4]);
    const header = new Uint8Array(43);
    header[0] = 0x12;
    header.set(encoder.encode(windowId), 1);
    header[33] = 1;
    new DataView(header.buffer).setUint16(34, 320);
    new DataView(header.buffer).setUint16(36, 180);
    new DataView(header.buffer).setUint32(38, content.length);
    header[42] = 1;
    const data = new Uint8Array(37 + content.length);
    data[0] = 0x13;
    data.set(encoder.encode(windowId), 1);
    new DataView(data.buffer).setUint32(33, 0);
    data.set(content, 37);

    const parsedHeader = parseAppsPreviewRecord(header.buffer);
    const parsedData = parseAppsPreviewRecord(data.buffer);
    expect(parsedHeader).not.toBeNull();
    expect(parsedData).not.toBeNull();

    const assembler = new AppsPreviewAssembler();
    expect(assembler.accept(parsedHeader!)).toBeUndefined();
    const complete = assembler.accept(parsedData!);
    expect(complete?.kind).toBe("complete");
    if (complete?.kind === "complete") {
      expect(new Uint8Array(await complete.blob.arrayBuffer())).toEqual(content);
    }
  });

  it("rejects wrong versions, oversized records, and out-of-order chunks", () => {
    expect(parseAppsPreviewRecord(new Uint8Array([0x22]).buffer)).toBeNull();
    expect(parseAppsPreviewRecord(new Uint8Array(37 + 48 * 1024 + 1).buffer)).toBeNull();

    const data = new Uint8Array(38);
    data[0] = 0x13;
    data.set(encoder.encode(windowId), 1);
    expect(new AppsPreviewAssembler().accept(parseAppsPreviewRecord(data.buffer)!)).toBeNull();
  });

  it("bounds incomplete preview assembly to the requested three cards", () => {
    const assembler = new AppsPreviewAssembler();
    for (const digit of ["1", "2", "3"]) {
      expect(
        assembler.accept({
          kind: "header",
          windowId: digit.repeat(32),
          available: true,
          width: 1,
          height: 1,
          encodedBytes: 1,
        }),
      ).toBeUndefined();
    }

    expect(
      assembler.accept({
        kind: "header",
        windowId: "4".repeat(32),
        available: true,
        width: 1,
        height: 1,
        encodedBytes: 1,
      }),
    ).toBeNull();
  });
});
