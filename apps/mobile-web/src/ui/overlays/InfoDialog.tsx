import { useId } from "react";
import { ModalDialog } from "./ModalDialog";

interface InfoDialogProps {
  description: string;
  isOpen: boolean;
  onClose: () => void;
  size?: "compact" | "detailed";
  title: string;
}

export function InfoDialog({ description, isOpen, onClose, size = "compact", title }: InfoDialogProps) {
  const descriptionId = useId();
  return (
    <ModalDialog
      ariaDescribedBy={descriptionId}
      actionsClassName="info-dialog-actions"
      className={`info-dialog${size === "detailed" ? " info-dialog-detailed" : ""}`}
      dismissLabel="OK"
      focusDismissAction
      isOpen={isOpen}
      onClose={onClose}
      title={title}
    >
      <p id={descriptionId}>{description}</p>
    </ModalDialog>
  );
}
