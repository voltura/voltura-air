import type { MouseEvent, ReactNode } from "react";
import type { SettingsSection } from "./SettingsDrawerTypes";

interface SettingsSectionDetailsProps {
  children: ReactNode;
  isOpen: boolean;
  label: string;
  onToggle: (event: MouseEvent<HTMLElement>, section: SettingsSection) => void;
  section: SettingsSection;
}

export function SettingsSectionDetails({ children, isOpen, label, onToggle, section }: SettingsSectionDetailsProps) {
  return (
    <details className="settings-section" data-settings-section={section} open={isOpen}>
      <summary onClick={(event) => { onToggle(event, section); }}>
        <span>{label}</span>
      </summary>
      <div className="settings-section-body">{children}</div>
    </details>
  );
}
