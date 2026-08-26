import { describe, expect, it } from "vitest";
import {
  createFileTransferAcknowledgement,
  createFileTransferDataRecord,
  maximumFileTransferPayloadBytes,
  parseFileTransferRecord,
} from "./fileTransferRecords";

describe("file transfer records", () => {
  it("round-trips data and zero-byte cumulative acknowledgements with big-endian offsets", () => {
    const data = createFileTransferDataRecord(0x01020304050607, new Uint8Array([1, 2, 3]));
    const acknowledgement = createFileTransferAcknowledgement(0);

    expect(Array.from(new Uint8Array(data).slice(0, 9))).toEqual([0x11, 0, 1, 2, 3, 4, 5, 6, 7]);
    expect(parseFileTransferRecord(data)).toEqual({
      kind: "data",
      offset: 0x01020304050607,
      payload: new Uint8Array([1, 2, 3]),
    });
    expect(parseFileTransferRecord(acknowledgement)).toEqual({
      kind: "acknowledgement",
      offset: 0,
      payload: new Uint8Array(),
    });
  });

  it("rejects invalid versions, shapes, sizes, and unsafe offsets", () => {
    const wrongVersion = new Uint8Array(createFileTransferAcknowledgement(0));
    wrongVersion[0] = 0x22;
    const unsafeOffset = new Uint8Array(createFileTransferAcknowledgement(0));
    unsafeOffset.fill(0xff, 1);

    expect(
      parseFileTransferRecord(new Uint8Array([0x11, 0, 0, 0, 0, 0, 0, 0, 0]).buffer),
    ).toBeNull();
    expect(
      parseFileTransferRecord(new Uint8Array([0x12, 0, 0, 0, 0, 0, 0, 0, 0, 1]).buffer),
    ).toBeNull();
    expect(parseFileTransferRecord(wrongVersion.buffer)).toBeNull();
    expect(parseFileTransferRecord(unsafeOffset.buffer)).toBeNull();
    expect(() => createFileTransferDataRecord(0, new Uint8Array())).toThrow(RangeError);
    expect(() =>
      createFileTransferDataRecord(0, new Uint8Array(maximumFileTransferPayloadBytes + 1)),
    ).toThrow(RangeError);
  });
});
