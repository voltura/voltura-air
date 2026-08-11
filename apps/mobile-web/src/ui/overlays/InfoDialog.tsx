import { useId } from "react";
import { CircleAlert } from "lucide-react";
import { ModalDialog } from "./ModalDialog";

interface InfoDialogProps {
  description: string;
  isOpen: boolean;
  onClose: () => void;
  size?: "compact" | "detailed";
  title: string;
  tone?: "default" | "error";
}

export function InfoDialog({ description, isOpen, onClose, size = "compact", title, tone = "default" }: InfoDialogProps) {
  const descriptionId = useId();
  return (
    <ModalDialog
      ariaDescribedBy={descriptionId}
      actionsClassName="info-dialog-actions"
      className={`info-dialog${size === "detailed" ? " info-dialog-detailed" : ""}${tone === "error" ? " info-dialog-error" : ""}`}
      dismissLabel="OK"
      focusDismissAction
      isOpen={isOpen}
      onClose={onClose}
      title={title}
      titleAccessory={tone === "error" ? <CircleAlert className="info-dialog-error-icon" aria-hidden="true" /> : undefined}
    >
      <p id={descriptionId}>{description}</p>
    </ModalDialog>
  );
}
