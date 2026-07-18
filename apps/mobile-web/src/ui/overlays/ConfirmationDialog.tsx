import { useEffect, useId, useRef } from "react";
import { createPortal } from "react-dom";

interface ConfirmationDialogProps {
  confirmLabel: string;
  description: string;
  isOpen: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  title: string;
}

export function ConfirmationDialog({ confirmLabel, description, isOpen, onCancel, onConfirm, title }: ConfirmationDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog) {
      return;
    }

    if (!dialog.open) {
      if (typeof dialog.showModal === "function") {
        dialog.showModal();
      } else {
        dialog.setAttribute("open", "");
      }
    }
    cancelButtonRef.current?.focus();

    const cancelFromBackdrop = (event: MouseEvent) => {
      if (event.target !== dialog) {
        return;
      }

      event.stopPropagation();
      onCancel();
    };

    dialog.addEventListener("click", cancelFromBackdrop);
    return () => { dialog.removeEventListener("click", cancelFromBackdrop); };
  }, [isOpen, onCancel]);

  if (!isOpen) {
    return null;
  }

  return createPortal(
    <dialog
      ref={dialogRef}
      className="info-dialog confirmation-dialog"
      aria-describedby={descriptionId}
      aria-labelledby={titleId}
      aria-modal="true"
      onCancel={(event) => {
        event.preventDefault();
        onCancel();
      }}
    >
      <h2 id={titleId}>{title}</h2>
      <p id={descriptionId}>{description}</p>
      <div className="info-dialog-actions confirmation-dialog-actions">
        <button ref={cancelButtonRef} type="button" className="confirmation-dialog-cancel" onClick={onCancel}>Cancel</button>
        <button type="button" className="confirmation-dialog-confirm" onClick={onConfirm}>{confirmLabel}</button>
      </div>
    </dialog>,
    document.body
  );
}
