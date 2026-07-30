import { ArrowLeft, Play, Presentation, RefreshCw } from "lucide-react";
import type { ReactNode } from "react";
import type {
  AppLaunchActionSummary,
  AppLaunchResultMessage,
  AvailablePowerPointPresentation,
  PowerPointCapability,
  PowerPointLaunchResultMessage
} from "../../../foundation/protocol/messages";

type Selection =
  | { kind: "open"; id: string }
  | { kind: "saved"; id: string }
  | null;

interface PowerPointPresentationChooserProps {
  appLaunchAction?: AppLaunchActionSummary | undefined;
  appLaunchResult?: AppLaunchResultMessage | null | undefined;
  capability: PowerPointCapability;
  launchPending: boolean;
  launchResult?: PowerPointLaunchResultMessage | null | undefined;
  lockedMessage?: string | null | undefined;
  onBack: () => void;
  onLaunchApp?: ((actionId: string) => void) | undefined;
  onLaunchSaved: (presentationId: string) => void;
  onRefresh?: (() => void) | undefined;
  onSelectOpen: (runtimePresentationId: string) => void;
  refreshPending: boolean;
  appLaunchPending: boolean;
  selection: Selection;
  onSelectionChange: (selection: Selection) => void;
}

export function PowerPointPresentationChooser({
  appLaunchAction,
  appLaunchResult,
  appLaunchPending,
  capability,
  launchPending,
  launchResult,
  lockedMessage,
  onBack,
  onLaunchApp,
  onLaunchSaved,
  onRefresh,
  onSelectOpen,
  refreshPending,
  selection,
  onSelectionChange
}: PowerPointPresentationChooserProps) {
  const open = capability.presentations;
  const saved = capability.availablePresentations ?? [];
  const selectedOpen = selection?.kind === "open"
    ? open.find((item) => item.runtimePresentationId === selection.id)
    : undefined;
  const selectedSaved = selection?.kind === "saved"
    ? saved.find((item) => item.presentationId === selection.id)
    : undefined;
  const commitDisabled = launchPending || lockedMessage !== null && lockedMessage !== undefined ||
    (!selectedOpen && !selectedSaved);

  return (
    <section className="presentation-chooser" aria-labelledby="presentation-chooser-title">
      <header className="presentation-chooser-header">
        <button type="button" className="presentation-chooser-back" onClick={onBack}>
          <ArrowLeft aria-hidden="true" /><span>Back</span>
        </button>
        <div>
          <h1 id="presentation-chooser-title">Choose presentation</h1>
          <p>Select an open deck or a saved PowerPoint file on this PC.</p>
        </div>
        <button
          type="button"
          className="presentation-chooser-refresh"
          disabled={refreshPending || launchPending}
          onClick={onRefresh}
        >
          <RefreshCw aria-hidden="true" /><span>{refreshPending ? "Refreshing…" : "Refresh"}</span>
        </button>
      </header>

      <div className="presentation-chooser-lists">
        <ChooserGroup title="Open in PowerPoint" empty="No open presentations.">
          {open.map((presentation) => (
            <ChooserRow
              checked={selection?.kind === "open" && selection.id === presentation.runtimePresentationId}
              detail={presentation.state === "presenting"
                ? `Slide ${presentation.currentSlideIndex ?? "–"} of ${presentation.slideCount} · Presenting`
                : `${presentation.slideCount} slides · Ready`}
              disabled={launchPending ||
                lockedMessage !== null && lockedMessage !== undefined &&
                !(selection?.kind === "open" && selection.id === presentation.runtimePresentationId)}
              id={`open-${presentation.runtimePresentationId}`}
              key={presentation.runtimePresentationId}
              name={presentation.name}
              onSelect={() => { onSelectionChange({ kind: "open", id: presentation.runtimePresentationId }); }}
            />
          ))}
        </ChooserGroup>

        <ChooserGroup title="Available on this PC" empty="No saved PowerPoint files are available.">
          {saved.map((presentation) => (
            <ChooserRow
              checked={selection?.kind === "saved" && selection.id === presentation.presentationId}
              detail={presentation.fileName === presentation.title
                ? "Saved presentation"
                : presentation.fileName}
              disabled={launchPending ||
                lockedMessage !== null && lockedMessage !== undefined &&
                !(selection?.kind === "saved" && selection.id === presentation.presentationId)}
              id={`saved-${presentation.presentationId}`}
              key={presentation.presentationId}
              name={presentation.title}
              onSelect={() => { onSelectionChange({ kind: "saved", id: presentation.presentationId }); }}
            />
          ))}
        </ChooserGroup>
      </div>

      <footer className="presentation-chooser-footer">
        <div className="presentation-chooser-feedback" aria-live="polite">
          {lockedMessage && <p className="presentation-permission-message">{lockedMessage}</p>}
          {launchResult?.succeeded === false && (
            <p className="presentation-permission-message" role="alert">{launchResult.message}</p>
          )}
          {appLaunchResult?.succeeded === false && appLaunchResult.actionId === appLaunchAction?.id && (
            <p className="presentation-permission-message" role="alert">{appLaunchResult.message}</p>
          )}
          {!lockedMessage && !launchResult && capability.state !== "ready" && (
            <p>PowerPoint is not running. Choose a saved deck to start it, or start PowerPoint without opening a file.</p>
          )}
        </div>
        <div className="presentation-chooser-actions">
          {capability.state !== "ready" && appLaunchAction && onLaunchApp && (
            <button
              type="button"
              disabled={appLaunchPending || launchPending}
              onClick={() => { onLaunchApp(appLaunchAction.id); }}
            >
              <Presentation aria-hidden="true" />
              <span>{appLaunchPending ? "Starting…" : "Start PowerPoint"}</span>
            </button>
          )}
          <button
            type="button"
            className="primary"
            disabled={commitDisabled}
            onClick={() => {
              if (selectedOpen) {
                onSelectOpen(selectedOpen.runtimePresentationId);
              } else if (selectedSaved) {
                onLaunchSaved(selectedSaved.presentationId);
              }
            }}
          >
            <Play aria-hidden="true" />
            <span>{launchPending
              ? "Opening…"
              : selectedSaved
                ? "Open and present"
                : "Use presentation"}</span>
          </button>
        </div>
      </footer>
    </section>
  );
}

function ChooserGroup({
  children,
  empty,
  title
}: {
  children: ReactNode;
  empty: string;
  title: string;
}) {
  const count = Array.isArray(children) ? children.length : children ? 1 : 0;
  return (
    <section className="presentation-chooser-group">
      <h2>{title}</h2>
      <div className="presentation-chooser-options">
        {count === 0 ? <p className="presentation-chooser-empty">{empty}</p> : children}
      </div>
    </section>
  );
}

function ChooserRow({
  checked,
  detail,
  disabled,
  id,
  name,
  onSelect
}: {
  checked: boolean;
  detail: string;
  disabled: boolean;
  id: string;
  name: string;
  onSelect: () => void;
}) {
  return (
    <label
      className={`presentation-chooser-row${checked ? " selected" : ""}${disabled ? " disabled" : ""}`}
      htmlFor={id}
    >
      <input
        id={id}
        type="radio"
        name="powerpoint-presentation"
        checked={checked}
        disabled={disabled}
        onChange={() => {
          if (!disabled) {
            onSelect();
          }
        }}
      />
      <Presentation aria-hidden="true" />
      <span>
        <strong title={name}>{name}</strong>
        <small>{detail}</small>
      </span>
    </label>
  );
}

export type PowerPointChooserSelection = Selection;
export type PowerPointSavedPresentation = AvailablePowerPointPresentation;
