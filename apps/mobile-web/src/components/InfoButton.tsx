import { useEffect, useId, useRef, useState } from "react";
import { Info } from "lucide-react";

type InfoButtonProps = {
  description: string;
  title: string;
};

export function InfoButton({ description, title }: InfoButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement | null>(null);
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

  const finishClosing = () => {
    setIsOpen(false);
    buttonRef.current?.focus();
  };

  const closeDialog = () => {
    const dialog = dialogRef.current;
    if (!dialog) {
      finishClosing();
      return;
    }

    if (typeof dialog.close === "function") {
      dialog.close();
      return;
    }

    dialog.removeAttribute("open");
    finishClosing();
  };

  return (
    <>
      <button ref={buttonRef} className="info-button" type="button" aria-label={`About ${title}`} onClick={() => setIsOpen(true)}>
        <Info aria-hidden="true" />
      </button>
      {isOpen && (
        <dialog
          ref={dialogRef}
          className="info-dialog"
          aria-describedby={descriptionId}
          aria-labelledby={titleId}
          aria-modal="true"
          onClick={(event) => {
            if (event.target === event.currentTarget) {
              closeDialog();
            }
          }}
          onClose={finishClosing}
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
            <button type="button" autoFocus onClick={closeDialog}>
              OK
            </button>
          </div>
        </dialog>
      )}
    </>
  );
}
