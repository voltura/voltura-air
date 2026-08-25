import { useEffect, useRef, useState } from "react";
import { ArrowRightLeft, Download, Share2, Upload, X } from "lucide-react";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import type { ClientMessage } from "../../foundation/protocol/messages";
import { ModalDialog } from "../../ui/overlays/ModalDialog";
import { supportsDeviceTransferStorage } from "./fileTransferDeviceStorage";
import { useFileTransfer, type FileTransferTarget } from "./useFileTransfer";

export function FileTransferMenu({
  activePc, canModify, clientId, enabled, onPresentationChange, onTransferNotice, onUploadCompleted, send, target
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
  const [open, setOpen] = useState(false);
  const [replacementName, setReplacementName] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const transfer = useFileTransfer(activePc, clientId, enabled, send, onUploadCompleted, onTransferNotice);
  const supportsDownload = supportsDeviceTransferStorage();
  const canDownload = supportsDownload && target.entry?.kind === "file";
  const busy = transfer.presentation.active || transfer.presentation.readyToSave;
  useEffect(() => {
    onPresentationChange?.(busy);
    return () => onPresentationChange?.(false);
  }, [busy, onPresentationChange]);
  useEffect(() => {
    if (!open) {return;}
    const closeOutside = (event: PointerEvent) => {
      if (!triggerRef.current?.contains(event.target as Node) && !menuRef.current?.contains(event.target as Node)) {setOpen(false);}
    };
    document.addEventListener("pointerdown", closeOutside);
    return () => document.removeEventListener("pointerdown", closeOutside);
  }, [open]);
  return <>
    <button ref={triggerRef} type="button" aria-label="Transfer" title="Transfer" data-file-menu-trigger onClick={() => setOpen((value) => !value)}><ArrowRightLeft /><span>Transfer</span></button>
    {open && <div ref={menuRef} className="file-toolbar-menu file-transfer-menu" role="menu" aria-label="Transfer files">
      {supportsDownload && <button role="menuitem" disabled={!canDownload || busy} onClick={() => {setOpen(false); transfer.startDownload(target);}}><Download />Save to this device</button>}
      <button role="menuitem" disabled={!canModify || busy} onClick={() => {setOpen(false); inputRef.current?.click();}}><Upload />Choose file from this device</button>
    </div>}
    <input ref={inputRef} className="file-transfer-input" type="file" onChange={(event) => {
      const file = event.currentTarget.files?.[0];
      event.currentTarget.value = "";
      setOpen(false);
      if (file) {setReplacementName(file.name); transfer.startUpload(target, file);}
    }} />
    {(transfer.presentation.active || transfer.presentation.readyToSave) && <div className="file-transfer-progress" role="status">
      <div><strong>{transfer.presentation.fileName}</strong><span>{transfer.presentation.message}</span></div>
      {!transfer.presentation.readyToSave && <progress max={1} value={transfer.presentation.progress} />}
      {transfer.presentation.readyToSave
        ? <div className="file-transfer-ready-actions"><button type="button" onClick={() => void transfer.saveReadyFile()}><Share2 />Save to Files / Share</button><button type="button" className="icon-action" aria-label="Discard transferred file" title="Discard" onClick={() => void transfer.discardReadyFile()}><X /></button></div>
        : <button type="button" aria-label="Cancel file transfer" onClick={transfer.cancel}><X /></button>}
    </div>}
    <ModalDialog dismissLabel="Cancel" isOpen={transfer.presentation.needsReplacementName} onClose={transfer.cancel} onSubmit={(event) => {
      event.preventDefault();
      return transfer.retryUploadName(replacementName);
    }} submitLabel="Continue" title="File name">
      <label className="file-rename-field">Name<input className="text-input" maxLength={255} value={replacementName} onChange={(event) => setReplacementName(event.target.value)} /></label>
    </ModalDialog>
  </>;
}
