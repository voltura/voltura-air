const version = 1;
const inputKind = 1;
const outputKind = 2;
const acknowledgementKind = 3;
const resizeKind = 4;
export const maximumTerminalPayloadBytes = 16 * 1024;
export const maximumTerminalPasteBytes = 64 * 1024;

export function createTerminalInput(payload: Uint8Array): ArrayBuffer {
  if (payload.length < 1 || payload.length > maximumTerminalPayloadBytes) {
    throw new RangeError("Invalid Terminal input size.");
  }
  const result = new Uint8Array(9 + payload.length);
  result[0] = (version << 4) | inputKind;
  result.set(payload, 9);
  return result.buffer;
}

export function createTerminalAcknowledgement(offset: number): ArrayBuffer {
  if (!Number.isSafeInteger(offset) || offset < 0) {
    throw new RangeError("Invalid Terminal offset.");
  }
  const result = new Uint8Array(9);
  result[0] = (version << 4) | acknowledgementKind;
  new DataView(result.buffer).setBigUint64(1, BigInt(offset));
  return result.buffer;
}

export function createTerminalResize(columns: number, rows: number): ArrayBuffer {
  if (
    !Number.isInteger(columns) ||
    columns < 10 ||
    columns > 500 ||
    !Number.isInteger(rows) ||
    rows < 5 ||
    rows > 300
  ) {
    throw new RangeError("Invalid Terminal dimensions.");
  }
  const result = new Uint8Array(5);
  result[0] = (version << 4) | resizeKind;
  new DataView(result.buffer).setUint16(1, columns);
  new DataView(result.buffer).setUint16(3, rows);
  return result.buffer;
}

export function parseTerminalOutput(
  value: ArrayBuffer,
): { offset: number; payload: Uint8Array } | null {
  if (value.byteLength < 10 || value.byteLength > 9 + maximumTerminalPayloadBytes) {
    return null;
  }
  const bytes = new Uint8Array(value);
  if (bytes[0] !== ((version << 4) | outputKind)) {
    return null;
  }
  const offset = Number(new DataView(value).getBigUint64(1));
  return Number.isSafeInteger(offset) ? { offset, payload: bytes.slice(9) } : null;
}

export function splitTerminalInput(payload: Uint8Array): Uint8Array[] {
  const result: Uint8Array[] = [];
  for (let offset = 0; offset < payload.length; offset += maximumTerminalPayloadBytes) {
    result.push(payload.slice(offset, offset + maximumTerminalPayloadBytes));
  }
  return result;
}
