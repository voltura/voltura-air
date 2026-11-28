import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { ArrowRightLeft, Camera, Download, Share2, Upload, X } from "lucide-react";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import type { ClientMessage } from "../../foundation/protocol/messages";
import { ModalDialog } from "../../ui/overlays/ModalDialog";
import { supportsDeviceFileStorage } from "../../foundation/file-transfer/fileTransferDeviceStorage";
import {
  useFileTransfer,
  type FileTransferTarget,
} from "../../foundation/file-transfer/useFileTransfer";

export function FileTransferMenu({
  activePc,
  canModify,
  clientId,
  enabled,
  onPresentationChange,
  onTransferNotice,
  onUploadCompleted,
  send,
  target,
}: {
  activePc: PcProfile;
  canModify: boolean;
  clientId: string;
  enabled: boolean;
  onPresentationChange?: (presented: boolean) => void;
  onTransferNotice?: (message: string, tone: "success" | "error" | "neutral") => void;
  onUploadCompleted?: (panel: "left" | "right", fileName: string) => void;
  send: (message: ClientMessage) => void;
  target: FileTransferTarget;
}) {
  const [openMenuKey, setOpenMenuKey] = useState<string | null>(null);
  const [replacementName, setReplacementName] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const photoInputRef = useRef<HTMLInputElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const transfer = useFileTransfer(
    activePc,
    clientId,
    enabled,
    send,
    onUploadCompleted,
    onTransferNotice,
  );
  const supportsDownload = supportsDeviceFileStorage();
  const canDownload = supportsDownload && target.entry?.kind === "file";
  const busy = transfer.presentation.active || transfer.presentation.readyToSave;
  const destinationKey = JSON.stringify([
    activePc.id,
    activePc.hostIdentityPublicKey,
    clientId,
    target.sessionId,
    target.panel,
    target.revision,
  ]);
  const menuKey = JSON.stringify([destinationKey, target.entry?.id]);
  const open = enabled && openMenuKey === menuKey;
  if (!open && openMenuKey !== null) {
    setOpenMenuKey(null);
  }
  const pickerRef = useRef<{ destinationKey: string; input: HTMLInputElement } | null>(null);
  useLayoutEffect(() => {
    pickerRef.current = null;
  }, [canModify, destinationKey, enabled]);
  const openPicker = (input: HTMLInputElement | null) => {
    setOpenMenuKey(null);
    if (input) {
      pickerRef.current = { destinationKey, input };
      input.click();
    }
  };
  const uploadSelectedFile = (input: HTMLInputElement) => {
    const file = input.files?.[0];
    input.value = "";
    setOpenMenuKey(null);
    const picker = pickerRef.current;
    if (picker?.input === input) {
      pickerRef.current = null;
    }
    if (file) {
      if (
        !enabled ||
        !canModify ||
        busy ||
        picker?.input !== input ||
        picker.destinationKey !== destinationKey
      ) {
        onTransferNotice?.(
          "The destination or file permissions changed. Choose the file again.",
          "error",
        );
        return;
      }
      setReplacementName(file.name);
      transfer.startUpload(target, file);
    }
  };
  useEffect(() => {
    onPresentationChange?.(busy);
    return () => onPresentationChange?.(false);
  }, [busy, onPresentationChange]);
  useEffect(() => {
    if (!open) {
      return;
    }
    const closeOutside = (event: PointerEvent) => {
      if (
        !triggerRef.current?.contains(event.target as Node) &&
        !menuRef.current?.contains(event.target as Node)
      ) {
        setOpenMenuKey(null);
      }
    };
    document.addEventListener("pointerdown", closeOutside);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        setOpenMenuKey(null);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);
  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        aria-label="Transfer"
        title="Transfer"
        data-file-menu-trigger
        disabled={!enabled}
        onClick={() => setOpenMenuKey(open ? null : menuKey)}
      >
        <ArrowRightLeft />
        <span>Transfer</span>
      </button>
      {open && (
        <div
          ref={menuRef}
          className="file-toolbar-menu file-transfer-menu"
          role="menu"
          aria-label="Transfer files"
        >
          {supportsDownload && (
            <button
              role="menuitem"
              disabled={!enabled || !canDownload || busy}
              onClick={() => {
                setOpenMenuKey(null);
                transfer.startDownload(target);
              }}
            >
              <Download />
              Save to this device
            </button>
          )}
          <button
            role="menuitem"
            disabled={!enabled || !canModify || busy}
            onClick={() => openPicker(inputRef.current)}
          >
            <Upload />
            Choose file from this device
          </button>
          <button
            role="menuitem"
            disabled={!enabled || !canModify || busy}
            onClick={() => openPicker(photoInputRef.current)}
          >
            <Camera />
            Take photo
          </button>
        </div>
      )}
      <input
        ref={inputRef}
        className="file-transfer-input"
        type="file"
        onChange={(event) => uploadSelectedFile(event.currentTarget)}
      />
      <input
        ref={photoInputRef}
        className="file-transfer-input"
        type="file"
        accept="image/*"
        capture="environment"
        onChange={(event) => uploadSelectedFile(event.currentTarget)}
      />
      {(transfer.presentation.active || transfer.presentation.readyToSave) && (
        <div className="file-transfer-progress" role="status">
          <div>
            <strong>{transfer.presentation.fileName}</strong>
            <span>{transfer.presentation.message}</span>
          </div>
          {!transfer.presentation.readyToSave && (
            <progress max={1} value={transfer.presentation.progress} />
          )}
          {transfer.presentation.readyToSave ? (
            <div className="file-transfer-ready-actions">
              <button type="button" onClick={() => void transfer.saveReadyFile()}>
                <Share2 />
                Save to Files / Share
              </button>
              <button
                type="button"
                className="icon-action"
                aria-label="Discard transferred file"
                title="Discard"
                onClick={() => void transfer.discardReadyFile()}
              >
                <X />
              </button>
            </div>
          ) : (
            <button type="button" aria-label="Cancel file transfer" onClick={transfer.cancel}>
              <X />
            </button>
          )}
        </div>
      )}
      <ModalDialog
        dismissLabel="Cancel"
        isOpen={transfer.presentation.needsReplacementName}
        onClose={transfer.cancel}
        onSubmit={(event) => {
          event.preventDefault();
          return transfer.retryUploadName(replacementName);
        }}
        submitLabel="Continue"
        title="File name"
      >
        <label className="file-rename-field">
          Name
          <input
            className="text-input"
            maxLength={255}
            value={replacementName}
            onChange={(event) => setReplacementName(event.target.value)}
          />
        </label>
      </ModalDialog>
    </>
  );
}
