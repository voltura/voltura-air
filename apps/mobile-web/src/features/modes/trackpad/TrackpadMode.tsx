import { useEffect, useEffectEvent, useRef, type ReactNode } from "react";
import { Maximize2, Minimize2, MousePointer2, Orbit, Volume2, VolumeX } from "lucide-react";
import type { GyroAvailability } from "../../../foundation/input/gyroMouse";
import type { TrackpadSettings, TwoFingerMode } from "../../../foundation/input/gestures";
import type { AudioStateMessage } from "../../../foundation/protocol/messages";

type MouseButtonName = "left" | "right";
const gyroTapMaximumDurationMs = 300;
const gyroHoldActivationDelayMs = gyroTapMaximumDurationMs + 1;

interface TrackpadModeProps {
  audioState: AudioStateMessage | null;
  compactModeSelector?: ReactNode | undefined;
  isExpanded: boolean;
  gyro?: {
    availability: GyroAvailability;
    enableFromUserGesture: () => void;
    engaged: boolean;
    selected: boolean;
    setEngaged: (engaged: boolean) => void;
    setSelected: (selected: boolean) => void;
  };
  showRestoredMouseButtons?: boolean;
  supportsVolumeControl: boolean;
  trackpadSettings: TrackpadSettings;
  twoFingerMode: TwoFingerMode;
  onSetVolume: (volume: number) => void;
  onToggleExpanded: () => void;
  onToggleMute: () => void;
  onTwoFingerModeChange: (mode: TwoFingerMode) => void;
  onTouchCancel: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchEnd: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchMove: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchStart: (event: React.TouchEvent<HTMLDivElement>) => void;
  onMouseButtonClick: (button: MouseButtonName) => void;
  onMouseButtonDown: (button: MouseButtonName) => void;
  onMouseButtonUp: (button: MouseButtonName) => void;
}

