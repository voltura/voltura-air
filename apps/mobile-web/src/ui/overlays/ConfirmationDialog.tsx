import { useRef } from "react";
import { ModalDialog } from "./ModalDialog";

interface ConfirmationDialogProps {
  cancelLabel?: string;
  confirmLabel: string;
  destructive?: boolean;
  description: string;
  initialFocus?: "cancel" | "confirm";
  isOpen: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  title: string;
}

export function ConfirmationDialog({
  cancelLabel = "Cancel",
  confirmLabel,
  destructive = true,
  description,
  initialFocus,
  isOpen,
  onCancel,
  onConfirm,
  title
}: ConfirmationDialogProps) {
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  return (
    <ModalDialog
      actions={(
        <>
          <button ref={confirmButtonRef} type="button" className={`confirmation-dialog-confirm${destructive ? " confirmation-dialog-destructive" : ""}`} onClick={onConfirm}>{confirmLabel}</button>
          <button ref={cancelButtonRef} type="button" className="confirmation-dialog-cancel" onClick={onCancel}>{cancelLabel}</button>
        </>
      )}
      actionsClassName="confirmation-dialog-actions"
      className="confirmation-dialog"
      dismissLabel={cancelLabel}
      initialFocusRef={(initialFocus ?? (destructive ? "cancel" : "confirm")) === "cancel"
        ? cancelButtonRef
        : confirmButtonRef}
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
    >
      <p>{description}</p>
    </ModalDialog>
  );
}
