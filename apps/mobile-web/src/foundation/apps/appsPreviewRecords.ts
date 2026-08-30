const version = 1;
const requestKind = 1;
const headerKind = 2;
const dataKind = 3;
const opaqueIdLength = 32;
const maximumRequestedPreviews = 3;
const maximumPreviewBytes = 1536 * 1024;
const maximumPreviewWidth = 1024;
const maximumPreviewHeight = 640;
const maximumChunkBytes = 48 * 1024;
const headerBytes = 1 + opaqueIdLength + 1 + 2 + 2 + 4 + 1;
const dataHeaderBytes = 1 + opaqueIdLength + 4;
const encoder = new TextEncoder();
const decoder = new TextDecoder("ascii", { fatal: true });
const opaqueIdPattern = /^[a-f0-9]{32}$/u;

export type AppsPreviewRecord =
  | {
      kind: "header";
      windowId: string;
      available: boolean;
      width: number;
      height: number;
      encodedBytes: number;
    }
  | { kind: "data"; windowId: string; offset: number; payload: Uint8Array };

export type AppsPreviewAssembly =
  | { kind: "unavailable"; windowId: string }
  | { kind: "complete"; windowId: string; width: number; height: number; blob: Blob };

export function createAppsPreviewRequest(revision: string, windowIds: string[]): ArrayBuffer {
  const uniqueIds = [...new Set(windowIds)];
  if (
    !opaqueIdPattern.test(revision) ||
    uniqueIds.length < 1 ||
    uniqueIds.length > maximumRequestedPreviews ||
    uniqueIds.some((id) => !opaqueIdPattern.test(id))
  ) {
    throw new TypeError("Invalid Apps preview request.");
  }

  const result = new Uint8Array(1 + opaqueIdLength + 1 + uniqueIds.length * opaqueIdLength);
  result[0] = (version << 4) | requestKind;
  result.set(encoder.encode(revision), 1);
  result[1 + opaqueIdLength] = uniqueIds.length;
  uniqueIds.forEach((id, index) => {
    result.set(encoder.encode(id), 1 + opaqueIdLength + 1 + index * opaqueIdLength);
  });
  return result.buffer;
}

export function parseAppsPreviewRecord(data: ArrayBuffer): AppsPreviewRecord | null {
  const bytes = new Uint8Array(data);
  const discriminator = bytes[0];
  if (discriminator === undefined || discriminator >> 4 !== version) {
    return null;
  }
  const kind = discriminator & 0x0f;

  if (kind === headerKind) {
    if (bytes.length !== headerBytes) {
      return null;
    }
    const windowId = decodeId(bytes.subarray(1, 1 + opaqueIdLength));
    const view = new DataView(data);
    const available = bytes[1 + opaqueIdLength] === 1;
    const width = view.getUint16(2 + opaqueIdLength);
    const height = view.getUint16(4 + opaqueIdLength);
    const encodedBytes = view.getUint32(6 + opaqueIdLength);
    if (
      !windowId ||
      (bytes[1 + opaqueIdLength] !== 0 && !available) ||
      bytes[10 + opaqueIdLength] !== 1 ||
      width > maximumPreviewWidth ||
      height > maximumPreviewHeight ||
      encodedBytes > maximumPreviewBytes ||
      available !== (width > 0 && height > 0 && encodedBytes > 0)
    ) {
      return null;
    }
    return { kind: "header", windowId, available, width, height, encodedBytes };
  }

  if (kind === dataKind) {
    if (bytes.length <= dataHeaderBytes || bytes.length > dataHeaderBytes + maximumChunkBytes) {
      return null;
    }
    const windowId = decodeId(bytes.subarray(1, 1 + opaqueIdLength));
    const offset = new DataView(data).getUint32(1 + opaqueIdLength);
    return windowId
      ? { kind: "data", windowId, offset, payload: bytes.slice(dataHeaderBytes) }
      : null;
  }

  return null;
}

export class AppsPreviewAssembler {
  readonly #pending = new Map<
    string,
    { width: number; height: number; bytes: Uint8Array; received: number }
  >();

  accept(record: AppsPreviewRecord): AppsPreviewAssembly | null | undefined {
    if (record.kind === "header") {
      this.#pending.delete(record.windowId);
      if (!record.available) {
        return { kind: "unavailable", windowId: record.windowId };
      }
      if (this.#pending.size >= maximumRequestedPreviews) {
        return null;
      }
      this.#pending.set(record.windowId, {
        width: record.width,
        height: record.height,
        bytes: new Uint8Array(record.encodedBytes),
        received: 0,
      });
      return undefined;
    }

    const pending = this.#pending.get(record.windowId);
    if (
      !pending ||
      record.offset !== pending.received ||
      record.payload.length > pending.bytes.length - pending.received
    ) {
      return null;
    }
    pending.bytes.set(record.payload, pending.received);
    pending.received += record.payload.length;
    if (pending.received !== pending.bytes.length) {
      return undefined;
    }

    this.#pending.delete(record.windowId);
    return {
      kind: "complete",
      windowId: record.windowId,
      width: pending.width,
      height: pending.height,
      blob: new Blob([pending.bytes.slice().buffer], { type: "image/jpeg" }),
    };
  }

  clear(): void {
    this.#pending.clear();
  }
}

function decodeId(bytes: Uint8Array): string | null {
  try {
    const id = decoder.decode(bytes);
    return opaqueIdPattern.test(id) ? id : null;
  } catch {
    return null;
  }
}
