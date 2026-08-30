import { AppWindow, LoaderCircle, Maximize2, Minus, X } from "lucide-react";
import { useEffect, useRef, useState, type PointerEvent } from "react";
import type { AppsWindowSummary } from "../../foundation/protocol/messages";
import type { AppsPreviewState } from "./useAppsPreviews";

interface Props {
  busy: boolean;
  onActivate: () => void;
  onClose: () => void;
  onSelect: () => void;
  previewState: AppsPreviewState;
  previewUrl?: string | undefined;
  selected: boolean;
  window: AppsWindowSummary;
}

interface Gesture {
  pointerId: number;
  startX: number;
  startY: number;
  axis: "pending" | "horizontal" | "vertical";
}

const closeThreshold = 72;
const previewSwapDurationMs = 180;

function AppsPreviewImage({ url }: { url: string }) {
  const [displayedUrl, setDisplayedUrl] = useState(url);
  const [readyUrl, setReadyUrl] = useState<string | null>(null);
  const incomingUrl = url === displayedUrl ? null : url;
  const incomingReady = incomingUrl !== null && readyUrl === incomingUrl;

  useEffect(() => {
    if (!incomingReady || !incomingUrl) {
      return;
    }
    const timeout = window.setTimeout(() => {
      setDisplayedUrl((current) => (current === displayedUrl ? incomingUrl : current));
    }, previewSwapDurationMs);
    return () => window.clearTimeout(timeout);
  }, [displayedUrl, incomingReady, incomingUrl]);

  return (
    <span className="apps-preview-images">
      <img src={displayedUrl} alt="" draggable={false} />
      {incomingUrl && (
        <img
          src={incomingUrl}
          alt=""
          className={`apps-preview-image-incoming${incomingReady ? " is-ready" : ""}`}
          draggable={false}
          onLoad={() => setReadyUrl(incomingUrl)}
        />
      )}
    </span>
  );
}

function showCloseProgress(card: HTMLElement, upwardDistance: number) {
  const distance = Math.max(0, upwardDistance);
  const progress = Math.min(1, distance / closeThreshold);
  card.classList.remove("is-close-committed");
  card.style.setProperty("--apps-close-progress", String(progress));
  card.style.setProperty("--apps-close-offset", `${-distance}px`);
  card.style.setProperty("--apps-close-scale", String(1 - progress * 0.02));
  card.style.setProperty("--apps-close-opacity", String(1 - progress * 0.22));
  card.style.setProperty("--apps-close-cue-offset", `${(1 - progress) * -8}px`);
  card.classList.toggle("is-close-dragging", distance > 0);
}

function commitClose(card: HTMLElement) {
  const currentOffset = Number.parseFloat(
    card.style.getPropertyValue("--apps-close-offset") || "0",
  );
  const offscreenOffset = currentOffset - Math.max(0, card.getBoundingClientRect().bottom) - 24;
  card.classList.remove("is-close-dragging");
  card.classList.add("is-close-committed");
  card.style.setProperty("--apps-close-offset", `${offscreenOffset}px`);
  card.style.setProperty("--apps-close-opacity", "0");
}

