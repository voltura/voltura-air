import { useState } from "react";
import { Power } from "lucide-react";
import type { PowerCapabilities, SystemPowerAction, SystemPowerResultMessage } from "../../protocol";
import { PowerControlSheet } from "./PowerControlSheet";

type PowerControlEntryProps = {
  capabilities: PowerCapabilities | null;
  onAction: (action: SystemPowerAction) => void;
  onOpen: () => void;
  pendingAction: SystemPowerAction | null;
  result: SystemPowerResultMessage | null;
};

export function PowerControlEntry({ capabilities, onAction, onOpen, pendingAction, result }: PowerControlEntryProps) {
  const [isOpen, setIsOpen] = useState(false);
  if (!capabilities) {
    return null;
  }

  return (
    <>
      <button
        type="button"
        className="remote-power-button"
        aria-haspopup="dialog"
        aria-expanded={isOpen}
        onClick={() => {
          onOpen();
          setIsOpen(true);
        }}
      >
        <Power aria-hidden="true" />
        <span>Power</span>
      </button>
      {isOpen && <PowerControlSheet capabilities={capabilities} onAction={onAction} onClose={() => setIsOpen(false)} pendingAction={pendingAction} result={result} />}
    </>
  );
}
