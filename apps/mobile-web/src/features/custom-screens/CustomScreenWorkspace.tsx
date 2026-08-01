import {
  useEffect,
  useCallback,
  useMemo,
  useRef,
  useState,
  type PointerEvent
} from "react";
import {
  ChevronDown, Maximize2, Minimize2
} from "lucide-react";
import type {
  ClientMessage,
  AudioStateMessage,
  CustomScreenButtonDefinition,
  CustomScreenDefinition
} from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { TrackpadSettings } from "../../foundation/input/gestures";
import { usePointerInput } from "../../foundation/input/usePointerInput";
import { CustomScreenButtonGrid } from "./CustomScreenButtonGrid";
import { CustomScreenNavigationRing } from "./CustomScreenNavigationRing";
import { CustomScreenVolumeSlider } from "./CustomScreenVolumeSlider";
import "./custom-screens.css";

const repeatDelayMs = 400;
const repeatMs = 55;
const noExpansionOverrides: ReadonlyMap<string, boolean> = new Map();

interface CustomScreenWorkspaceProps {
  audioState?: AudioStateMessage | null;
  definition: CustomScreenDefinition | null;
  error?: string | null;
  invoke: (screenId: string, revision: string, buttonId: string) => void;
  onBack: () => void;
  pendingButtonIds: ReadonlySet<string>;
  requestedName: string;
  send: (payload: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
}

export function CustomScreenWorkspace({
  audioState = null,
  definition,
  error,
  invoke,
  onBack,
  pendingButtonIds,
  requestedName,
  send,
  state,
  trackpadSettings
}: CustomScreenWorkspaceProps) {
  const [orientation, setOrientation] = useState<"portrait" | "landscape">(
    window.innerWidth > window.innerHeight ? "landscape" : "portrait");
  const definitionKey = definition ? `${definition.id}:${definition.revision}` : "";
  const [expansionState, setExpansionState] = useState<{
    definitionKey: string;
    values: ReadonlyMap<string, boolean>;
  }>({ definitionKey: "", values: noExpansionOverrides });
  const [fullscreenTrackpadId, setFullscreenTrackpadId] = useState<string | null>(null);
  const expansionOverrides = expansionState.definitionKey === definitionKey
    ? expansionState.values
    : noExpansionOverrides;
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const ignoreClickRef = useRef(false);
  const repeatPointerReleasedRef = useRef(false);
  const pressedPointerButtonsRef = useRef(new Set<"left" | "right">());
  const {
    emit,
    onTouchCancel,
    onTouchEnd,
    onTouchMove,
    onTouchStart
  } = usePointerInput({ send, state, trackpadSettings });
  const emitRef = useRef(emit);
  useEffect(() => {
    emitRef.current = emit;
  }, [emit]);

  const stopRepeat = useCallback(() => {
    window.clearTimeout(repeatTimeoutRef.current ?? undefined);
    window.clearInterval(repeatIntervalRef.current ?? undefined);
    repeatTimeoutRef.current = null;
    repeatIntervalRef.current = null;
  }, []);

  const completeRepeatPress = useCallback(() => {
    repeatPointerReleasedRef.current = true;
    stopRepeat();
  }, [stopRepeat]);

  const cancelRepeatPress = useCallback(() => {
    ignoreClickRef.current = false;
    repeatPointerReleasedRef.current = false;
    stopRepeat();
  }, [stopRepeat]);

  const stopRepeatOnLostCapture = useCallback(() => {
    stopRepeat();
    if (!repeatPointerReleasedRef.current) {
      ignoreClickRef.current = false;
    }
  }, [stopRepeat]);

  const releasePointerButtons = useCallback(() => {
    for (const button of pressedPointerButtonsRef.current) {
      emitRef.current({ type: "pointer.button", button, action: "up" });
    }
    pressedPointerButtonsRef.current.clear();
  }, []);

  useEffect(() => {
    const update = () => {
      setOrientation(window.innerWidth > window.innerHeight ? "landscape" : "portrait");
    };
    const stopWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        cancelRepeatPress();
        releasePointerButtons();
      }
    };
    const releaseOnBlur = () => {
      cancelRepeatPress();
      releasePointerButtons();
    };
    window.addEventListener("resize", update);
    window.addEventListener("blur", releaseOnBlur);
    document.addEventListener("visibilitychange", stopWhenHidden);
    return () => {
      cancelRepeatPress();
      releasePointerButtons();
      window.removeEventListener("resize", update);
      window.removeEventListener("blur", releaseOnBlur);
      document.removeEventListener("visibilitychange", stopWhenHidden);
    };
  }, [cancelRepeatPress, releasePointerButtons]);

  useEffect(() => {
    if (state !== "paired") {
      releasePointerButtons();
      const clearFullscreen = window.setTimeout(() => {
        setFullscreenTrackpadId(null);
      }, 0);
      return () => { window.clearTimeout(clearFullscreen); };
    }
    return undefined;
  }, [releasePointerButtons, state]);

  useEffect(() => {
    if (state === "paired" && definition?.sections.some(section =>
      section.kind === "volume" && section.volumeEnabled)) {
      send({ type: "audio.get" });
    }
  }, [definition, send, state]);

  const sections = useMemo(() => {
    if (!definition) {
      return [];
    }

    return definition.sections
      .map((section, index) => ({
        section,
        baseOrder: index,
        layout: definition.orientationLayoutsEnabled
          ? orientation === "portrait" ? section.portrait : section.landscape
          : undefined
      }))
      .filter(({ layout }) => layout?.visible !== false)
      .sort((left, right) => (left.layout?.order ?? left.baseOrder) - (right.layout?.order ?? right.baseOrder));
  }, [definition, orientation]);

  const sectionRows = useMemo(() => {
    const rows: (typeof sections)[] = [];
    let row: typeof sections = [];
    let usedColumns = 0;
    for (const item of sections) {
      const widthColumns = item.layout?.widthColumns ?? item.section.widthColumns;
      if (row.length > 0 && usedColumns + widthColumns > 12) {
        rows.push(row);
        row = [];
        usedColumns = 0;
      }
      row.push(item);
      usedColumns += widthColumns;
      if (usedColumns >= 12) {
        rows.push(row);
        row = [];
        usedColumns = 0;
      }
    }
    if (row.length > 0) {
      rows.push(row);
    }
    return rows;
  }, [sections]);

  const press = (event: PointerEvent<HTMLButtonElement>, button: CustomScreenButtonDefinition) => {
    if (!definition || event.button !== 0 || !button.repeat) {
      return;
    }
    event.preventDefault();
    ignoreClickRef.current = true;
    repeatPointerReleasedRef.current = false;
    event.currentTarget.setPointerCapture?.(event.pointerId);
    stopRepeat();
    invoke(definition.id, definition.revision, button.id);
    repeatTimeoutRef.current = window.setTimeout(() => {
      invoke(definition.id, definition.revision, button.id);
      repeatIntervalRef.current = window.setInterval(() => {
        invoke(definition.id, definition.revision, button.id);
      }, repeatMs);
    }, repeatDelayMs);
  };

  return (
    <section
      className={`custom-screen-workspace${definition?.showNavigationHeader === false ? " header-hidden" : ""}`}
      aria-label={definition?.name ?? requestedName}
    >
      {definition?.showNavigationHeader !== false && (
        <header className="custom-screen-header">
          <button
            type="button"
            className="custom-screen-back"
            onClick={() => {
              setFullscreenTrackpadId(null);
              onBack();
            }}
          >
            Back
          </button>
          <h1>{definition?.name ?? requestedName}</h1>
        </header>
      )}
      {!definition && !error && <div className="custom-screen-state" role="status">Loading custom screen…</div>}
      {error && <div className="custom-screen-state custom-screen-error" role="alert">{error}</div>}
      {definition && (
        <div className="custom-screen-grid">
          {sectionRows.map((row, rowIndex) => {
            const rowFillWeight = row.reduce((weight, { section }) => {
              const collapsible = section.collapsible;
              const expanded = expansionOverrides.get(section.id)
                ?? section.initiallyExpanded;
              return section.heightMode === "fill" && (!collapsible || expanded)
                ? Math.max(weight, section.fillWeight)
                : weight;
            }, 0);
            return (
              <div
                className={`custom-screen-row${rowFillWeight > 0 ? " height-fill" : ""}`}
                key={`row-${rowIndex}`}
                style={rowFillWeight > 0
                  ? { flexGrow: rowFillWeight, flexBasis: 0 }
                  : undefined}
              >
          {row.map(({ section, layout }) => {
            const widthColumns = layout?.widthColumns ?? section.widthColumns;
            const trackpadEnabled = section.trackpadEnabled;
            const trackpadButtons = section.trackpadButtonSide === "left"
              ? (["right", "left"] as const)
              : (["left", "right"] as const);
            const collapsible = section.collapsible;
            const expanded = expansionOverrides.get(section.id)
              ?? section.initiallyExpanded;
            const fullscreen =
              state === "paired" && fullscreenTrackpadId === section.id;
            const contentId = `custom-screen-section-content-${section.id}`;
            const kindClass = collapsible
              ? "collapsible"
              : section.kind === "navigationRing" || section.kind === "dpad"
                ? "navigation-ring"
                : section.kind;
            return (
              <section
                className={`custom-screen-section height-${section.heightMode} kind-${kindClass}${collapsible ? expanded ? " is-expanded" : " is-collapsed" : ""}`}
                key={section.id}
                style={{
                  gridColumn: `span ${widthColumns}`
                }}
              >
                {collapsible ? (
                  <button
                    aria-controls={contentId}
                    aria-expanded={expanded}
                    className="custom-screen-collapsible-toggle"
                    onClick={() => {
                      setExpansionState((current) => {
                        const next = new Map(
                          current.definitionKey === definitionKey
                            ? current.values
                            : noExpansionOverrides);
                        next.set(section.id, !expanded);
                        return { definitionKey, values: next };
                      });
                    }}
                    type="button"
                  >
                    <h2>{section.name}</h2>
                    <ChevronDown aria-hidden="true" />
                  </button>
                ) : section.showHeader && <h2>{section.name}</h2>}
                {(!collapsible || expanded) && (section.kind === "trackpad" ? (
                  <div
                    aria-disabled={!trackpadEnabled}
                    className={`custom-screen-trackpad-layout buttons-${section.trackpadButtonSide}${fullscreen ? " is-fullscreen" : ""}${collapsible ? " custom-screen-collapsible-content" : ""}`}
                    id={collapsible ? contentId : undefined}
                    title={trackpadEnabled ? section.name : section.trackpadUnavailableReason ?? "Remote input is unavailable."}
                  >
                    <div
                      aria-label={section.name}
                      className="custom-screen-trackpad"
                      role="application"
                      onTouchStart={trackpadEnabled ? onTouchStart : undefined}
                      onTouchMove={trackpadEnabled ? onTouchMove : undefined}
                      onTouchEnd={trackpadEnabled ? onTouchEnd : undefined}
                      onTouchCancel={trackpadEnabled ? onTouchCancel : undefined}
                    >
                      {section.trackpadFullscreenControl && (
                        <button
                          aria-label={fullscreen ? `Restore ${section.name}` : `Expand ${section.name}`}
                          className="custom-screen-trackpad-expand"
                          onClick={(event) => {
                            event.stopPropagation();
                            setFullscreenTrackpadId(fullscreen ? null : section.id);
                          }}
                          onTouchStart={(event) => { event.stopPropagation(); }}
                          onTouchMove={(event) => { event.stopPropagation(); }}
                          onTouchEnd={(event) => { event.stopPropagation(); }}
                          title={fullscreen ? "Restore trackpad" : "Expand trackpad"}
                          type="button"
                        >
                          {fullscreen
                            ? <Minimize2 aria-hidden="true" />
                            : <Maximize2 aria-hidden="true" />}
                        </button>
                      )}
                      <span aria-hidden="true">Trackpad</span>
                      {fullscreen &&
                        (section.trackpadLeftClick || section.trackpadRightClick) && (
                        <div
                          aria-label="Mouse buttons"
                          className="custom-screen-trackpad-fullscreen-buttons"
                        >
                          {trackpadButtons
                            .filter((button) => button === "left"
                              ? section.trackpadLeftClick
                              : section.trackpadRightClick)
                            .map((button) => renderPointerButton(button, trackpadEnabled))}
                        </div>
                      )}
                    </div>
                    {!fullscreen &&
                      (section.trackpadLeftClick || section.trackpadRightClick) && (
                      <div className="custom-screen-trackpad-buttons">
                        {trackpadButtons
                          .filter((button) => button === "left"
                            ? section.trackpadLeftClick
                            : section.trackpadRightClick)
                          .map((button) => renderPointerButton(button, trackpadEnabled))}
                      </div>
                    )}
                  </div>
                ) : section.kind === "navigationRing" || section.kind === "dpad" ? (
                  <CustomScreenNavigationRing
                    enabled={trackpadEnabled && state === "paired"}
                    name={section.name}
                    reason={section.trackpadUnavailableReason}
                    onCenterKey={() => {
                      emit({ type: "pointer.button", button: "left", action: "click" });
                    }}
                    onTouchCancel={onTouchCancel}
                    onTouchEnd={onTouchEnd}
                    onTouchMove={onTouchMove}
                    onTouchStart={onTouchStart}
                    sendSpecial={(key) => {
                      emit({ type: "keyboard.special", key });
                    }}
                  />
                ) : section.kind === "volume" ? (
                  <CustomScreenVolumeSlider
                    audioState={audioState}
                    enabled={section.volumeEnabled}
                    name={section.name}
                    reason={section.volumeUnavailableReason}
                    send={send}
                    state={state}
                  />
                ) : (
                  <CustomScreenButtonGrid
                    collapsible={collapsible}
                    contentId={contentId}
                    invoke={(button) => {
                      if (ignoreClickRef.current) {
                        ignoreClickRef.current = false;
                        repeatPointerReleasedRef.current = false;
                        return;
                      }
                      invoke(definition.id, definition.revision, button.id);
                    }}
                    onPointerDown={press}
                    onPointerCancel={cancelRepeatPress}
                    onPointerUp={completeRepeatPress}
                    onLostPointerCapture={stopRepeatOnLostCapture}
                    orientation={orientation}
                    orientationLayoutsEnabled={definition.orientationLayoutsEnabled}
                    pendingButtonIds={pendingButtonIds}
                    section={section}
                  />
                ))}
              </section>
            );
          })}
              </div>
            );
          })}
        </div>
      )}
    </section>
  );

  function renderPointerButton(button: "left" | "right", enabled: boolean) {
    const label = button === "left" ? "Left click" : "Right click";
    return (
      <button
        aria-label={label}
        className="custom-screen-trackpad-button"
        disabled={!enabled}
        key={button}
        type="button"
        onPointerDown={(event) => {
          if (event.button !== 0 || pressedPointerButtonsRef.current.has(button)) {
            return;
          }
          event.preventDefault();
          event.currentTarget.setPointerCapture?.(event.pointerId);
          pressedPointerButtonsRef.current.add(button);
          emit({ type: "pointer.button", button, action: "down" });
        }}
        onPointerUp={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
          }
        }}
        onPointerCancel={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
          }
        }}
        onLostPointerCapture={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
          }
        }}
        onClick={(event) => {
          if (event.detail === 0) {
            emit({ type: "pointer.button", button, action: "click" });
          }
        }}
      >
        {label}
      </button>
    );
  }
}
