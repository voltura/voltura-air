import { useState } from "react";
import { Power } from "lucide-react";
import type { AwakeCapability, AwakeResultMessage, PowerCapabilities, SystemPowerAction, SystemPowerResultMessage } from "../../protocol";
import { PowerControlSheet } from "./PowerControlSheet";

export type AwakeControlProps = {
  awake?: AwakeCapability | null;
  awakeResult?: AwakeResultMessage | null;
  onAwakeChange?: (enabled: boolean) => void;
  pendingAwakeChange?: boolean | null;
};

type PowerControlEntryProps = AwakeControlProps & {
  capabilities: PowerCapabilities | null;
  onAction: (action: SystemPowerAction) => void;
  onOpen: () => void;
  pendingAction: SystemPowerAction | null;
  result: SystemPowerResultMessage | null;
};

export function PowerControlEntry({ awake = null, awakeResult = null, capabilities, onAction, onAwakeChange = () => {}, onOpen, pendingAction, pendingAwakeChange = null, result }: PowerControlEntryProps) {
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
      {isOpen && <PowerControlSheet awake={awake} awakeResult={awakeResult} capabilities={capabilities} onAction={onAction} onAwakeChange={onAwakeChange} onClose={() => setIsOpen(false)} pendingAction={pendingAction} pendingAwakeChange={pendingAwakeChange} result={result} />}
    </>
  );
}
