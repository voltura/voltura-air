import { ArrowLeft, ArrowRight, CornerDownLeft, Monitor, Plus, RefreshCw, RotateCcw, Search, SquareX } from "lucide-react";
import type { RemoteSettings } from "../../remoteSettings";
import { RemoteButton } from "./RemoteButton";

type RemoteUtilityPanelProps = {
  id: string;
  isOpen: boolean;
  onClose: () => void;
  remoteSettings: RemoteSettings;
  sendSpecial: (key: string, modifiers?: string[]) => void;
};

export function RemoteUtilityPanel({ id, isOpen, onClose, remoteSettings, sendSpecial }: RemoteUtilityPanelProps) {
  return (
    <div id={id} className="remote-section remote-utility-section">
      <div className="remote-section-title">
        <span>Windows</span>
        <small>Fast helper keys for couch use.</small>
      </div>
      <div className="remote-utility-grid" aria-label="Windows helper controls">
        <RemoteButton label="Start or search" onClick={() => sendSpecial("Win")}>
          <Search aria-hidden="true" />
          <span>Start</span>
        </RemoteButton>
        <RemoteButton label="Alt+Tab" onClick={() => sendSpecial("Tab", ["Alt"])}>
          <span>Alt+Tab</span>
        </RemoteButton>
        {remoteSettings.showWindowHelpers && (
          <>
            <RemoteButton label="Show desktop" onClick={() => sendSpecial("D", ["Win"])}>
              <Monitor aria-hidden="true" />
              <span>Desktop</span>
            </RemoteButton>
            <RemoteButton label="Close focused window" onClick={() => sendSpecial("F4", ["Alt"])}>
              <SquareX aria-hidden="true" />
              <span>Close</span>
            </RemoteButton>
            <RemoteButton label="Minimize focused window" onClick={() => sendSpecial("ArrowDown", ["Win"])}>
              <span>Minimize</span>
            </RemoteButton>
          </>
        )}
      </div>
      <div className="remote-section-title remote-helper-section-title">
        <span>Browser</span>
        <small>Tabs and page controls.</small>
      </div>
      <div className="remote-utility-grid" aria-label="Browser helper controls">
        <RemoteButton label="Browser back" onClick={() => sendSpecial("BrowserBack")}>
          <CornerDownLeft aria-hidden="true" />
          <span>Back</span>
        </RemoteButton>
        {remoteSettings.showBrowserHelpers && (
          <>
            <RemoteButton label="New tab" onClick={() => sendSpecial("T", ["Control"])}>
              <Plus aria-hidden="true" />
              <span>New tab</span>
            </RemoteButton>
            <RemoteButton label="Close tab" onClick={() => sendSpecial("W", ["Control"])}>
              <SquareX aria-hidden="true" />
              <span>Close</span>
            </RemoteButton>
            <RemoteButton label="Reopen closed tab" onClick={() => sendSpecial("T", ["Control", "Shift"])}>
              <RotateCcw aria-hidden="true" />
              <span>Reopen</span>
            </RemoteButton>
            <RemoteButton label="Next tab" onClick={() => sendSpecial("Tab", ["Control"])}>
              <ArrowRight aria-hidden="true" />
              <span>Next</span>
            </RemoteButton>
            <RemoteButton label="Previous tab" onClick={() => sendSpecial("Tab", ["Control", "Shift"])}>
              <ArrowLeft aria-hidden="true" />
              <span>Prev tab</span>
            </RemoteButton>
            <RemoteButton label="Reload page" onClick={() => sendSpecial("R", ["Control"])}>
              <RefreshCw aria-hidden="true" />
              <span>Reload</span>
            </RemoteButton>
          </>
        )}
      </div>
      <button type="button" className="remote-fn-button remote-floating-fn" aria-controls={id} aria-expanded={isOpen} onClick={onClose}>
        Main
      </button>
    </div>
  );
}