export function AppsWindowCard({
  busy,
  onActivate,
  onClose,
  onSelect,
  previewState,
  previewUrl,
  selected,
  window,
}: Props) {
  const cardRef = useRef<HTMLElement | null>(null);
  const gestureRef = useRef<Gesture | null>(null);
  const suppressClickRef = useRef(false);
  const wasBusyRef = useRef(busy);

  useEffect(() => {
    if (wasBusyRef.current && !busy && cardRef.current) {
      showCloseProgress(cardRef.current, 0);
    }
    wasBusyRef.current = busy;
  }, [busy]);

  const onPointerDown = (event: PointerEvent<HTMLElement>) => {
    suppressClickRef.current = false;
    showCloseProgress(event.currentTarget, 0);
    event.currentTarget.setPointerCapture?.(event.pointerId);
    gestureRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      axis: "pending",
    };
  };

  const onPointerMove = (event: PointerEvent<HTMLElement>) => {
    const gesture = gestureRef.current;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      return;
    }
    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    if (gesture.axis === "pending" && Math.hypot(dx, dy) >= 8) {
      gesture.axis = Math.abs(dx) >= Math.abs(dy) ? "horizontal" : "vertical";
    }
    if (gesture.axis === "vertical") {
      showCloseProgress(event.currentTarget, selected && !busy ? Math.max(0, -dy) : 0);
      event.preventDefault();
    }
  };

  const finishGesture = (event: PointerEvent<HTMLElement>) => {
    const gesture = gestureRef.current;
    gestureRef.current = null;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      showCloseProgress(event.currentTarget, 0);
      return;
    }
    suppressClickRef.current = gesture.axis !== "pending";
    if (gesture.axis !== "vertical") {
      showCloseProgress(event.currentTarget, 0);
      return;
    }
    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    if (selected && !busy && dy <= -closeThreshold && Math.abs(dy) > Math.abs(dx) * 1.25) {
      commitClose(event.currentTarget);
      onClose();
      return;
    }
    showCloseProgress(event.currentTarget, 0);
  };

  return (
    <article
      ref={cardRef}
      className={`apps-window-card${selected ? " is-selected" : ""}`}
      aria-current={selected ? "true" : undefined}
      onClickCapture={(event) => {
        if (suppressClickRef.current) {
          suppressClickRef.current = false;
          event.preventDefault();
          event.stopPropagation();
        }
      }}
      onPointerCancel={(event) => {
        gestureRef.current = null;
        suppressClickRef.current = false;
        showCloseProgress(event.currentTarget, 0);
      }}
      onLostPointerCapture={(event) => {
        if (!gestureRef.current) {
          return;
        }
        gestureRef.current = null;
        suppressClickRef.current = false;
        showCloseProgress(event.currentTarget, 0);
      }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={finishGesture}
    >
      <button
        type="button"
        className="apps-card-main"
        aria-label={
          selected
            ? `Activate ${window.title}`
            : `Select ${window.title}, ${window.applicationName}`
        }
        disabled={busy}
        onClick={selected ? onActivate : onSelect}
      >
        <span className="apps-preview-stage">
          {previewUrl ? (
            <AppsPreviewImage url={previewUrl} />
          ) : (
            <span
              className={`apps-preview-fallback${previewState === "loading" ? " is-loading" : ""}`}
              aria-hidden="true"
            >
              {previewState === "loading" ? <LoaderCircle /> : <AppWindow />}
              <small>
                {previewState === "loading" ? "Loading preview…" : "Preview unavailable"}
              </small>
            </span>
          )}
          {(window.active || window.minimized) && (
            <span className="apps-active-badge">{window.active ? "Active" : "Minimized"}</span>
          )}
          <span className="apps-swipe-close-cue" aria-hidden="true">
            <X />
            Close
          </span>
        </span>
        <span className="apps-card-details">
          <span className="apps-card-copy">
            <strong>{window.title}</strong>
            <span>{window.applicationName}</span>
          </span>
          <span className="apps-activation-hint" aria-hidden="true">
            {window.minimized ? (
              <Minus />
            ) : window.maximizeSupported ? (
              <Maximize2 />
            ) : (
              <AppWindow />
            )}
            <span>
              {window.minimized ? "Restore" : window.maximizeSupported ? "Open full" : "Focus"}
            </span>
          </span>
        </span>
      </button>
      {selected && (
        <button
          type="button"
          className="apps-close-button"
          aria-label={`Close ${window.title}`}
          disabled={busy}
          onClick={(event) => {
            event.stopPropagation();
            onClose();
          }}
        >
          <X aria-hidden="true" />
        </button>
      )}
    </article>
  );
}
