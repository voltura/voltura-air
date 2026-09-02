import type { ScreenViewSoundQuality } from "../../foundation/protocol/messages";

interface ScreenViewingSettingsSectionProps {
  soundQuality: ScreenViewSoundQuality;
  soundQualityOverridden: boolean;
  setHostScreenSoundQuality: (soundQuality: ScreenViewSoundQuality | null) => void;
}

export function ScreenViewingSettingsSection({
  soundQuality,
  soundQualityOverridden,
  setHostScreenSoundQuality,
}: ScreenViewingSettingsSectionProps) {
  const selected = soundQualityOverridden ? soundQuality : "default";
  return (
    <label className="setting-group">
      <span>Sound quality</span>
      <select
        aria-label="Sound quality"
        className="text-input"
        value={selected}
        onChange={(event) => {
          const value = event.target.value;
          setHostScreenSoundQuality(
            value === "high" || value === "standard" || value === "low" ? value : null,
          );
        }}
      >
        <option value="default">Use PC default</option>
        <option value="high">High</option>
        <option value="standard">Standard</option>
        <option value="low">Low</option>
      </select>
      <small className="settings-help">{description(soundQuality, soundQualityOverridden)}</small>
    </label>
  );
}

function description(soundQuality: ScreenViewSoundQuality, overridden: boolean): string {
  const prefix = overridden
    ? "Override active. "
    : `Using PC default: ${displayName(soundQuality)}. `;
  switch (soundQuality) {
    case "high":
      return `${prefix}Best detail for music and movies. Stereo.`;
    case "standard":
      return `${prefix}Good stereo sound with lower network use.`;
    case "low":
      return `${prefix}Reduced-detail mono sound with the lowest network use.`;
  }
}

function displayName(soundQuality: ScreenViewSoundQuality): string {
  return soundQuality.charAt(0).toUpperCase() + soundQuality.slice(1);
}
