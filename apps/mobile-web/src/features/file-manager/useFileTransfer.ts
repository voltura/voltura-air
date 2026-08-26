import { useCallback, useEffect, useRef, useState } from "react";
import { subscribeFileManagerResults } from "../../foundation/connection/fileManagerResultBus";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { createLocalId } from "../../foundation/identity/localId";
import type {
  ClientMessage,
  FileTransferDirection,
  FileTransferOfferMessage,
  FileTransferResultMessage,
} from "../../foundation/protocol/messages";
import { hasOnlyRelayCandidates, waitForIceGathering } from "../../foundation/webrtc/iceGathering";
import {
  hashSessionDescription,
  verifyHostSessionSignature,
} from "../../foundation/webrtc/sessionCrypto";
import {
  createFileTransferAcknowledgement,
  createFileTransferDataRecord,
  maximumFileTransferPayloadBytes,
  maximumUnacknowledgedFileTransferBytes,
  parseFileTransferRecord,
} from "./fileTransferRecords";
import {
  prepareDeviceTransferStorage,
  removeDeviceTransferFile,
  saveOrShareDeviceTransfer,
  supportsDeviceTransferStorage,
  sweepDeviceTransferStorage,
} from "./fileTransferDeviceStorage";
import {
  createFileTransferAnswerTranscript,
  createFileTransferOfferTranscript,
  createFileTransferStartTranscript,
} from "./fileTransferTranscripts";
import {
  createFileTransferCancelMessage,
  idleFileTransferPresentation as idlePresentation,
  type FileTransferTarget,
  type TransferRuntime,
} from "./fileTransferRuntime";

export type { FileTransferPresentation, FileTransferTarget } from "./fileTransferRuntime";

const maximumSafeFileSize = Number.MAX_SAFE_INTEGER;

