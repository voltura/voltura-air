import { useRef } from "react";
import { ModalDialog } from "./ModalDialog";

interface ConfirmationDialogProps {
  confirmLabel: string;
  description: string;
  isOpen: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  title: string;
}

export function ConfirmationDialog({ confirmLabel, description, isOpen, onCancel, onConfirm, title }: ConfirmationDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  return (
    <ModalDialog
      actions={(
        <>
          <button ref={cancelButtonRef} type="button" className="confirmation-dialog-cancel" onClick={onCancel}>Cancel</button>
          <button type="button" className="confirmation-dialog-confirm" onClick={onConfirm}>{confirmLabel}</button>
        </>
      )}
      actionsClassName="confirmation-dialog-actions"
      className="confirmation-dialog"
      dismissLabel="Cancel"
      focusDismissAction
      initialFocusRef={cancelButtonRef}
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
    >
      <p>{description}</p>
    </ModalDialog>
  );
}
