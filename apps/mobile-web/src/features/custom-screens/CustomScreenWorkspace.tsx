import { useEffect, useCallback, useMemo, useRef, useState, type PointerEvent } from "react";
import { ChevronDown, Maximize2, Minimize2 } from "lucide-react";
import type {
  ClientMessage,
  AudioStateMessage,
  CustomScreenButtonDefinition,
  CustomScreenDefinition,
  PresentationCapability,
} from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { TrackpadSettings } from "../../foundation/input/gestures";
import type { GyroActivationRequest, GyroAvailability } from "../../foundation/input/gyroMouse";
import { useGyroMouse } from "../../foundation/input/useGyroMouse";
import { usePointerInput } from "../../foundation/input/usePointerInput";
import { CustomScreenButtonGrid } from "./CustomScreenButtonGrid";
import { CustomScreenNavigationRing } from "./CustomScreenNavigationRing";
import { CustomScreenVolumeSlider } from "./CustomScreenVolumeSlider";
import "./custom-screens.css";

const repeatDelayMs = 400;
const repeatMs = 55;
const gyroTapMaximumDurationMs = 300;
const gyroHoldActivationDelayMs = gyroTapMaximumDurationMs + 1;
const noExpansionOverrides: ReadonlyMap<string, boolean> = new Map();
const ignoreGyroSelection = () => undefined;

