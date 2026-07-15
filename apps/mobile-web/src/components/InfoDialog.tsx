import { useEffect, useId, useRef } from "react";

type InfoDialogProps = {
  description: string;
  isOpen: boolean;
  onClose: () => void;
  size?: "compact" | "detailed";
  title: string;
};

export function InfoDialog({ description, isOpen, onClose, size = "compact", title }: InfoDialogProps) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog || dialog.open) {
      return;
    }

    if (typeof dialog.showModal === "function") {
      dialog.showModal();
    } else {
      dialog.setAttribute("open", "");
    }
  }, [isOpen]);

  const closeDialog = () => {
    const dialog = dialogRef.current;
    if (!dialog) {
      onClose();
      return;
    }

    if (typeof dialog.close === "function") {
      dialog.close();
      return;
    }

    dialog.removeAttribute("open");
    onClose();
  };

  if (!isOpen) {
    return null;
  }

  return (
    <dialog
      ref={dialogRef}
      className={`info-dialog${size === "detailed" ? " info-dialog-detailed" : ""}`}
      aria-describedby={descriptionId}
      aria-labelledby={titleId}
      aria-modal="true"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          closeDialog();
        }
      }}
      onClose={onClose}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          closeDialog();
        }
      }}
    >
      <h2 id={titleId}>{title}</h2>
      <p id={descriptionId}>{description}</p>
      <div className="info-dialog-actions">
        <button type="button" autoFocus onClick={closeDialog}>OK</button>
      </div>
    </dialog>
  );
}