export function TrackpadMode({
  audioState,
  compactModeSelector,
  isExpanded,
  gyro = disabledGyro,
  showRestoredMouseButtons = true,
  supportsVolumeControl,
  trackpadSettings,
  twoFingerMode,
  onSetVolume,
  onToggleExpanded,
  onToggleMute,
  onTwoFingerModeChange,
  onTouchCancel,
  onTouchEnd,
  onTouchMove,
  onTouchStart,
  onMouseButtonClick,
  onMouseButtonDown,
  onMouseButtonUp,
}: TrackpadModeProps) {
  const activeButtonPointers = useRef(new Map<number, MouseButtonName>());
  const clutchPointerRef = useRef<number | null>(null);
  const clutchEngagementTimerRef = useRef<number | null>(null);
  const gyroClutchEngagedRef = useRef(false);
  const gyroTapRef = useRef<{
    pointerId: number;
    startedAt: number;
    buttonWasHeld: boolean;
  } | null>(null);
  const updateGyroEngagement = () => {
    gyro.setEngaged(
      gyro.availability === "ready" &&
        (gyroClutchEngagedRef.current || activeButtonPointers.current.size > 0),
    );
  };
  const clearClutchEngagementTimer = () => {
    if (clutchEngagementTimerRef.current !== null) {
      window.clearTimeout(clutchEngagementTimerRef.current);
      clutchEngagementTimerRef.current = null;
    }
  };
  const startGyroClutch = (pointerId: number, startedAt: number) => {
    clutchPointerRef.current = pointerId;
    gyroClutchEngagedRef.current = false;
    gyroTapRef.current = {
      pointerId,
      startedAt,
      buttonWasHeld: activeButtonPointers.current.size > 0,
    };
    clearClutchEngagementTimer();
    clutchEngagementTimerRef.current = window.setTimeout(() => {
      clutchEngagementTimerRef.current = null;
      if (clutchPointerRef.current !== pointerId) {
        return;
      }
      gyroClutchEngagedRef.current = true;
      updateGyroEngagement();
    }, gyroHoldActivationDelayMs);
  };
  const finishGyroClutch = (pointerId: number, finishedAt: number, allowClick: boolean) => {
    if (clutchPointerRef.current !== pointerId) {
      return;
    }
    const tap = gyroTapRef.current;
    const shouldClick =
      allowClick &&
      tap?.pointerId === pointerId &&
      finishedAt - tap.startedAt <= gyroTapMaximumDurationMs &&
      !tap.buttonWasHeld &&
      activeButtonPointers.current.size === 0;
    clearClutchEngagementTimer();
    clutchPointerRef.current = null;
    gyroClutchEngagedRef.current = false;
    gyroTapRef.current = null;
    updateGyroEngagement();
    if (shouldClick) {
      onMouseButtonClick("left");
    }
  };
  const releaseAllMouseButtons = useEffectEvent(() => {
    const heldButtons = new Set(activeButtonPointers.current.values());
    activeButtonPointers.current.clear();
    clearClutchEngagementTimer();
    clutchPointerRef.current = null;
    gyroClutchEngagedRef.current = false;
    gyroTapRef.current = null;
    for (const button of heldButtons) {
      onMouseButtonUp(button);
    }
    gyro.setEngaged(false);
  });

  useEffect(() => {
    const releaseOnHidden = () => {
      if (document.visibilityState === "hidden") {
        releaseAllMouseButtons();
      }
    };

    window.addEventListener("blur", releaseAllMouseButtons);
    document.addEventListener("visibilitychange", releaseOnHidden);
    return () => {
      window.removeEventListener("blur", releaseAllMouseButtons);
      document.removeEventListener("visibilitychange", releaseOnHidden);
      releaseAllMouseButtons();
    };
  }, []);

  useEffect(() => {
    if (gyro.selected && gyro.availability === "ready") {
      return;
    }
    if (clutchPointerRef.current !== null || gyroTapRef.current !== null) {
      clearClutchEngagementTimer();
      clutchPointerRef.current = null;
      gyroClutchEngagedRef.current = false;
      gyroTapRef.current = null;
      gyro.setEngaged(false);
    }
  }, [gyro]);

  const stopTouchPropagation = (event: React.TouchEvent<HTMLButtonElement>) => {
    event.stopPropagation();
  };
  const stopContextMenu = (event: React.MouseEvent) => {
    event.preventDefault();
  };
  const clickButtons = trackpadSettings.leftHandedButtons
    ? [
        { label: "Right", button: "right" as const },
        { label: "Left", button: "left" as const },
      ]
    : [
        { label: "Left", button: "left" as const },
        { label: "Right", button: "right" as const },
      ];
  const showVolumeControl =
    !isExpanded &&
    supportsVolumeControl &&
    trackpadSettings.showVolumeControl &&
    audioState !== null;

  const pressMouseButton = (
    event: React.PointerEvent<HTMLButtonElement>,
    button: MouseButtonName,
  ) => {
    event.preventDefault();
    event.stopPropagation();
    if (clutchPointerRef.current !== null) {
      gyroTapRef.current = null;
    }
    if (activeButtonPointers.current.has(event.pointerId)) {
      return;
    }

    const buttonWasAlreadyHeld = [...activeButtonPointers.current.values()].includes(button);
    activeButtonPointers.current.set(event.pointerId, button);
    if (gyro.selected) {
      updateGyroEngagement();
    }
    if (!buttonWasAlreadyHeld) {
      onMouseButtonDown(button);
    }
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      // Pointer capture is an enhancement for drag-to-hold. Some mobile
      // browsers expose the API but reject capture for touch pointers.
    }
  };

  const releaseMouseButton = (event: React.PointerEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    const button = activeButtonPointers.current.get(event.pointerId);
    if (!button) {
      return;
    }

    activeButtonPointers.current.delete(event.pointerId);
    if (![...activeButtonPointers.current.values()].includes(button)) {
      onMouseButtonUp(button);
    }
    if (gyro.selected) {
      updateGyroEngagement();
    }
    try {
      if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
        event.currentTarget.releasePointerCapture?.(event.pointerId);
      }
    } catch {
      // The pointer may already have been released by the browser.
    }
  };

  return (
    <section
      className={`trackpad-mode ${isExpanded ? "expanded" : ""} ${showVolumeControl ? "has-volume-control" : ""} ${trackpadSettings.largeClickButtons ? "large-click-buttons" : ""}`}
    >
      {showVolumeControl && (
        <div className={`volume-control ${audioState.muted ? "muted" : ""}`}>
          <button
            className="icon-button"
            type="button"
            aria-label={audioState.muted ? "Unmute PC" : "Mute PC"}
            title={audioState.muted ? "Unmute PC" : "Mute PC"}
            onClick={onToggleMute}
            onTouchStart={stopTouchPropagation}
            onTouchMove={stopTouchPropagation}
            onTouchEnd={stopTouchPropagation}
          >
            {audioState.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
          </button>
          <div className="range-row">
            <input
              aria-label="PC volume"
              type="range"
              min="0"
              max="100"
              step="1"
              value={audioState.volume}
              onChange={(event) => {
                onSetVolume(Number(event.target.value));
              }}
            />
            <output>{audioState.volume}%</output>
          </div>
        </div>
      )}
      <div
        className={`trackpad-surface ${gyro.selected ? "gyro-selected" : ""} ${gyro.engaged ? "gyro-engaged" : ""}`}
        onContextMenu={stopContextMenu}
        onPointerCancel={
          gyro.selected && gyro.availability === "ready"
            ? (event) => {
                if (event.pointerType !== "touch") {
                  finishGyroClutch(event.pointerId, event.timeStamp, false);
                }
              }
            : undefined
        }
        onPointerDown={
          gyro.selected && gyro.availability === "ready"
            ? (event) => {
                if (event.pointerType === "touch") {
                  return;
                }
                if ((event.target as HTMLElement).closest("button")) {
                  return;
                }
                if (clutchPointerRef.current !== null) {
                  gyroTapRef.current = null;
                  return;
                }
                if (event.button !== 0 || event.isPrimary === false) {
                  return;
                }
                event.preventDefault();
                startGyroClutch(event.pointerId, event.timeStamp);
                try {
                  event.currentTarget.setPointerCapture(event.pointerId);
                } catch {
                  // Pointer capture is not consistently available on mobile browsers.
                }
              }
            : undefined
        }
        onPointerUp={
          gyro.selected && gyro.availability === "ready"
            ? (event) => {
                if (event.pointerType !== "touch") {
                  finishGyroClutch(event.pointerId, event.timeStamp, true);
                }
              }
            : undefined
        }
        onLostPointerCapture={
          gyro.selected && gyro.availability === "ready"
            ? (event) => {
                if (event.pointerType !== "touch") {
                  finishGyroClutch(event.pointerId, event.timeStamp, false);
                }
              }
            : undefined
        }
        onTouchCancel={
          gyro.selected
            ? (event) => {
                const touch = Array.from(event.changedTouches).find(
                  (candidate) => candidate.identifier === clutchPointerRef.current,
                );
                if (touch) {
                  finishGyroClutch(touch.identifier, event.timeStamp, false);
                }
              }
            : onTouchCancel
        }
        onTouchStart={
          gyro.selected
            ? (event) => {
                if (
                  gyro.availability !== "ready" ||
                  (event.target as HTMLElement).closest("button")
                ) {
                  return;
                }
                if (event.touches.length !== 1 || clutchPointerRef.current !== null) {
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
        }
        onTouchMove={gyro.selected ? undefined : onTouchMove}
        onTouchEnd={
          gyro.selected
            ? (event) => {
                const touch = Array.from(event.changedTouches).find(
                  (candidate) => candidate.identifier === clutchPointerRef.current,
                );
                if (touch) {
                  finishGyroClutch(touch.identifier, event.timeStamp, true);
                }
              }
            : onTouchEnd
        }
      >
        {compactModeSelector && (
          <div className="trackpad-compact-mode-selector">{compactModeSelector}</div>
        )}
        <button
          className="trackpad-expand-button"
          type="button"
          aria-label={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          title={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          onClick={(event) => {
            event.stopPropagation();
            onToggleExpanded();
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
        >
          {isExpanded ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
        </button>
        <div className="trackpad-movement-selector" role="group" aria-label="Trackpad movement">
          <button
            type="button"
            className={!gyro.selected ? "active" : ""}
            aria-pressed={!gyro.selected}
            onClick={(event) => {
              event.stopPropagation();
              gyro.setSelected(false);
            }}
            onTouchStart={stopTouchPropagation}
            onTouchMove={stopTouchPropagation}
            onTouchEnd={stopTouchPropagation}
          >
            Touch
          </button>
          <button
            type="button"
            className={gyro.selected ? "active" : ""}
            aria-pressed={gyro.selected}
            onClick={(event) => {
              event.stopPropagation();
              gyro.enableFromUserGesture();
            }}
            onTouchStart={stopTouchPropagation}
            onTouchMove={stopTouchPropagation}
            onTouchEnd={stopTouchPropagation}
          >
            Gyro
          </button>
        </div>
        {!gyro.selected && trackpadSettings.zoomGestures && (
          <button
            className="trackpad-two-finger-mode"
            type="button"
            aria-label={`Two-finger mode: ${twoFingerMode === "scroll" ? "Scroll" : "Zoom"}. Switch to ${twoFingerMode === "scroll" ? "Zoom" : "Scroll"}`}
            onClick={(event) => {
              event.stopPropagation();
              onTwoFingerModeChange(twoFingerMode === "scroll" ? "zoom" : "scroll");
            }}
            onTouchStart={stopTouchPropagation}
            onTouchMove={stopTouchPropagation}
            onTouchEnd={stopTouchPropagation}
            onTouchCancel={stopTouchPropagation}
          >
            {twoFingerMode === "scroll" ? "Scroll" : "Zoom"}
          </button>
        )}
        {gyro.selected ? (
          <div
            className="gyro-clutch-content"
            aria-label={
              gyro.availability === "ready"
                ? "Tap to click, double-tap to double-click, hold to move the mouse"
                : undefined
            }
            aria-live="polite"
            aria-pressed={gyro.availability === "ready" ? gyro.engaged : undefined}
            role={gyro.availability === "ready" ? "button" : undefined}
            tabIndex={gyro.availability === "ready" ? 0 : undefined}
            onBlur={
              gyro.availability === "ready"
                ? () => {
                    gyro.setEngaged(false);
                  }
                : undefined
            }
            onClick={
              gyro.availability === "ready"
                ? (event) => {
                    if (event.detail === 0) {
                      onMouseButtonClick("left");
                    }
                  }
                : undefined
            }
            onKeyDown={
              gyro.availability === "ready"
                ? (event) => {
                    if (event.key === "Enter" && !event.repeat) {
                      event.preventDefault();
                      onMouseButtonClick("left");
                    } else if (event.key === " ") {
                      event.preventDefault();
                      gyro.setEngaged(true);
                    }
                  }
                : undefined
            }
            onKeyUp={
              gyro.availability === "ready"
                ? (event) => {
                    if (event.key === " ") {
                      event.preventDefault();
                      gyro.setEngaged(false);
                    }
                  }
                : undefined
            }
          >
            <Orbit aria-hidden="true" />
            <strong>
              {gyro.engaged
                ? "Moving"
                : gyro.availability === "ready"
                  ? "Tap to click · Double-tap to double-click · Hold to move"
                  : gyroMessage(gyro.availability)}
            </strong>
            {gyro.availability !== "ready" && (
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  gyro.enableFromUserGesture();
                }}
              >
                Retry
              </button>
            )}
          </div>
        ) : (
          <MousePointer2 aria-hidden="true" />
        )}
        {isExpanded && (
          <div className="trackpad-click-zones" aria-label="Mouse buttons">
            {clickButtons.map((button) => (
              <button
                key={button.label}
                type="button"
                onLostPointerCapture={releaseMouseButton}
                onPointerCancel={releaseMouseButton}
                onPointerDown={(event) => {
                  pressMouseButton(event, button.button);
                }}
                onPointerUp={releaseMouseButton}
                onTouchEnd={stopTouchPropagation}
                onTouchMove={stopTouchPropagation}
                onTouchStart={stopTouchPropagation}
              >
                {button.label}
              </button>
            ))}
          </div>
        )}
      </div>
      {!isExpanded && showRestoredMouseButtons && (
        <div className="mouse-buttons">
          {clickButtons.map((button) => (
            <button
              key={button.label}
              onLostPointerCapture={releaseMouseButton}
              onPointerCancel={releaseMouseButton}
              onPointerDown={(event) => {
                pressMouseButton(event, button.button);
              }}
              onPointerUp={releaseMouseButton}
              type="button"
            >
              <span>{button.label}</span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

const noop = () => {
  // Used only by touch-only test and embedded surfaces.
};

const disabledGyro = {
  availability: "missing-api" as const,
  enableFromUserGesture: noop,
  engaged: false,
  selected: false,
  setEngaged: noop,
  setSelected: noop,
};

function gyroMessage(availability: GyroAvailability): string {
  if (availability === "insecure") {
    return "Gyro requires Enhanced capabilities over HTTPS";
  }
  if (availability === "missing-api") {
    return "Motion sensors are unavailable in this browser";
  }
  if (availability === "denied") {
    return "Motion access denied. Reopen Voltura Air or check browser permissions";
  }
  if (availability === "no-data") {
    return "No motion sensor data received";
  }
  return "Hold to move";
}
