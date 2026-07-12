import { useId } from "react";
import type { TrackpadSettings } from "../gestures";
import { InfoButton } from "./InfoButton";

const splitModeDescription = "Shows the keyboard and trackpad side by side. It is intended mainly for landscape phones and tablets.";

type SplitModeSettingsProps = {
  settings: TrackpadSettings;
  updateSetting: <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => void;
};

export function SplitModeSettings({ settings, updateSetting }: SplitModeSettingsProps) {
  const splitModeId = useId();

  return (
    <>
      <div className="toggle-row">
        <span className="setting-label-with-info">
          <label htmlFor={splitModeId}>Enable split mode</label>
          <InfoButton title="Split mode" description={splitModeDescription} />
        </span>
        <input id={splitModeId} type="checkbox" checked={settings.enableSplitMode} onChange={(event) => updateSetting("enableSplitMode", event.target.checked)} />
      </div>

      <div className="setting-group">
        <span>Trackpad placement in split mode</span>
        <div className="segmented-control" aria-label="Trackpad placement in split mode">
          <button type="button" className={settings.splitTrackpadPlacement === "left" ? "active" : ""} onClick={() => updateSetting("splitTrackpadPlacement", "left")}>
            Left
          </button>
          <button type="button" className={settings.splitTrackpadPlacement === "right" ? "active" : ""} onClick={() => updateSetting("splitTrackpadPlacement", "right")}>
            Right
          </button>
        </div>
      </div>

      <label className="toggle-row">
        <span>Show mode buttons in split mode</span>
        <input type="checkbox" checked={settings.splitShowModeButtons} onChange={(event) => updateSetting("splitShowModeButtons", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Show status row in split mode</span>
        <input type="checkbox" checked={settings.splitShowStatusRow} onChange={(event) => updateSetting("splitShowStatusRow", event.target.checked)} />
      </label>
    </>
  );
}
