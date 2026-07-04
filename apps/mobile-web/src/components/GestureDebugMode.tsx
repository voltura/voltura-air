import { useEffect, useRef, useState } from "react";
import { Trash2 } from "lucide-react";
import { GestureRecognizer, touchesFromList, type GestureOutput, type TouchPoint, type TrackpadSettings } from "../gestures";

type GestureDebugModeProps = {
  trackpadSettings: TrackpadSettings;
};

type DebugFrame = {
  outputDx: number;
  outputDy: number;
  rawDx: number;
  rawDy: number;
  touchCount: number;
};

type DebugLogEntry = {
  id: number;
  label: string;
  outputs: GestureOutput[];
};

export function GestureDebugMode({ trackpadSettings }: GestureDebugModeProps) {
  const recognizerRef = useRef(new GestureRecognizer());
  const previousPointsRef = useRef<TouchPoint[]>([]);
  const nextLogIdRef = useRef(1);
  const [frame, setFrame] = useState<DebugFrame>({ outputDx: 0, outputDy: 0, rawDx: 0, rawDy: 0, touchCount: 0 });
  const [log, setLog] = useState<DebugLogEntry[]>([]);

  useEffect(() => {
    const resetActiveGesture = () => {
      recognizerRef.current.cancel();
      previousPointsRef.current = [];
      setFrame((current) => ({ ...current, outputDx: 0, outputDy: 0, rawDx: 0, rawDy: 0, touchCount: 0 }));
    };

    window.addEventListener("orientationchange", resetActiveGesture);
    window.addEventListener("resize", resetActiveGesture);
    window.visualViewport?.addEventListener("resize", resetActiveGesture);

    return () => {
      window.removeEventListener("orientationchange", resetActiveGesture);
      window.removeEventListener("resize", resetActiveGesture);
      window.visualViewport?.removeEventListener("resize", resetActiveGesture);
    };
  }, []);

  const appendLog = (label: string, outputs: GestureOutput[]) => {
    setLog((current) => [
      {
        id: nextLogIdRef.current++,
        label,
        outputs
      },
      ...current.slice(0, 19)
    ]);
  };

  const onTouchStart = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    const points = touchesFromList(event.targetTouches);
    recognizerRef.current.start(points, event.timeStamp);
    previousPointsRef.current = points;
    setFrame({ outputDx: 0, outputDy: 0, rawDx: 0, rawDy: 0, touchCount: points.length });
    appendLog("start", []);
  };

  const onTouchMove = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    const points = touchesFromList(event.targetTouches);
    const rawDelta = getRawDelta(previousPointsRef.current, points);
    const outputs = recognizerRef.current.move(points, event.timeStamp, trackpadSettings);
    const outputDelta = getOutputDelta(outputs);
    previousPointsRef.current = points;
    setFrame({ outputDx: outputDelta.dx, outputDy: outputDelta.dy, rawDx: rawDelta.dx, rawDy: rawDelta.dy, touchCount: points.length });
    appendLog("move", outputs);
  };

  const onTouchEnd = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    const outputs = recognizerRef.current.end(event.timeStamp, trackpadSettings);
    previousPointsRef.current = [];
    setFrame((current) => ({ ...current, touchCount: 0 }));
    appendLog("end", outputs);
  };

  const onTouchCancel = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.cancel();
    previousPointsRef.current = [];
    setFrame((current) => ({ ...current, touchCount: 0 }));
    appendLog("cancel", []);
  };

  return (
    <section className="gesture-debug-mode">
      <div
        className="gesture-debug-surface"
        onTouchCancel={onTouchCancel}
        onTouchEnd={onTouchEnd}
        onTouchMove={onTouchMove}
        onTouchStart={onTouchStart}
      >
        <span>Touch here</span>
      </div>

      <div className="gesture-debug-grid" aria-label="Gesture debug values">
        <DebugValue label="Touches" value={frame.touchCount.toString()} />
        <DebugValue label="Raw dx" value={formatNumber(frame.rawDx)} />
        <DebugValue label="Raw dy" value={formatNumber(frame.rawDy)} />
        <DebugValue label="Output dx" value={formatNumber(frame.outputDx)} />
        <DebugValue label="Output dy" value={formatNumber(frame.outputDy)} />
        <DebugValue label="Pointer speed" value={`${trackpadSettings.pointerSpeed}%`} />
        <DebugValue label="Smoothing" value={trackpadSettings.pointerSmoothing ? "On" : "Off"} />
        <DebugValue label="Pointer accel" value={trackpadSettings.pointerAcceleration ? "On" : "Off"} />
        <DebugValue label="Scroll accel" value={trackpadSettings.scrollAcceleration ? "On" : "Off"} />
        <DebugValue label="Scroll mode" value={trackpadSettings.scrollDirection === "normal" ? "Natural" : "Traditional"} />
      </div>

      <div className="gesture-debug-log">
        <div className="gesture-debug-log-header">
          <h2>Gesture log</h2>
          <button type="button" onClick={() => setLog([])}>
            <Trash2 aria-hidden="true" />
            <span>Clear</span>
          </button>
        </div>
        <ol aria-label="Gesture debug log">
          {log.map((entry) => (
            <li key={entry.id}>
              <strong>{entry.label}</strong>
              <code>{entry.outputs.length > 0 ? JSON.stringify(entry.outputs) : "[]"}</code>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

function DebugValue({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function getRawDelta(previous: TouchPoint[], current: TouchPoint[]): { dx: number; dy: number } {
  if (previous.length !== current.length || current.length === 0) {
    return { dx: 0, dy: 0 };
  }

  const previousCenter = getCenter(previous);
  const currentCenter = getCenter(current);
  return {
    dx: currentCenter.x - previousCenter.x,
    dy: currentCenter.y - previousCenter.y
  };
}

function getOutputDelta(outputs: GestureOutput[]): { dx: number; dy: number } {
  const output = outputs.find((entry) => entry.type === "pointer.move" || entry.type === "pointer.wheel");
  return output && "dx" in output && "dy" in output ? { dx: output.dx, dy: output.dy } : { dx: 0, dy: 0 };
}

function getCenter(points: TouchPoint[]): TouchPoint {
  return {
    id: -1,
    x: points.reduce((sum, point) => sum + point.x, 0) / points.length,
    y: points.reduce((sum, point) => sum + point.y, 0) / points.length
  };
}

function formatNumber(value: number): string {
  return (Math.round(value * 100) / 100).toString();
}
