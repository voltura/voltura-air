import { InfoDialog } from "./InfoDialog";

interface ErrorDialogProps {
  code?: string | undefined;
  isOpen: boolean;
  message: string;
  onClose: () => void;
  title: string;
}

export function ErrorDialog({ code, isOpen, message, onClose, title }: ErrorDialogProps) {
  return (
    <InfoDialog
      description={code ? `${message} Diagnostic code: ${code}` : message}
      isOpen={isOpen}
      onClose={onClose}
      size="detailed"
      title={title}
      tone="error"
    />
  );
}
