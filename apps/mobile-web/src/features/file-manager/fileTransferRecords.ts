export const maximumFileTransferPayloadBytes = 64 * 1024;
export const maximumUnacknowledgedFileTransferBytes = 1024 * 1024;
const headerBytes = 9;
const version = 1;
const dataKind = 1;
const acknowledgementKind = 2;

export type FileTransferRecord =
  | { kind: "data"; offset: number; payload: Uint8Array }
  | { kind: "acknowledgement"; offset: number; payload: Uint8Array };

export function createFileTransferDataRecord(offset: number, payload: Uint8Array): ArrayBuffer {
  if (
    !Number.isSafeInteger(offset) ||
    offset < 0 ||
    payload.byteLength < 1 ||
    payload.byteLength > maximumFileTransferPayloadBytes
  ) {
    throw new RangeError("Invalid file-transfer data record.");
  }
  const result = new Uint8Array(headerBytes + payload.byteLength);
  result[0] = (version << 4) | dataKind;
  new DataView(result.buffer).setBigUint64(1, BigInt(offset), false);
  result.set(payload, headerBytes);
  return result.buffer;
}

export function createFileTransferAcknowledgement(offset: number): ArrayBuffer {
  if (!Number.isSafeInteger(offset) || offset < 0) {
    throw new RangeError("Invalid file-transfer acknowledgement.");
  }
  const result = new Uint8Array(headerBytes);
  result[0] = (version << 4) | acknowledgementKind;
  new DataView(result.buffer).setBigUint64(1, BigInt(offset), false);
  return result.buffer;
}

export function parseFileTransferRecord(value: unknown): FileTransferRecord | null {
  const bytes =
    value instanceof ArrayBuffer
      ? new Uint8Array(value)
      : ArrayBuffer.isView(value)
        ? new Uint8Array(value.buffer, value.byteOffset, value.byteLength)
        : null;
  const header = bytes?.[0];
  if (
    !bytes ||
    header === undefined ||
    bytes.byteLength < headerBytes ||
    bytes.byteLength > headerBytes + maximumFileTransferPayloadBytes ||
    header >> 4 !== version
  ) {
    return null;
  }
  const offsetValue = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getBigUint64(
    1,
    false,
  );
  if (offsetValue > BigInt(Number.MAX_SAFE_INTEGER)) {
    return null;
  }
  const kind = header & 0x0f;
  const payload = bytes.slice(headerBytes);
  if (kind === dataKind && payload.byteLength > 0) {
    return { kind: "data", offset: Number(offsetValue), payload };
  }
  if (kind === acknowledgementKind && payload.byteLength === 0) {
    return { kind: "acknowledgement", offset: Number(offsetValue), payload };
  }
  return null;
}
