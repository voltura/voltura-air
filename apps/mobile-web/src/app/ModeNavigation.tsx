import { ChevronDown } from "lucide-react";
import type { AppTab, MainAppTab, ModeDefinition } from "./appModeTabs";

interface ModeNavigationProps {
  className: string;
  modeTabs: ModeDefinition[];
  tab: AppTab;
  onSelect: (tab: MainAppTab) => void;
}

export function ModeNavigation({ className, modeTabs, onSelect, tab }: ModeNavigationProps) {
  return (
    <nav className={className} aria-label="Mode">
      {modeTabs.map(({ id, label, ariaLabel, Icon }) => (
        <button key={id} aria-label={ariaLabel} aria-current={tab === id ? "page" : undefined} className={tab === id ? "active" : ""} onClick={() => { onSelect(id); }}>
          <Icon aria-hidden="true" />
          <span>{label}</span>
        </button>
      ))}
    </nav>
  );
}

interface ModeSelectorProps {
  modeTabs: ModeDefinition[];
  tab: AppTab;
  onClose: () => void;
  onSelect: (tab: MainAppTab) => void;
}

export function ModeSelector({ modeTabs, onClose, onSelect, tab }: ModeSelectorProps) {
  return (
    <>
      <button className="mode-selector-scrim" type="button" aria-label="Close mode selector" onClick={onClose} />
      <div className="mode-selector-popover" role="menu" aria-label="Change mode">
        {modeTabs.map(({ id, label, ariaLabel, Icon }) => (
          <button key={id} role="menuitemradio" aria-checked={tab === id} aria-label={ariaLabel} className={tab === id ? "active" : ""} onClick={() => { onSelect(id); }}>
            <Icon aria-hidden="true" />
            <span>{label}</span>
          </button>
        ))}
      </div>
    </>
  );
}

interface CompactModeSelectorButtonProps {
  activeMode: ModeDefinition;
  isOpen: boolean;
  onToggle: () => void;
}

export function CompactModeSelectorButton({ activeMode, isOpen, onToggle }: CompactModeSelectorButtonProps) {
  const ActiveModeIcon = activeMode.Icon;
  return (
    <button
      className="compact-mode-button"
      type="button"
      aria-expanded={isOpen}
      aria-haspopup="menu"
      aria-label="Change mode"
      title={`Change mode (${activeMode.label})`}
      onClick={onToggle}
    >
      <ActiveModeIcon aria-hidden="true" />
      <ChevronDown aria-hidden="true" />
    </button>
  );
}