interface CustomScreenWorkspaceProps {
  audioState?: AudioStateMessage | null;
  connectionEpoch?: number;
  definition: CustomScreenDefinition | null;
  error?: string | null;
  invoke: (
    screenId: string,
    revision: string,
    buttonId: string,
    enabled?: boolean,
    suppressResult?: boolean,
  ) => void;
  gyroActivationRequest?: GyroActivationRequest | null;
  onGyroSelectedChange?: (selected: boolean) => void;
  onBack: () => void;
  pendingButtonIds: ReadonlySet<string>;
  requestedName: string;
  presentationCapability?: PresentationCapability | null | undefined;
  send: (payload: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
}

export function CustomScreenWorkspace({
  audioState = null,
  connectionEpoch = 0,
  definition,
  error,
  gyroActivationRequest = null,
  invoke,
  onGyroSelectedChange = ignoreGyroSelection,
  onBack,
  pendingButtonIds,
  presentationCapability = null,
  requestedName,
  send,
  state,
  trackpadSettings,
}: CustomScreenWorkspaceProps) {
  const [orientation, setOrientation] = useState<"portrait" | "landscape">(
    window.innerWidth > window.innerHeight ? "landscape" : "portrait",
  );
  const definitionKey = definition ? `${definition.id}:${definition.revision}` : "";
  const laserPointerActive = presentationCapability?.laserPointerActive === true;
  const laserPointerColor =
    presentationCapability?.laserPointerColor ??
    (laserPointerActive ? (presentationCapability?.laserPointerDefaultColor ?? "red") : null);
  const laserPointerDefaultColor = presentationCapability?.laserPointerDefaultColor ?? "red";
  const laserPointerPending =
    definition?.sections.some((section) =>
      section.buttons.some(
        (button) =>
          button.laserPointerColor !== null &&
          button.laserPointerColor !== undefined &&
          pendingButtonIds.has(button.id),
      ),
    ) === true;
  const [expansionState, setExpansionState] = useState<{
    definitionKey: string;
    values: ReadonlyMap<string, boolean>;
  }>({ definitionKey: "", values: noExpansionOverrides });
  const [fullscreenTrackpadId, setFullscreenTrackpadId] = useState<string | null>(null);
  const expansionOverrides =
    expansionState.definitionKey === definitionKey ? expansionState.values : noExpansionOverrides;
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const pressedPointerButtonsRef = useRef(new Set<"left" | "right">());
  const gyroClutchPointerRef = useRef<number | null>(null);
  const gyroClutchTimerRef = useRef<number | null>(null);
  const gyroClutchEngagedRef = useRef(false);
  const gyroScrollActiveRef = useRef(false);
  const gyroTapRef = useRef<{
    pointerId: number;
    startedAt: number;
    buttonWasHeld: boolean;
  } | null>(null);
  const { cancel, emit, onTouchCancel, onTouchEnd, onTouchMove, onTouchStart } = usePointerInput({
    send,
    state,
    trackpadSettings,
    inputContext: "custom-screens",
  });
  const gyroSurfaceEnabled =
    definition?.sections.some(
      (section) =>
        section.kind === "trackpad" &&
        section.trackpadGyroControl &&
        section.trackpadEnabled &&
        (!definition.orientationLayoutsEnabled ||
          (orientation === "portrait" ? section.portrait : section.landscape)?.visible !== false) &&
        (!section.collapsible || (expansionOverrides.get(section.id) ?? section.initiallyExpanded)),
    ) === true;
  const gyro = useGyroMouse({
    activationRequest: gyroActivationRequest,
    connected: state === "paired",
    enabledSurface: gyroSurfaceEnabled,
    onMove: (dx, dy) => {
      emit({ type: "pointer.move", inputContext: "gyro-mouse", dx, dy });
    },
    onSelectedChange: onGyroSelectedChange,
    onStop: cancel,
    sensitivity: trackpadSettings.gyroSensitivity,
    sessionKey: connectionEpoch,
  });
  const setGyroEngaged = gyro.setEngaged;
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

  const clearGyroClutchTimer = useCallback(() => {
    if (gyroClutchTimerRef.current !== null) {
      window.clearTimeout(gyroClutchTimerRef.current);
      gyroClutchTimerRef.current = null;
    }
  }, []);

  const cancelGyroClutch = useCallback(() => {
    clearGyroClutchTimer();
    gyroClutchPointerRef.current = null;
    gyroClutchEngagedRef.current = false;
    gyroTapRef.current = null;
    setGyroEngaged(false);
  }, [clearGyroClutchTimer, setGyroEngaged]);

  const releasePointerButtons = useCallback(() => {
    for (const button of pressedPointerButtonsRef.current) {
      emitRef.current({ type: "pointer.button", button, action: "up" });
    }
    pressedPointerButtonsRef.current.clear();
    if (gyroScrollActiveRef.current) {
      gyroScrollActiveRef.current = false;
      cancel();
    }
    cancelGyroClutch();
  }, [cancel, cancelGyroClutch]);

  const updateGyroEngagement = useCallback(() => {
    setGyroEngaged(
      gyro.availability === "ready" &&
        !gyroScrollActiveRef.current &&
        (gyroClutchEngagedRef.current || pressedPointerButtonsRef.current.size > 0),
    );
  }, [gyro.availability, setGyroEngaged]);

  const startGyroClutch = useCallback(
    (pointerId: number, startedAt: number) => {
      gyroClutchPointerRef.current = pointerId;
      gyroClutchEngagedRef.current = false;
      gyroTapRef.current = {
        pointerId,
        startedAt,
        buttonWasHeld: pressedPointerButtonsRef.current.size > 0,
      };
      clearGyroClutchTimer();
      gyroClutchTimerRef.current = window.setTimeout(() => {
        gyroClutchTimerRef.current = null;
        if (gyroClutchPointerRef.current !== pointerId) {
          return;
        }
        gyroClutchEngagedRef.current = true;
        updateGyroEngagement();
      }, gyroHoldActivationDelayMs);
    },
    [clearGyroClutchTimer, updateGyroEngagement],
  );

  const finishGyroClutch = useCallback(
    (pointerId: number, finishedAt: number, allowClick: boolean) => {
      if (gyroClutchPointerRef.current !== pointerId) {
        return;
      }
      const tap = gyroTapRef.current;
      const shouldClick =
        allowClick &&
        tap?.pointerId === pointerId &&
        finishedAt - tap.startedAt <= gyroTapMaximumDurationMs &&
        !tap.buttonWasHeld &&
        pressedPointerButtonsRef.current.size === 0;
      clearGyroClutchTimer();
      gyroClutchPointerRef.current = null;
      gyroClutchEngagedRef.current = false;
      gyroTapRef.current = null;
      updateGyroEngagement();
      if (shouldClick) {
        emit({ type: "pointer.button", button: "left", action: "click" });
      }
    },
    [clearGyroClutchTimer, emit, updateGyroEngagement],
  );

  useEffect(
    () => () => {
      cancelGyroClutch();
    },
    [cancelGyroClutch],
  );

  useEffect(() => {
    if (gyro.selected && gyro.availability === "ready") {
      return;
    }
    if (gyroScrollActiveRef.current) {
      gyroScrollActiveRef.current = false;
      cancel();
    }
    cancelGyroClutch();
  }, [cancel, cancelGyroClutch, gyro.availability, gyro.selected]);

  useEffect(() => {
    const update = () => {
      setOrientation(window.innerWidth > window.innerHeight ? "landscape" : "portrait");
    };
    const stopWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        stopRepeat();
        releasePointerButtons();
      }
    };
    const releaseOnBlur = () => {
      stopRepeat();
      releasePointerButtons();
    };
    window.addEventListener("resize", update);
    window.addEventListener("blur", releaseOnBlur);
    document.addEventListener("visibilitychange", stopWhenHidden);
    return () => {
      stopRepeat();
      releasePointerButtons();
      window.removeEventListener("resize", update);
      window.removeEventListener("blur", releaseOnBlur);
      document.removeEventListener("visibilitychange", stopWhenHidden);
    };
  }, [releasePointerButtons, stopRepeat]);

  useEffect(() => {
    if (state !== "paired") {
      releasePointerButtons();
      const clearFullscreen = window.setTimeout(() => {
        setFullscreenTrackpadId(null);
      }, 0);
      return () => {
        window.clearTimeout(clearFullscreen);
      };
    }
    return undefined;
  }, [releasePointerButtons, state]);

  useEffect(() => {
    if (
      state === "paired" &&
      definition?.sections.some((section) => section.kind === "volume" && section.volumeEnabled)
    ) {
      send({ type: "audio.get" });
    }
  }, [definition, send, state]);

  useEffect(() => {
    if (!definition) {
      return undefined;
    }

    const laserButton = definition.sections
      .flatMap((section) => section.buttons)
      .find(
        (button) => button.laserPointerColor !== null && button.laserPointerColor !== undefined,
      );
    if (!laserButton) {
      return undefined;
    }

    return () => {
      invoke(definition.id, definition.revision, laserButton.id, false, true);
    };
  }, [definition, invoke]);

  const sections = useMemo(() => {
    if (!definition) {
      return [];
    }

    return definition.sections
      .map((section, index) => ({
        section,
        baseOrder: index,
        layout: definition.orientationLayoutsEnabled
          ? orientation === "portrait"
            ? section.portrait
            : section.landscape
          : undefined,
      }))
      .filter(({ layout }) => layout?.visible !== false)
      .sort(
        (left, right) =>
          (left.layout?.order ?? left.baseOrder) - (right.layout?.order ?? right.baseOrder),
      );
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
      {!definition && !error && (
        <div className="custom-screen-state" role="status">
          Loading custom screen…
        </div>
      )}
      {error && (
        <div className="custom-screen-state custom-screen-error" role="alert">
          {error}
        </div>
      )}
      {definition && (
        <div className="custom-screen-grid">
          {sectionRows.map((row, rowIndex) => {
            const rowFillWeight = row.reduce((weight, { section }) => {
              const collapsible = section.collapsible;
              const expanded = expansionOverrides.get(section.id) ?? section.initiallyExpanded;
              return section.heightMode === "fill" && (!collapsible || expanded)
                ? Math.max(weight, section.fillWeight)
                : weight;
            }, 0);
            return (
              <div
                className={`custom-screen-row${rowFillWeight > 0 ? " height-fill" : ""}`}
                key={`row-${rowIndex}`}
                style={rowFillWeight > 0 ? { flexGrow: rowFillWeight, flexBasis: 0 } : undefined}
              >
                {row.map(({ section, layout }) => {
                  const widthColumns = layout?.widthColumns ?? section.widthColumns;
                  const trackpadEnabled = section.trackpadEnabled;
                  const trackpadButtons =
                    section.trackpadButtonSide === "left"
                      ? (["right", "left"] as const)
                      : (["left", "right"] as const);
                  const collapsible = section.collapsible;
                  const expanded = expansionOverrides.get(section.id) ?? section.initiallyExpanded;
                  const fullscreen = state === "paired" && fullscreenTrackpadId === section.id;
                  const contentId = `custom-screen-section-content-${section.id}`;
                  const kindClass = collapsible
                    ? "collapsible"
                    : section.kind === "navigationRing" || section.kind === "dpad"
                      ? "navigation-ring"
                      : section.kind;
                  return (
                    <section
                      className={`custom-screen-section height-${section.heightMode} kind-${kindClass}${collapsible ? (expanded ? " is-expanded" : " is-collapsed") : ""}`}
                      key={section.id}
                      style={{
                        gridColumn: `span ${widthColumns}`,
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
                                  : noExpansionOverrides,
                              );
                              next.set(section.id, !expanded);
                              return { definitionKey, values: next };
                            });
                          }}
                          type="button"
                        >
                          <h2>{section.name}</h2>
                          <ChevronDown aria-hidden="true" />
                        </button>
                      ) : (
                        section.showHeader && <h2>{section.name}</h2>
                      )}
                      {(!collapsible || expanded) &&
                        (section.kind === "trackpad" ? (
                          <div
                            aria-disabled={!trackpadEnabled}
                            className={`custom-screen-trackpad-layout buttons-${section.trackpadButtonSide}${fullscreen ? " is-fullscreen" : ""}${collapsible ? " custom-screen-collapsible-content" : ""}`}
                            id={collapsible ? contentId : undefined}
                            title={
                              trackpadEnabled
                                ? section.name
                                : (section.trackpadUnavailableReason ??
                                  "Remote input is unavailable.")
                            }
                          >
                            <div
                              aria-label={section.name}
                              className={`custom-screen-trackpad${section.trackpadGyroControl && gyro.selected ? " gyro-selected" : ""}${section.trackpadGyroControl && gyro.engaged ? " gyro-engaged" : ""}`}
                              role="application"
                              onPointerCancel={
                                trackpadEnabled &&
                                section.trackpadGyroControl &&
                                gyro.selected &&
                                gyro.availability === "ready"
                                  ? (event) => {
                                      if (event.pointerType !== "touch") {
                                        finishGyroClutch(event.pointerId, event.timeStamp, false);
                                      }
                                    }
                                  : undefined
                              }
                              onPointerDown={
                                trackpadEnabled &&
                                section.trackpadGyroControl &&
                                gyro.selected &&
                                gyro.availability === "ready"
                                  ? (event) => {
                                      if (
                                        event.pointerType === "touch" ||
                                        (event.target as HTMLElement).closest("button") ||
                                        event.button !== 0 ||
                                        event.isPrimary === false ||
                                        gyroScrollActiveRef.current
                                      ) {
                                        return;
                                      }
                                      if (gyroClutchPointerRef.current !== null) {
                                        gyroTapRef.current = null;
                                        return;
                                      }
                                      event.preventDefault();
                                      startGyroClutch(event.pointerId, event.timeStamp);
                                      try {
                                        event.currentTarget.setPointerCapture(event.pointerId);
                                      } catch {
                                        /* Pointer capture is optional. */
                                      }
                                    }
                                  : undefined
                              }
                              onPointerUp={
                                trackpadEnabled &&
                                section.trackpadGyroControl &&
                                gyro.selected &&
                                gyro.availability === "ready"
                                  ? (event) => {
                                      if (event.pointerType !== "touch") {
                                        finishGyroClutch(event.pointerId, event.timeStamp, true);
                                      }
                                    }
                                  : undefined
                              }
                              onLostPointerCapture={
                                trackpadEnabled &&
                                section.trackpadGyroControl &&
                                gyro.selected &&
                                gyro.availability === "ready"
                                  ? (event) => {
                                      if (event.pointerType !== "touch") {
                                        finishGyroClutch(event.pointerId, event.timeStamp, false);
                                      }
                                    }
                                  : undefined
                              }
                              onTouchStart={
                                trackpadEnabled
                                  ? section.trackpadGyroControl && gyro.selected
                                    ? (event) => {
                                        if (
                                          gyro.availability !== "ready" ||
                                          (event.target as HTMLElement).closest("button")
                                        ) {
                                          return;
                                        }
                                        if (
                                          event.targetTouches.length === 2 &&
                                          !gyroScrollActiveRef.current
                                        ) {
                                          gyroScrollActiveRef.current = true;
                                          const clutchPointer = gyroClutchPointerRef.current;
                                          if (clutchPointer !== null) {
                                            finishGyroClutch(clutchPointer, event.timeStamp, false);
                                          } else {
                                            updateGyroEngagement();
                                          }
                                          onTouchStart(event);
                                          return;
                                        }
                                        if (gyroScrollActiveRef.current) {
                                          event.preventDefault();
                                          gyroScrollActiveRef.current = false;
                                          cancel();
                                        }
                                        if (
                                          event.touches.length !== 1 ||
                                          gyroClutchPointerRef.current !== null
                                        ) {
                                          gyroTapRef.current = null;
                                          return;
                                        }
                                        event.preventDefault();
                                        const touch = event.touches[0];
                                        if (touch) {
                                          startGyroClutch(touch.identifier, event.timeStamp);
                                        }
                                      }
                                    : onTouchStart
                                  : undefined
                              }
                              onTouchMove={
                                trackpadEnabled
                                  ? section.trackpadGyroControl && gyro.selected
                                    ? (event) => {
                                        if (gyroScrollActiveRef.current) {
                                          onTouchMove(event, "scroll");
                                        }
                                      }
                                    : onTouchMove
                                  : undefined
                              }
                              onTouchEnd={
                                trackpadEnabled
                                  ? section.trackpadGyroControl && gyro.selected
                                    ? (event) => {
                                        if (gyroScrollActiveRef.current) {
                                          onTouchEnd(event, false);
                                          gyroScrollActiveRef.current = false;
                                          return;
                                        }
                                        const touch = Array.from(event.changedTouches).find(
                                          (candidate) =>
                                            candidate.identifier === gyroClutchPointerRef.current,
                                        );
                                        if (touch) {
                                          finishGyroClutch(touch.identifier, event.timeStamp, true);
                                        }
                                      }
                                    : onTouchEnd
                                  : undefined
                              }
                              onTouchCancel={
                                trackpadEnabled
                                  ? section.trackpadGyroControl && gyro.selected
                                    ? (event) => {
                                        if (gyroScrollActiveRef.current) {
                                          gyroScrollActiveRef.current = false;
                                          onTouchCancel(event);
                                        }
                                        const touch = Array.from(event.changedTouches).find(
                                          (candidate) =>
                                            candidate.identifier === gyroClutchPointerRef.current,
                                        );
                                        if (touch) {
                                          finishGyroClutch(
                                            touch.identifier,
                                            event.timeStamp,
                                            false,
                                          );
                                        }
                                      }
                                    : onTouchCancel
                                  : undefined
                              }
                            >
                              {section.trackpadGyroControl && (
                                <div
                                  className="custom-screen-trackpad-movement-selector"
                                  role="group"
                                  aria-label="Trackpad movement"
                                >
                                  <button
                                    type="button"
                                    disabled={!trackpadEnabled || state !== "paired"}
                                    className={!gyro.selected ? "active" : ""}
                                    aria-pressed={!gyro.selected}
                                    onClick={(event) => {
                                      event.stopPropagation();
                                      gyro.setSelected(false);
                                    }}
                                  >
                                    Touch
                                  </button>
                                  <button
                                    type="button"
                                    disabled={!trackpadEnabled || state !== "paired"}
                                    className={gyro.selected ? "active" : ""}
                                    aria-pressed={gyro.selected}
                                    title={gyroMessage(gyro.availability)}
                                    onClick={(event) => {
                                      event.stopPropagation();
                                      gyro.enableFromUserGesture();
                                    }}
                                  >
                                    Gyro
                                  </button>
                                </div>
                              )}
                              {section.trackpadFullscreenControl && (
                                <button
                                  aria-label={
                                    fullscreen
                                      ? `Restore ${section.name}`
                                      : `Expand ${section.name}`
                                  }
                                  className="custom-screen-trackpad-expand"
                                  onClick={(event) => {
                                    event.stopPropagation();
                                    setFullscreenTrackpadId(fullscreen ? null : section.id);
                                  }}
                                  onTouchStart={(event) => {
                                    event.stopPropagation();
                                  }}
                                  onTouchMove={(event) => {
                                    event.stopPropagation();
                                  }}
                                  onTouchEnd={(event) => {
                                    event.stopPropagation();
                                  }}
                                  title={fullscreen ? "Restore trackpad" : "Expand trackpad"}
                                  type="button"
                                >
                                  {fullscreen ? (
                                    <Minimize2 aria-hidden="true" />
                                  ) : (
                                    <Maximize2 aria-hidden="true" />
                                  )}
                                </button>
                              )}
                              <span className="custom-screen-trackpad-label" aria-hidden="true">
                                Trackpad
                              </span>
                              {fullscreen &&
                                (section.trackpadLeftClick || section.trackpadRightClick) && (
                                  <div
                                    aria-label="Mouse buttons"
                                    className="custom-screen-trackpad-fullscreen-buttons"
                                  >
                                    {trackpadButtons
                                      .filter((button) =>
                                        button === "left"
                                          ? section.trackpadLeftClick
                                          : section.trackpadRightClick,
                                      )
                                      .map((button) =>
                                        renderPointerButton(button, trackpadEnabled),
                                      )}
                                  </div>
                                )}
                            </div>
                            {!fullscreen &&
                              (section.trackpadLeftClick || section.trackpadRightClick) && (
                                <div className="custom-screen-trackpad-buttons">
                                  {trackpadButtons
                                    .filter((button) =>
                                      button === "left"
                                        ? section.trackpadLeftClick
                                        : section.trackpadRightClick,
                                    )
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
                            invoke={(button, enabled) => {
                              if (enabled === undefined) {
                                invoke(definition.id, definition.revision, button.id);
                              } else {
                                invoke(definition.id, definition.revision, button.id, enabled);
                              }
                            }}
                            laserPointerActive={laserPointerActive}
                            laserPointerColor={laserPointerColor}
                            laserPointerDefaultColor={laserPointerDefaultColor}
                            laserPointerPending={laserPointerPending}
                            onPointerDown={press}
                            onPointerCancel={stopRepeat}
                            onPointerUp={stopRepeat}
                            onLostPointerCapture={stopRepeat}
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
          if (gyroClutchPointerRef.current !== null) {
            gyroTapRef.current = null;
          }
          event.currentTarget.setPointerCapture?.(event.pointerId);
          pressedPointerButtonsRef.current.add(button);
          emit({ type: "pointer.button", button, action: "down" });
          if (gyro.selected) {
            updateGyroEngagement();
          }
        }}
        onPointerUp={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
            if (gyro.selected) {
              updateGyroEngagement();
            }
          }
        }}
        onPointerCancel={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
            if (gyro.selected) {
              updateGyroEngagement();
            }
          }
        }}
        onLostPointerCapture={() => {
          if (pressedPointerButtonsRef.current.delete(button)) {
            emit({ type: "pointer.button", button, action: "up" });
            if (gyro.selected) {
              updateGyroEngagement();
            }
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

function gyroMessage(availability: GyroAvailability): string {
  if (availability === "insecure") {
    return "Gyro requires Enhanced capabilities over HTTPS";
  }
  if (availability === "missing-api") {
    return "Gyro is not available on this device";
  }
  if (availability === "denied") {
    return "Gyro permission was denied";
  }
  if (availability === "no-data") {
    return "No Gyro sensor data received";
  }
  return "Use Gyro mouse";
}
