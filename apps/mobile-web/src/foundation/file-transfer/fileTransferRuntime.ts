import type {
  FileManagerEntry,
  FileTransferCancelMessage,
  FileTransferDirection,
} from "../protocol/messages";

export interface FileTransferTarget {
  sessionId: string;
  panel: "left" | "right";
  revision: string;
  entry: FileManagerEntry | null;
}

export interface ScreenCaptureTransferTarget {
  screenOperationId: string;
  displayId: string;
}

type RuntimeTarget =
  | ({ kind: "file" } & Omit<FileTransferTarget, "entry"> & { entryId: string })
  | ({ kind: "screen-capture" } & ScreenCaptureTransferTarget);

export interface FileTransferPresentation {
  active: boolean;
  fileName: string;
  message: string;
  needsReplacementName: boolean;
  progress: number;
  readyToSave: boolean;
}

export const idleFileTransferPresentation = (message = ""): FileTransferPresentation => ({
  active: false,
  fileName: "",
  message,
  needsReplacementName: false,
  progress: 0,
  readyToSave: false,
});

export function createFileTransferCancelMessage(
  operationId: string,
  runtime: TransferRuntime,
): FileTransferCancelMessage {
  return {
    type: "file.transfer.cancel",
    operationId,
    ...(runtime.transferId
      ? { transferId: runtime.transferId }
      : { requestId: runtime.operationId }),
  };
}

export interface TransferRuntime {
  operationId: string;
  transferId: string;
  direction: FileTransferDirection;
  fileName: string;
  declaredSize: number;
  uploadFile: File | null;
  peer: RTCPeerConnection | null;
  channel: RTCDataChannel | null;
  offerHash: string;
  writable: FileSystemWritableFileStream | null;
  directory: FileSystemDirectoryHandle | null;
  handle: FileSystemFileHandle | null;
  readyFile: File | null;
  storedName: string;
  received: number;
  sent: number;
  acknowledged: number;
  transportComplete: boolean;
  receiveChain: Promise<void>;
  pumping: boolean;
  target: RuntimeTarget;
}
