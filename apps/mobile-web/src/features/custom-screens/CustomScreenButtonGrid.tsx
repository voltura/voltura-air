import { useRef, useState, type PointerEvent } from "react";
import {
  AppWindow, ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Clipboard, Command,
  CornerDownLeft, Copy, Keyboard, Maximize, Minimize, Monitor, Pause, Play,
  MousePointer2, RefreshCw, Search, SkipBack, SkipForward, SquareX, Volume1, Volume2, VolumeX
} from "lucide-react";
import type {
  CustomScreenButtonDefinition,
  CustomScreenLayoutOverride,
  CustomScreenSectionDefinition
} from "../../foundation/protocol/messages";
import { ConfirmationDialog } from "../../ui/overlays/ConfirmationDialog";
import { ModalDialog } from "../../ui/overlays/ModalDialog";
import { HoldToConfirmButton } from "../../ui/overlays/HoldToConfirmButton";

const icons = {
  "app-window": AppWindow,
  "arrow-down": ArrowDown,
  "arrow-left": ArrowLeft,
  "arrow-right": ArrowRight,
  "arrow-up": ArrowUp,
  clipboard: Clipboard,
  command: Command,
  "corner-down-left": CornerDownLeft,
  copy: Copy,
  keyboard: Keyboard,
  maximize: Maximize,
  minimize: Minimize,
  monitor: Monitor,
  "mouse-pointer-2": MousePointer2,
  pause: Pause,
  play: Play,
  refresh: RefreshCw,
  search: Search,
  "skip-back": SkipBack,
  "skip-forward": SkipForward,
  "square-x": SquareX,
  "volume-1": Volume1,
  "volume-2": Volume2,
  "volume-x": VolumeX
} as const;

interface CustomScreenButtonGridProps {
  collapsible: boolean;
  contentId: string;
  invoke: (button: CustomScreenButtonDefinition, enabled?: boolean) => void;
  laserPointerActive: boolean;
  laserPointerColor: "red" | "green" | "blue" | null;
  laserPointerDefaultColor: "red" | "green" | "blue";
  laserPointerPending: boolean;
  onPointerDown: (
    event: PointerEvent<HTMLButtonElement>,
    button: CustomScreenButtonDefinition
  ) => void;
  onLostPointerCapture: () => void;
  onPointerCancel: () => void;
  onPointerUp: () => void;
  orientation: "portrait" | "landscape";
  orientationLayoutsEnabled: boolean;
  pendingButtonIds: ReadonlySet<string>;
  section: CustomScreenSectionDefinition;
}