export function useFileTransfer(
  activePc: PcProfile,
  clientId: string,
  enabled: boolean,
  send: (message: ClientMessage) => void,
  onUploadCompleted?: (panel: "left" | "right", fileName: string) => void,
  onTransferNotice?: (message: string, tone: "success" | "error" | "neutral") => void,
) {
  const canSaveToDevice = supportsDeviceTransferStorage();
  const [presentation, setPresentation] = useState(() => idlePresentation());
  const runtimeRef = useRef<TransferRuntime | null>(null);
  const initialSweepRef = useRef<Promise<void> | null>(null);
  const ensureInitialSweep = useCallback(() => {
    initialSweepRef.current ??= canSaveToDevice ? sweepDeviceTransferStorage() : Promise.resolve();
    return initialSweepRef.current;
  }, [canSaveToDevice]);

  const removeStoredFile = useCallback(async (runtime: TransferRuntime) => {
    if (!runtime.directory || !runtime.storedName) {
      return;
    }
    await removeDeviceTransferFile(runtime.directory, runtime.storedName);
    runtime.handle = null;
    runtime.storedName = "";
  }, []);

  const closeRuntime = useCallback(
    async (runtime: TransferRuntime, removeStored: boolean) => {
      runtime.channel?.close();
      runtime.peer?.close();
      runtime.channel = null;
      runtime.peer = null;
      if (runtime.writable) {
        try {
          await runtime.writable.abort();
        } catch {
          /* The host still owns the authoritative result. */
        }
        runtime.writable = null;
      }
      if (removeStored) {
        try {
          await removeStoredFile(runtime);
        } catch {
          /* The next Files start retries the sweep. */
        }
      }
    },
    [removeStoredFile],
  );

  const fail = useCallback(
    (
      runtime: TransferRuntime,
      message: string,
      cancelHost = true,
      tone: "error" | "neutral" = "error",
    ) => {
      if (runtimeRef.current !== runtime) {
        return;
      }
      if (cancelHost && (runtime.transferId || runtime.operationId)) {
        send(createFileTransferCancelMessage(createLocalId(), runtime));
      }
      runtimeRef.current = null;
      void closeRuntime(runtime, true);
      setPresentation(idlePresentation(message));
      onTransferNotice?.(message, tone);
    },
    [closeRuntime, onTransferNotice, send],
  );

  const updateProgress = useCallback(
    (runtime: TransferRuntime, completed: number, message = "Transferring…") => {
      if (runtimeRef.current !== runtime) {
        return;
      }
      setPresentation({
        active: true,
        fileName: runtime.fileName,
        message,
        needsReplacementName: false,
        progress: runtime.declaredSize === 0 ? 1 : Math.min(1, completed / runtime.declaredSize),
        readyToSave: false,
      });
    },
    [],
  );

  const pumpUpload = useCallback(
    async (runtime: TransferRuntime) => {
      const channel = runtime.channel;
      const file = runtime.uploadFile;
      if (
        !channel ||
        !file ||
        channel.readyState !== "open" ||
        runtime.pumping ||
        runtimeRef.current !== runtime
      ) {
        return;
      }
      runtime.pumping = true;
      try {
        while (
          runtime.sent < runtime.declaredSize &&
          runtime.sent - runtime.acknowledged < maximumUnacknowledgedFileTransferBytes &&
          channel.bufferedAmount < maximumUnacknowledgedFileTransferBytes
        ) {
          const count = Math.min(
            maximumFileTransferPayloadBytes,
            runtime.declaredSize - runtime.sent,
            maximumUnacknowledgedFileTransferBytes - (runtime.sent - runtime.acknowledged),
          );
          const payload = new Uint8Array(
            await file.slice(runtime.sent, runtime.sent + count).arrayBuffer(),
          );
          if (runtimeRef.current !== runtime || channel.readyState !== "open") {
            return;
          }
          channel.send(createFileTransferDataRecord(runtime.sent, payload));
          runtime.sent += payload.byteLength;
        }
      } catch {
        fail(runtime, "The file could not be sent.");
      } finally {
        runtime.pumping = false;
      }
    },
    [fail],
  );

  const acceptDownloadRecord = useCallback(
    async (runtime: TransferRuntime, data: unknown) => {
      const record = parseFileTransferRecord(data);
      if (
        record?.kind !== "data" ||
        record.offset !== runtime.received ||
        runtime.received + record.payload.byteLength > runtime.declaredSize ||
        !runtime.writable ||
        !runtime.channel
      ) {
        fail(runtime, "The PC sent invalid file data.");
        return;
      }
      try {
        await runtime.writable.write({
          type: "write",
          position: runtime.received,
          data: Uint8Array.from(record.payload).buffer,
        });
        runtime.received += record.payload.byteLength;
        runtime.channel.send(createFileTransferAcknowledgement(runtime.received));
        updateProgress(runtime, runtime.received);
      } catch {
        fail(runtime, "This device could not store the file.");
      }
    },
    [fail, updateProgress],
  );

  const attachChannel = useCallback(
    (runtime: TransferRuntime, channel: RTCDataChannel) => {
      if (
        channel.label !== "voltura-file-transfer" ||
        runtime.channel ||
        runtimeRef.current !== runtime
      ) {
        channel.close();
        return;
      }
      runtime.channel = channel;
      channel.binaryType = "arraybuffer";
      channel.bufferedAmountLowThreshold = maximumFileTransferPayloadBytes;
      channel.addEventListener("open", () => {
        updateProgress(runtime, 0);
        if (runtime.direction === "upload") {
          void pumpUpload(runtime);
        }
      });
      channel.addEventListener("bufferedamountlow", () => {
        if (runtime.direction === "upload") {
          void pumpUpload(runtime);
        }
      });
      channel.addEventListener("message", (event) => {
        if (runtimeRef.current !== runtime) {
          return;
        }
        if (runtime.direction === "download") {
          runtime.receiveChain = runtime.receiveChain.then(() =>
            acceptDownloadRecord(runtime, event.data),
          );
          return;
        }
        const record = parseFileTransferRecord(event.data);
        if (
          record?.kind !== "acknowledgement" ||
          record.offset < runtime.acknowledged ||
          record.offset > runtime.sent
        ) {
          fail(runtime, "The PC returned invalid transfer progress.");
          return;
        }
        runtime.acknowledged = record.offset;
        updateProgress(runtime, record.offset);
        void pumpUpload(runtime);
      });
    },
    [acceptDownloadRecord, fail, pumpUpload, updateProgress],
  );

  const prepareDownload = useCallback(
    async (runtime: TransferRuntime) => {
      if (!canSaveToDevice) {
        throw new Error("Saving files is unsupported in this browser.");
      }
      await ensureInitialSweep();
      const storage = await prepareDeviceTransferStorage(runtime.declaredSize, runtime.transferId);
      if (runtimeRef.current !== runtime) {
        try {
          await storage.writable.abort();
        } catch {
          /* The abandoned partial is removed below. */
        }
        try {
          await removeDeviceTransferFile(storage.directory, storage.storedName);
        } catch {
          /* The next Files start retries the sweep. */
        }
        throw new DOMException("File transfer canceled.", "AbortError");
      }
      runtime.directory = storage.directory;
      runtime.handle = storage.handle;
      runtime.storedName = storage.storedName;
      runtime.writable = storage.writable;
    },
    [canSaveToDevice, ensureInitialSweep],
  );

  const acceptOffer = useCallback(
    async (message: FileTransferOfferMessage) => {
      const runtime = runtimeRef.current;
      if (
        runtime?.transferId !== message.transferId ||
        runtime.direction !== message.direction ||
        runtime.peer ||
        !activePc.hostIdentityPublicKey ||
        message.declaredSize < 0 ||
        !Number.isSafeInteger(message.declaredSize)
      ) {
        return;
      }
      runtime.fileName = message.fileName;
      runtime.declaredSize = message.declaredSize;
      const offerHash = hashSessionDescription(message.offerSdp);
      const hostTranscript = createFileTransferOfferTranscript(
        clientId,
        activePc.hostIdentityPublicKey,
        runtime.operationId,
        message.transferId,
        message.direction,
        message.fileName,
        message.declaredSize,
        offerHash,
      );
      if (
        !verifyHostSessionSignature(
          activePc.hostIdentityPublicKey,
          message.hostSignature,
          hostTranscript,
        )
      ) {
        fail(runtime, "The PC identity signature was invalid.");
        return;
      }
      const relayMode = activePc.transportMode === "relay";
      if (relayMode && (!message.iceServers || message.iceServers.length === 0)) {
        fail(runtime, "Relay credentials were unavailable.");
        return;
      }
      try {
        if (runtime.direction === "download") {
          await prepareDownload(runtime);
        }
        if (runtimeRef.current !== runtime) {
          return;
        }
        const peer = new RTCPeerConnection({
          iceServers: message.iceServers ?? [],
          iceTransportPolicy: relayMode ? "relay" : "all",
        });
        runtime.peer = peer;
        runtime.offerHash = offerHash;
        peer.addEventListener("datachannel", (event) => attachChannel(runtime, event.channel));
        peer.addEventListener("connectionstatechange", () => {
          if (
            runtimeRef.current === runtime &&
            !runtime.transportComplete &&
            (peer.connectionState === "failed" || peer.connectionState === "closed")
          ) {
            fail(runtime, "The file connection was lost.");
          }
        });
        await peer.setRemoteDescription({ type: "offer", sdp: message.offerSdp });
        if (runtimeRef.current !== runtime) {
          return;
        }
        const answer = await peer.createAnswer();
        if (runtimeRef.current !== runtime) {
          return;
        }
        await peer.setLocalDescription(answer);
        if (runtimeRef.current !== runtime) {
          return;
        }
        await waitForIceGathering(peer, relayMode);
        if (runtimeRef.current !== runtime) {
          return;
        }
        const answerSdp = peer.localDescription?.sdp;
        if (
          !answerSdp ||
          answerSdp.length > 32 * 1024 ||
          (relayMode && !hasOnlyRelayCandidates(answerSdp))
        ) {
          throw new Error("Invalid answer.");
        }
        const answerHash = hashSessionDescription(answerSdp);
        const transcript = createFileTransferAnswerTranscript(
          clientId,
          activePc.hostIdentityPublicKey,
          runtime.operationId,
          runtime.transferId,
          runtime.direction,
          runtime.fileName,
          runtime.declaredSize,
          offerHash,
          answerHash,
        );
        const signature = signClientPayload(clientId, activePc.id, transcript);
        if (!signature) {
          throw new Error("Missing reconnect key.");
        }
        send({
          type: "file.transfer.answer",
          operationId: createLocalId(),
          transferId: runtime.transferId,
          answerSdp,
          clientSignature: signature,
        });
        updateProgress(runtime, 0, "Connecting…");
      } catch (error) {
        if (
          error instanceof DOMException &&
          error.name === "AbortError" &&
          runtimeRef.current !== runtime
        ) {
          return;
        }
        fail(
          runtime,
          error instanceof Error ? error.message : "The file connection could not be created.",
        );
      }
    },
    [activePc, attachChannel, clientId, fail, prepareDownload, send, updateProgress],
  );

  const finish = useCallback(
    async (message: FileTransferResultMessage) => {
      const runtime = runtimeRef.current;
      if (runtime?.transferId !== message.transferId || runtime.direction !== message.direction) {
        return;
      }
      await runtime.receiveChain;
      if (runtimeRef.current !== runtime) {
        return;
      }
      if (!message.succeeded) {
        fail(runtime, message.message, false);
        return;
      }
      if (runtime.direction === "download") {
        if (runtime.received !== runtime.declaredSize || !runtime.writable || !runtime.handle) {
          fail(runtime, "The received file was incomplete.");
          return;
        }
        let stored: File;
        try {
          await runtime.writable.close();
          runtime.writable = null;
          stored = await runtime.handle.getFile();
        } catch {
          fail(runtime, "This device could not finish storing the file.");
          return;
        }
        if (runtimeRef.current !== runtime) {
          return;
        }
        if (stored.size !== runtime.declaredSize) {
          fail(runtime, "The received file was incomplete.");
          return;
        }
        runtime.readyFile = stored;
        runtime.transportComplete = true;
        runtime.channel?.close();
        runtime.peer?.close();
        setPresentation({
          active: false,
          fileName: runtime.fileName,
          message: message.message,
          needsReplacementName: false,
          progress: 1,
          readyToSave: true,
        });
        return;
      }
      runtimeRef.current = null;
      await closeRuntime(runtime, true);
      setPresentation(idlePresentation(message.message));
      onUploadCompleted?.(runtime.target.panel, message.fileName);
    },
    [closeRuntime, fail, onUploadCompleted],
  );

  useEffect(
    () =>
      subscribeFileManagerResults((message) => {
        const runtime = runtimeRef.current;
        if (
          message.type === "file.transfer.start.result" &&
          runtime?.operationId === message.operationId
        ) {
          if (!message.succeeded || !message.transferId) {
            if (message.code === "invalid-name" && runtime.direction === "upload") {
              setPresentation({
                active: false,
                fileName: runtime.fileName,
                message: message.message,
                needsReplacementName: true,
                progress: 0,
                readyToSave: false,
              });
            } else {
              fail(runtime, message.message, false);
            }
            return;
          }
          runtime.transferId = message.transferId;
          setPresentation((current) => ({
            ...current,
            message:
              runtime.direction === "upload" && message.job?.state === "queued"
                ? "Queued…"
                : "Preparing…",
          }));
        } else if (message.type === "file.transfer.offer") {
          void acceptOffer(message);
        } else if (
          message.type === "file.transfer.status" &&
          runtime?.transferId === message.transferId
        ) {
          updateProgress(
            runtime,
            message.bytesCompleted,
            message.state === "queued"
              ? "Queued…"
              : message.state === "connecting"
                ? "Connecting…"
                : "Transferring…",
          );
        } else if (message.type === "file.transfer.result") {
          void finish(message);
        }
      }),
    [acceptOffer, fail, finish, updateProgress],
  );

  useEffect(() => {
    void ensureInitialSweep();
    return () => {
      const runtime = runtimeRef.current;
      runtimeRef.current = null;
      if (runtime?.transferId || runtime?.operationId) {
        send(createFileTransferCancelMessage(createLocalId(), runtime));
      }
      if (runtime) {
        void closeRuntime(runtime, true);
      }
    };
  }, [closeRuntime, ensureInitialSweep, send]);

  useEffect(() => {
    if (enabled) {
      return;
    }
    const runtime = runtimeRef.current;
    if (runtime) {
      fail(runtime, "File transfer is no longer available.");
    }
  }, [enabled, fail]);

  const publishStart = useCallback(
    (runtime: TransferRuntime) => {
      const hostPublicKey = activePc.hostIdentityPublicKey;
      if (!hostPublicKey) {
        fail(runtime, "Pair this device again to transfer files.", false);
        return;
      }
      const operationId = createLocalId();
      runtime.operationId = operationId;
      const entryId = runtime.direction === "download" ? runtime.target.entryId : "";
      const fileName = runtime.direction === "upload" ? runtime.fileName : "";
      const declaredSize = runtime.direction === "upload" ? runtime.declaredSize : null;
      const transcript = createFileTransferStartTranscript(
        clientId,
        hostPublicKey,
        operationId,
        runtime.direction,
        runtime.target.sessionId,
        runtime.target.panel,
        runtime.target.revision,
        entryId,
        fileName,
        declaredSize,
      );
      const signature = signClientPayload(clientId, activePc.id, transcript);
      if (!signature) {
        fail(runtime, "Pair this device again to transfer files.", false);
        return;
      }
      setPresentation({
        active: true,
        fileName: runtime.fileName,
        message: runtime.direction === "upload" ? "Queueing…" : "Preparing…",
        needsReplacementName: false,
        progress: 0,
        readyToSave: false,
      });
      send({
        type: "file.transfer.start",
        operationId,
        direction: runtime.direction,
        sessionId: runtime.target.sessionId,
        panel: runtime.target.panel,
        revision: runtime.target.revision,
        ...(runtime.direction === "download"
          ? { entryId }
          : { fileName, declaredSize: declaredSize! }),
        clientSignature: signature,
      });
    },
    [activePc.hostIdentityPublicKey, activePc.id, clientId, fail, send],
  );

  const start = useCallback(
    (direction: FileTransferDirection, target: FileTransferTarget, uploadFile: File | null) => {
      if (
        !enabled ||
        runtimeRef.current ||
        !activePc.hostIdentityPublicKey ||
        typeof RTCPeerConnection === "undefined"
      ) {
        return;
      }
      if (direction === "download" && (!canSaveToDevice || target.entry?.kind !== "file")) {
        return;
      }
      if (
        direction === "upload" &&
        (!uploadFile ||
          !Number.isSafeInteger(uploadFile.size) ||
          uploadFile.size > maximumSafeFileSize)
      ) {
        return;
      }
      const runtime: TransferRuntime = {
        operationId: "",
        transferId: "",
        direction,
        fileName: uploadFile?.name ?? target.entry?.name ?? "",
        declaredSize: uploadFile?.size ?? 0,
        uploadFile,
        peer: null,
        channel: null,
        offerHash: "",
        writable: null,
        directory: null,
        handle: null,
        readyFile: null,
        storedName: "",
        received: 0,
        sent: 0,
        acknowledged: 0,
        transportComplete: false,
        receiveChain: Promise.resolve(),
        pumping: false,
        target: {
          sessionId: target.sessionId,
          panel: target.panel,
          revision: target.revision,
          entryId: target.entry?.id ?? "",
        },
      };
      runtimeRef.current = runtime;
      publishStart(runtime);
    },
    [activePc.hostIdentityPublicKey, canSaveToDevice, enabled, publishStart],
  );

  const retryUploadName = useCallback(
    (fileName: string) => {
      const runtime = runtimeRef.current;
      if (
        runtime?.direction !== "upload" ||
        !presentation.needsReplacementName ||
        !fileName.trim() ||
        fileName.length > 255
      ) {
        return false;
      }
      runtime.fileName = fileName;
      publishStart(runtime);
      return true;
    },
    [presentation.needsReplacementName, publishStart],
  );

  const cancel = useCallback(() => {
    const runtime = runtimeRef.current;
    if (runtime) {
      fail(runtime, "File transfer canceled.", true, "neutral");
    }
  }, [fail]);

  const saveReadyFile = useCallback(async () => {
    const runtime = runtimeRef.current;
    if (runtime?.direction !== "download" || !runtime.readyFile || !presentation.readyToSave) {
      return;
    }
    try {
      const result = await saveOrShareDeviceTransfer(runtime.readyFile, runtime.fileName);
      if (result === "shared") {
        runtimeRef.current = null;
        await closeRuntime(runtime, true);
        setPresentation(idlePresentation("Shared."));
        onTransferNotice?.("Shared.", "success");
        return;
      }
      runtimeRef.current = null;
      await closeRuntime(runtime, true);
      setPresentation(idlePresentation("Download started."));
      onTransferNotice?.("Download started.", "success");
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        return;
      }
      setPresentation((current) => ({ ...current, message: "The file could not be saved." }));
      onTransferNotice?.("The file could not be saved.", "error");
    }
  }, [closeRuntime, onTransferNotice, presentation.readyToSave]);

  const discardReadyFile = useCallback(async () => {
    const runtime = runtimeRef.current;
    if (runtime?.direction !== "download" || !presentation.readyToSave) {
      return;
    }
    runtimeRef.current = null;
    await closeRuntime(runtime, true);
    setPresentation(idlePresentation());
  }, [closeRuntime, presentation.readyToSave]);

  return {
    cancel,
    discardReadyFile,
    presentation,
    retryUploadName,
    saveReadyFile,
    startDownload: (target: FileTransferTarget) => start("download", target, null),
    startUpload: (target: FileTransferTarget, file: File) => start("upload", target, file),
  };
}
