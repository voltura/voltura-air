import { useRef } from "react";
import { ModalDialog } from "../../ui/overlays/ModalDialog";

interface InputRecoveryNoticeProps {
  dismissed: boolean;
  onDismiss: () => void;
  onOpen: () => void;
  onShowDesktop: () => void;
}

export function InputRecoveryNotice({
  dismissed,
  onDismiss,
  onOpen,
  onShowDesktop,
}: InputRecoveryNoticeProps) {
  const showDesktopRef = useRef<HTMLButtonElement>(null);

  return (
    <>
      {dismissed && (
        <button
          type="button"
          className="input-recovery-toast"
          onClick={onOpen}
          aria-label="PC input paused. Open recovery options."
        >
          <strong>PC input paused</strong>
          <span>Show options</span>
        </button>
      )}
      <ModalDialog
        className="input-recovery-dialog"
        title="Administrator app active"
        isOpen={!dismissed}
        onClose={onDismiss}
        dismissLabel="Continue"
        initialFocusRef={showDesktopRef}
        actionsClassName="input-recovery-dialog-actions"
        actions={
          <>
            <button ref={showDesktopRef} type="button" className="primary" onClick={onShowDesktop}>
              Show desktop
            </button>
            <button type="button" onClick={onDismiss}>
              Continue
            </button>
          </>
        }
      >
        <p>Pointer control is unavailable. Other controls remain available.</p>
      </ModalDialog>
    </>
  );
}