export function CustomScreenButtonGrid({
  collapsible,
  contentId,
  invoke,
  laserPointerActive,
  laserPointerColor,
  laserPointerDefaultColor,
  laserPointerPending,
  onLostPointerCapture,
  onPointerCancel,
  onPointerDown,
  onPointerUp,
  orientation,
  orientationLayoutsEnabled,
  pendingButtonIds,
  section
}: CustomScreenButtonGridProps) {
  const [confirmation, setConfirmation] = useState<CustomScreenButtonDefinition | null>(null);
  const holdButtonRef = useRef<HTMLButtonElement | null>(null);
  const buttons = arrangeButtons(
    section.buttons,
    orientationLayoutsEnabled,
    orientation);
  const rows = arrangeButtonRows(buttons, section.rowLimit);
  return (
    <div
      className={`custom-screen-buttons${collapsible ? " custom-screen-collapsible-content" : ""}`}
      data-row-limit={section.rowLimit}
      id={collapsible ? contentId : undefined}
    >
      {rows.map((row, rowIndex) => (
        <div
          className="custom-screen-button-row"
          data-button-alignment={section.buttonAlignment}
          data-row={rowIndex + 1}
          key={`button-row-${rowIndex}`}
        >
          {row.map(({ button, layout }) => {
            const Icon = icons[button.icon as keyof typeof icons] ?? Command;
            const size = layout?.size ?? button.size;
            const effectiveLaserColor = button.laserPointerColor === "default"
              ? laserPointerDefaultColor
              : button.laserPointerColor;
            const laserPressed = effectiveLaserColor !== undefined &&
              laserPointerActive && laserPointerColor === effectiveLaserColor;
            const isLaserPointer = button.laserPointerColor !== null &&
              button.laserPointerColor !== undefined;
            const laserDisabled = isLaserPointer &&
              laserPointerPending && !laserPressed;
            return (
              <button
                aria-label={button.name}
                aria-pressed={isLaserPointer ? laserPressed : undefined}
                className={`custom-screen-button size-${size}${laserPressed ? " active" : ""}`}
                data-custom-screen-button-id={button.id}
                disabled={!button.enabled || laserDisabled}
                key={button.id}
                onClick={() => {
                  if (button.confirmation) {
                    setConfirmation(button);
                  } else {
                    invoke(button, isLaserPointer && laserPressed ? false : undefined);
                  }
                }}
                onLostPointerCapture={onLostPointerCapture}
                onPointerCancel={onPointerCancel}
                onPointerDown={(event) => { onPointerDown(event, button); }}
                onPointerUp={onPointerUp}
                title={button.enabled
                  ? button.name
                  : button.unavailableReason ?? "Unavailable"}
                type="button"
              >
                {button.presentation !== "label" && <Icon aria-hidden="true" />}
                {button.presentation !== "icon" && <span>{button.label}</span>}
                {(pendingButtonIds.has(button.id) ||
                  isLaserPointer && laserPointerPending) &&
                  <span className="custom-screen-pending" aria-hidden="true" />}
              </button>
            );
          })}
        </div>
      ))}
      <ConfirmationDialog
        confirmLabel={confirmation?.name ?? "Continue"}
        description={confirmation?.confirmationMessage ?? "Continue with this system action?"}
        isOpen={confirmation?.confirmation === "confirm"}
        onCancel={() => { setConfirmation(null); }}
        onConfirm={() => {
          if (confirmation) {
            invoke(confirmation);
          }
          setConfirmation(null);
        }}
        title={confirmation?.name ?? "Confirm system action"}
      />
      <ModalDialog
        actions={confirmation?.confirmation === "hold" ? (
          <>
            <HoldToConfirmButton
              disabled={false}
              label={confirmation.name}
              ref={holdButtonRef}
              onConfirm={() => {
                invoke(confirmation);
                setConfirmation(null);
              }}
            />
            <button type="button" onClick={() => { setConfirmation(null); }}>Cancel</button>
          </>
        ) : undefined}
        actionsClassName="hold-confirm-actions"
        className="confirmation-dialog"
        dismissLabel="Cancel"
        isOpen={confirmation?.confirmation === "hold"}
        initialFocusRef={holdButtonRef}
        onClose={() => { setConfirmation(null); }}
        title={confirmation?.name ?? "Confirm system action"}
      >
        <p>{confirmation?.confirmationMessage ?? "Unsaved work may be lost."}</p>
      </ModalDialog>
    </div>
  );
}

interface ArrangedButton {
  button: CustomScreenButtonDefinition;
  layout: CustomScreenLayoutOverride | null | undefined;
}

function arrangeButtons(
  buttons: CustomScreenButtonDefinition[],
  orientationLayoutsEnabled: boolean,
  orientation: "portrait" | "landscape"
): ArrangedButton[] {
  return buttons
    .map((button, index) => ({
      button,
      baseOrder: index,
      layout: orientationLayoutsEnabled
        ? orientation === "portrait" ? button.portrait : button.landscape
        : undefined
    }))
    .filter(({ layout }) => layout?.visible !== false)
    .sort((left, right) =>
      (left.layout?.order ?? left.baseOrder) -
      (right.layout?.order ?? right.baseOrder));
}

function arrangeButtonRows(
  buttons: ArrangedButton[],
  rowLimit: number
): ArrangedButton[][] {
  if (rowLimit <= 0) {
    return [buttons];
  }

  const rows = Array.from(
    { length: rowLimit },
    () => [] as ArrangedButton[]);
  let automaticIndex = 0;
  for (const item of buttons) {
    const configuredRow = item.layout?.row ?? item.button.row ?? 0;
    const rowIndex = configuredRow > 0
      ? Math.min(configuredRow, rowLimit) - 1
      : automaticIndex++ % rowLimit;
    rows[rowIndex]!.push(item);
  }
  return rows;
}
