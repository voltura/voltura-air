import { useMemo, useState, type CSSProperties } from "react";
import { ModalDialog } from "../../ui/overlays/ModalDialog";

interface AccentColorPickerDialogProps {
  initialColor: string;
  isOpen: boolean;
  onApply: (color: string) => void;
  onCancel: () => void;
}

interface Hsv {
  hue: number;
  saturation: number;
  value: number;
}

const canonicalColorPattern = /^#[0-9A-F]{6}$/;

export function AccentColorPickerDialog({
  initialColor,
  isOpen,
  onApply,
  onCancel,
}: AccentColorPickerDialogProps) {
  const [draft, setDraft] = useState(initialColor);
  const [hsv, setHsv] = useState(() => hexToHsv(initialColor));
  const normalizedDraft = normalizeInput(draft);
  const valid = canonicalColorPattern.test(normalizedDraft);
  const hueColor = useMemo(() => hsvToHex({ hue: hsv.hue, saturation: 1, value: 1 }), [hsv.hue]);

  const updateFromHsv = (next: Hsv) => {
    setHsv(next);
    setDraft(hsvToHex(next));
  };

  const pickSurface = (element: HTMLElement, clientX: number, clientY: number) => {
    const bounds = element.getBoundingClientRect();
    updateFromHsv({
      hue: hsv.hue,
      saturation: clamp((clientX - bounds.left) / bounds.width, 0, 1),
      value: clamp(1 - (clientY - bounds.top) / bounds.height, 0, 1),
    });
  };

  return (
    <ModalDialog
      actionsClassName="accent-picker-actions"
      className="accent-picker-dialog"
      dismissLabel="Cancel"
      isOpen={isOpen}
      onClose={onCancel}
      onSubmit={() => {
        if (!valid) {
          return false;
        }
        onApply(normalizedDraft);
        return true;
      }}
      submitClassName="accent-picker-apply"
      submitLabel="Apply"
      title="Custom color"
    >
      <div className="accent-picker-input-row">
        <span
          className="accent-picker-swatch"
          style={{ background: valid ? normalizedDraft : "transparent" }}
        />
        <label>
          <span className="visually-hidden">Hex color</span>
          <input
            className="text-input"
            aria-invalid={!valid}
            autoCapitalize="characters"
            inputMode="text"
            maxLength={7}
            spellCheck={false}
            value={draft}
            onChange={(event) => {
              const next = event.target.value.toUpperCase();
              setDraft(next);
              const normalized = normalizeInput(next);
              if (canonicalColorPattern.test(normalized)) {
                setHsv(hexToHsv(normalized));
              }
            }}
          />
        </label>
      </div>
      <div
        className="accent-picker-surface"
        role="slider"
        aria-label="Saturation and brightness"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(hsv.value * 100)}
        aria-valuetext={`Saturation ${Math.round(hsv.saturation * 100)}%, brightness ${Math.round(hsv.value * 100)}%`}
        tabIndex={0}
        style={{ "--picker-hue": hueColor } as CSSProperties}
        onKeyDown={(event) => {
          const step = event.shiftKey ? 0.1 : 0.02;
          if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            event.preventDefault();
            updateFromHsv({
              ...hsv,
              saturation: clamp(hsv.saturation + (event.key === "ArrowRight" ? step : -step), 0, 1),
            });
          } else if (event.key === "ArrowUp" || event.key === "ArrowDown") {
            event.preventDefault();
            updateFromHsv({
              ...hsv,
              value: clamp(hsv.value + (event.key === "ArrowUp" ? step : -step), 0, 1),
            });
          }
        }}
        onPointerDown={(event) => {
          event.currentTarget.setPointerCapture(event.pointerId);
          pickSurface(event.currentTarget, event.clientX, event.clientY);
        }}
        onPointerMove={(event) => {
          if (event.currentTarget.hasPointerCapture(event.pointerId)) {
            pickSurface(event.currentTarget, event.clientX, event.clientY);
          }
        }}
        onPointerUp={(event) => event.currentTarget.releasePointerCapture(event.pointerId)}
        onPointerCancel={(event) => {
          if (event.currentTarget.hasPointerCapture(event.pointerId)) {
            event.currentTarget.releasePointerCapture(event.pointerId);
          }
        }}
      >
        <span
          className="accent-picker-marker"
          style={{ left: `${hsv.saturation * 100}%`, top: `${(1 - hsv.value) * 100}%` }}
        />
      </div>
      <label className="accent-picker-hue-row">
        <span>Hue</span>
        <input
          type="range"
          min="0"
          max="360"
          value={Math.round(hsv.hue)}
          onChange={(event) => updateFromHsv({ ...hsv, hue: Number(event.target.value) })}
        />
      </label>
    </ModalDialog>
  );
}

function normalizeInput(value: string): string {
  const upper = value.trim().toUpperCase();
  return upper.startsWith("#") ? upper : `#${upper}`;
}

function hexToHsv(value: string): Hsv {
  const red = Number.parseInt(value.slice(1, 3), 16) / 255;
  const green = Number.parseInt(value.slice(3, 5), 16) / 255;
  const blue = Number.parseInt(value.slice(5, 7), 16) / 255;
  const maximum = Math.max(red, green, blue);
  const minimum = Math.min(red, green, blue);
  const delta = maximum - minimum;
  const hue =
    delta === 0
      ? 0
      : maximum === red
        ? 60 * (((green - blue) / delta) % 6)
        : maximum === green
          ? 60 * ((blue - red) / delta + 2)
          : 60 * ((red - green) / delta + 4);
  return {
    hue: hue < 0 ? hue + 360 : hue,
    saturation: maximum === 0 ? 0 : delta / maximum,
    value: maximum,
  };
}

function hsvToHex({ hue, saturation, value }: Hsv): string {
  const chroma = value * saturation;
  const second = chroma * (1 - Math.abs(((hue / 60) % 2) - 1));
  const offset = value - chroma;
  const [red, green, blue] =
    hue < 60
      ? [chroma, second, 0]
      : hue < 120
        ? [second, chroma, 0]
        : hue < 180
          ? [0, chroma, second]
          : hue < 240
            ? [0, second, chroma]
            : hue < 300
              ? [second, 0, chroma]
              : [chroma, 0, second];
  const channel = (component: number) =>
    Math.round((component + offset) * 255)
      .toString(16)
      .padStart(2, "0");
  return `#${channel(red)}${channel(green)}${channel(blue)}`.toUpperCase();
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}
