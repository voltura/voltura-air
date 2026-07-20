import { useRef } from "react";
import { ModalDialog } from "./ModalDialog";

interface ConfirmationDialogProps {
  confirmLabel: string;
  destructive?: boolean;
  description: string;
  isOpen: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  title: string;
}

export function ConfirmationDialog({ confirmLabel, destructive = true, description, isOpen, onCancel, onConfirm, title }: ConfirmationDialogProps) {
  const confirmButtonRef = useRef<HTMLButtonElement>(null);

  return (
    <ModalDialog
      actions={(
        <>
          <button ref={confirmButtonRef} type="button" className={`confirmation-dialog-confirm${destructive ? " confirmation-dialog-destructive" : ""}`} onClick={onConfirm}>{confirmLabel}</button>
          <button type="button" className="confirmation-dialog-cancel" onClick={onCancel}>Cancel</button>
        </>
      )}
      actionsClassName="confirmation-dialog-actions"
      className="confirmation-dialog"
      dismissLabel="Cancel"
      initialFocusRef={confirmButtonRef}
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
    >
      <p>{description}</p>
    </ModalDialog>
  );
}
