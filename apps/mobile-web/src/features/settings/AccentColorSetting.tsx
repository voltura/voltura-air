import { lazy, Suspense, useState } from "react";

const AccentColorPickerDialog = lazy(() =>
  import("./AccentColorPickerDialog").then((module) => ({
    default: module.AccentColorPickerDialog,
  })),
);

interface AccentColorSettingProps {
  accentColor: string | null | undefined;
  accentColorOverridden: boolean;
  setHostAccentColor: ((accentColor: string | null) => void) | undefined;
}

export function AccentColorSetting({
  accentColor,
  accentColorOverridden,
  setHostAccentColor,
}: AccentColorSettingProps) {
  const [isPickerOpen, setIsPickerOpen] = useState(false);
  const pickerColor = accentColor ?? "#12A894";

  return (
    <div className="accent-setting">
      <span>Accent color</span>
      <div className="accent-setting-actions">
        <button
          className="accent-setting-button"
          type="button"
          onClick={() => setIsPickerOpen(true)}
        >
          <span
            className="accent-setting-swatch"
            style={{ background: accentColor ?? "var(--accent)" }}
            aria-hidden="true"
          />
          <span>{accentColor ?? "Voltura default"}</span>
        </button>
        {accentColorOverridden && (
          <button type="button" onClick={() => setHostAccentColor?.(null)}>
            Use PC default
          </button>
        )}
      </div>
      {isPickerOpen && (
        <Suspense fallback={null}>
          <AccentColorPickerDialog
            initialColor={pickerColor}
            isOpen
            onApply={(color) => {
              setHostAccentColor?.(color);
              setIsPickerOpen(false);
            }}
            onCancel={() => setIsPickerOpen(false)}
          />
        </Suspense>
      )}
    </div>
  );
}
