const maxPlaintextBytes = 1024 * 1024;
const maxCursorBytes = 1024 * 1024;

export interface ScreenCursorRecord {
  type: "cursor";
  sequence: bigint;
  visible: boolean;
  x: number;
  y: number;
  hotSpotX: number;
  hotSpotY: number;
  width: number;
  height: number;
  pngBytes?: Uint8Array;
}

export interface ScreenStatusRecord { type: "status"; code: string; message: string; }
export type ScreenPlaintextRecord = ScreenCursorRecord | ScreenStatusRecord;

export function parseScreenPlaintextRecord(bytes: Uint8Array): ScreenPlaintextRecord {
  if (bytes.length === 0 || bytes.length > maxPlaintextBytes) {throw new Error("Invalid screen event size.");}
  if (bytes[0] === 4) {return parseCursor(bytes);}
  if (bytes[0] === 5) {return parseStatus(bytes);}
  throw new Error("Unknown screen event type.");
}

function parseCursor(bytes: Uint8Array): ScreenCursorRecord {
  if (bytes.length < 39) {throw new Error("Truncated cursor record.");}
  const view = dataView(bytes);
  const visibleByte = bytes[9];
  if (visibleByte !== 0 && visibleByte !== 1) {throw new Error("Invalid cursor visibility.");}
  const x = view.getInt32(10, false);
  const y = view.getInt32(14, false);
  const hotSpotX = view.getInt32(18, false);
  const hotSpotY = view.getInt32(22, false);
  const width = view.getInt32(26, false);
  const height = view.getInt32(30, false);
  const format = bytes[34];
  const length = view.getInt32(35, false);
  if (width < 0 || width > 512 || height < 0 || height > 512 || hotSpotX < 0 || hotSpotY < 0 || hotSpotX > width || hotSpotY > height)
    {throw new Error("Invalid cursor geometry.");}
  if (length < 0 || length > maxCursorBytes || length !== bytes.length - 39 || (length === 0 ? format !== 0 : format !== 2))
    {throw new Error("Invalid cursor image.");}
  return {
    type: "cursor",
    sequence: view.getBigInt64(1, false),
    visible: visibleByte === 1,
    x,
    y,
    hotSpotX,
    hotSpotY,
    width,
    height,
    ...(length > 0 ? { pngBytes: bytes.slice(39) } : {})
  };
}

function parseStatus(bytes: Uint8Array): ScreenStatusRecord {
  if (bytes.length < 4) {throw new Error("Truncated status record.");}
  const codeLength = bytes[1]!;
  const messageLength = dataView(bytes).getUint16(2, false);
  if (codeLength === 0 || codeLength > 64 || messageLength === 0 || messageLength > 512 || 4 + codeLength + messageLength !== bytes.length)
    {throw new Error("Invalid status record.");}
  const decoder = new TextDecoder("utf-8", { fatal: true });
  return {
    type: "status",
    code: decoder.decode(bytes.slice(4, 4 + codeLength)),
    message: decoder.decode(bytes.slice(4 + codeLength))
  };
}

function dataView(bytes: Uint8Array): DataView {
  return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
}
