import { Fragment, useEffect, useRef, type Ref } from "react";
import { remoteModeIds, type RemoteModeId } from "../foundation/settings/remoteSettings";
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
        <button
          key={id}
          aria-label={ariaLabel}
          aria-current={tab === id ? "page" : undefined}
          className={tab === id ? "active" : ""}
          onClick={() => {
            onSelect(id);
          }}
        >
          <Icon aria-hidden="true" />
          <span>{label}</span>
        </button>
      ))}
    </nav>
  );
}

interface ModeSelectorProps {
  modeTabs: ModeDefinition[];
  remoteMode: RemoteModeId;
  tab: AppTab;
  onClose: () => void;
  onSelectRemoteMode: (mode: RemoteModeId) => void;
  onSelect: (tab: MainAppTab) => void;
}

const remoteModeLabels: Record<RemoteModeId, string> = {
  standard: "Standard",
  youtube: "YouTube",
  kodi: "Kodi",
};

export function ModeSelector({
  modeTabs,
  onClose,
  onSelect,
  onSelectRemoteMode,
  remoteMode,
  tab,
}: ModeSelectorProps) {
  const menuRef = useRef<HTMLDivElement>(null);
  const invokingRef = useRef<HTMLElement | null>(null);
  useEffect(() => {
    invokingRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    menuRef.current?.querySelector<HTMLElement>('[aria-checked="true"]')?.focus();
  }, []);
  return (
    <>
      <button
        className="mode-selector-scrim"
        type="button"
        aria-label="Close mode selector"
        onClick={onClose}
      />
      <div
        ref={menuRef}
        className="mode-selector-popover"
        role="menu"
        tabIndex={-1}
        aria-label="Change mode"
        onKeyDown={(event) => {
          if (event.key === "Tab") {
            event.stopPropagation();
            onClose();
            return;
          }
          if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            onClose();
            invokingRef.current?.focus();
            return;
          }
          const direction = event.key === "ArrowDown" ? 1 : event.key === "ArrowUp" ? -1 : 0;
          if (!direction && event.key !== "Home" && event.key !== "End") {
            return;
          }
          const items = Array.from(
            event.currentTarget.querySelectorAll<HTMLButtonElement>('button[role="menuitemradio"]'),
          );
          if (!items.length) {
            return;
          }
          event.preventDefault();
          event.stopPropagation();
          const index = items.indexOf(document.activeElement as HTMLButtonElement);
          items[
            event.key === "Home"
              ? 0
              : event.key === "End"
                ? items.length - 1
                : (index + direction + items.length) % items.length
          ]?.focus();
        }}
      >
        {modeTabs.map(({ id, label, ariaLabel, Icon }) => (
          <Fragment key={id}>
            <button
              role="menuitemradio"
              aria-checked={tab === id}
              aria-label={ariaLabel}
              className={tab === id ? "active" : ""}
              onClick={() => {
                onSelect(id);
              }}
            >
              <Icon aria-hidden="true" />
              <span>{label}</span>
            </button>
            {id === "remote" && (
              <div className="mode-selector-remote-modes" role="group" aria-label="Remote mode">
                {remoteModeIds.map((mode) => (
                  <button
                    key={mode}
                    role="menuitemradio"
                    aria-checked={remoteMode === mode}
                    aria-label={`${remoteModeLabels[mode]} remote`}
                    className={remoteMode === mode ? "active" : ""}
                    onClick={() => {
                      onSelectRemoteMode(mode);
                    }}
                  >
                    {remoteModeLabels[mode]}
                  </button>
                ))}
              </div>
            )}
          </Fragment>
        ))}
      </div>
    </>
  );
}

interface CompactModeSelectorButtonProps {
  activeMode: ModeDefinition;
  buttonRef?: Ref<HTMLButtonElement> | undefined;
  isOpen: boolean;
  onToggle: () => void;
}

export function CompactModeSelectorButton({
  activeMode,
  buttonRef,
  isOpen,
  onToggle,
}: CompactModeSelectorButtonProps) {
  const ActiveModeIcon = activeMode.Icon;
  return (
    <button
      ref={buttonRef}
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
