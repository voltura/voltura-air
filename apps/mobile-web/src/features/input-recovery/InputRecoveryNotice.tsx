interface InputRecoveryNoticeProps {
  dismissed: boolean;
  onDismiss: () => void;
  onOpen: () => void;
  onShowDesktop: () => void;
}

export function InputRecoveryNotice({ dismissed, onDismiss, onOpen, onShowDesktop }: InputRecoveryNoticeProps) {
  if (dismissed) {
    return (
      <button type="button" className="input-recovery-toast" onClick={onOpen} aria-label="PC input paused. Open recovery options.">
        <strong>PC input paused</strong>
        <span>Show options</span>
      </button>
    );
  }

  return (
    <section className="input-recovery-dialog" role="dialog" aria-labelledby="input-recovery-dialog-title">
      <h2 id="input-recovery-dialog-title">Administrator app active</h2>
      <p>Pointer control is unavailable. Other controls remain available.</p>
      <div className="input-recovery-dialog-actions">
        <button type="button" className="primary" onClick={onShowDesktop}>Show desktop</button>
        <button type="button" onClick={onDismiss}>Continue</button>
      </div>
    </section>
  );
}
