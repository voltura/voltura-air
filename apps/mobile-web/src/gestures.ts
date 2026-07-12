import type { PointerButtonMessage, PointerMoveMessage, PointerWheelMessage, PointerZoomMessage } from "./protocol";

export type TouchPoint = {
  id: number;
  x: number;
  y: number;
};

export type GestureOutput = PointerMoveMessage | PointerWheelMessage | PointerZoomMessage | PointerButtonMessage;

export type TrackpadSettings = {
  verticalScroll: boolean;
  horizontalScroll: boolean;
  scrollDirection: "normal" | "inverted";
  pointerSpeed: number;
  pointerSmoothing: boolean;
  pointerAcceleration: boolean;
  scrollAcceleration: boolean;
  tapToClick: boolean;
  zoomGestures: boolean;
  showVolumeControl: boolean;
  enableSplitMode: boolean;
  hapticFeedback: boolean;
  leftHandedButtons: boolean;
  largeClickButtons: boolean;
};

type Mode = "idle" | "pointer" | "twoFinger" | "cancelled";

const tapMs = 260;
const longPressMs = 620;
const tapDistance = 10;
const scrollTapDistance = 12;
const pointerSensitivity = 1.35;
const wheelSensitivity = 1.1;
const zoomDistanceThreshold = 3;
const pointerSmoothingWeight = 0.35;

export const defaultTrackpadSettings: TrackpadSettings = {
  verticalScroll: true,
  horizontalScroll: true,
  scrollDirection: "normal",
  pointerSpeed: 100,
  pointerSmoothing: false,
  pointerAcceleration: false,
  scrollAcceleration: false,
  tapToClick: true,
  zoomGestures: false,
  showVolumeControl: true,
  enableSplitMode: false,
  hapticFeedback: false,
  leftHandedButtons: false,
  largeClickButtons: false
};

export function normalizeTrackpadSettings(value: Partial<TrackpadSettings>): TrackpadSettings {
  return {
    verticalScroll: typeof value.verticalScroll === "boolean" ? value.verticalScroll : defaultTrackpadSettings.verticalScroll,
    horizontalScroll: typeof value.horizontalScroll === "boolean" ? value.horizontalScroll : defaultTrackpadSettings.horizontalScroll,
    scrollDirection: value.scrollDirection === "inverted" ? "inverted" : "normal",
    pointerSpeed: typeof value.pointerSpeed === "number" ? Math.max(10, Math.min(100, value.pointerSpeed)) : defaultTrackpadSettings.pointerSpeed,
    pointerSmoothing: typeof value.pointerSmoothing === "boolean" ? value.pointerSmoothing : defaultTrackpadSettings.pointerSmoothing,
    pointerAcceleration: typeof value.pointerAcceleration === "boolean" ? value.pointerAcceleration : defaultTrackpadSettings.pointerAcceleration,
    scrollAcceleration: typeof value.scrollAcceleration === "boolean" ? value.scrollAcceleration : defaultTrackpadSettings.scrollAcceleration,
    tapToClick: typeof value.tapToClick === "boolean" ? value.tapToClick : defaultTrackpadSettings.tapToClick,
    zoomGestures: typeof value.zoomGestures === "boolean" ? value.zoomGestures : defaultTrackpadSettings.zoomGestures,
    showVolumeControl: typeof value.showVolumeControl === "boolean" ? value.showVolumeControl : defaultTrackpadSettings.showVolumeControl,
    enableSplitMode: typeof value.enableSplitMode === "boolean" ? value.enableSplitMode : defaultTrackpadSettings.enableSplitMode,
    hapticFeedback: typeof value.hapticFeedback === "boolean" ? value.hapticFeedback : defaultTrackpadSettings.hapticFeedback,
    leftHandedButtons: typeof value.leftHandedButtons === "boolean" ? value.leftHandedButtons : defaultTrackpadSettings.leftHandedButtons,
    largeClickButtons: typeof value.largeClickButtons === "boolean" ? value.largeClickButtons : defaultTrackpadSettings.largeClickButtons
  };
}

export class GestureRecognizer {
  private mode: Mode = "idle";
  private startTime = 0;
  private startPoints: TouchPoint[] = [];
  private lastPoints: TouchPoint[] = [];
  private maxDistance = 0;
  private smoothedPointerDelta: { dx: number; dy: number } | null = null;

  start(points: TouchPoint[], time: number): void {
    this.startTime = time;
    this.startPoints = clonePoints(points);
    this.lastPoints = clonePoints(points);
    this.maxDistance = 0;
    this.smoothedPointerDelta = null;
    this.mode = points.length === 1 ? "pointer" : points.length === 2 ? "twoFinger" : "cancelled";
  }

  cancel(): void {
    this.mode = "idle";
    this.smoothedPointerDelta = null;
  }

  move(points: TouchPoint[], _time: number, settings: TrackpadSettings = defaultTrackpadSettings): GestureOutput[] {
    if (this.mode === "cancelled" || this.mode === "idle") {
      return [];
    }

    if (this.mode === "pointer") {
      if (points.length !== 1 || this.lastPoints.length !== 1) {
        this.mode = "cancelled";
        return [];
      }

      const current = points[0];
      const previous = this.lastPoints[0];
      this.maxDistance = Math.max(this.maxDistance, distance(current, this.startPoints[0]));
      this.lastPoints = clonePoints(points);

      let dx = current.x - previous.x;
      let dy = current.y - previous.y;
      if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
        return [];
      }

      if (settings.pointerAcceleration) {
        const factor = accelerationFactor(Math.hypot(dx, dy), 28, 0.45);
        dx *= factor;
        dy *= factor;
      }

      if (settings.pointerSmoothing) {
        const smoothed = smoothDelta(dx, dy, this.smoothedPointerDelta);
        this.smoothedPointerDelta = smoothed;
        dx = smoothed.dx;
        dy = smoothed.dy;
      }

      return [
        {
          type: "pointer.move",
          dx: round(dx * pointerSensitivity * speedFactor(settings.pointerSpeed)),
          dy: round(dy * pointerSensitivity * speedFactor(settings.pointerSpeed))
        }
      ];
    }

    if (this.mode === "twoFinger") {
      if (points.length !== 2 || this.lastPoints.length !== 2) {
        this.mode = "cancelled";
        return [];
      }

      const currentCenter = center(points);
      const previousCenter = center(this.lastPoints);
      const startCenter = center(this.startPoints);
      const currentSpan = span(points);
      const previousSpan = span(this.lastPoints);
      this.maxDistance = Math.max(this.maxDistance, distance(currentCenter, startCenter));
      this.lastPoints = clonePoints(points);

      let dx = currentCenter.x - previousCenter.x;
      let dy = currentCenter.y - previousCenter.y;
      const spanDelta = currentSpan - previousSpan;

      if (settings.zoomGestures && Math.abs(spanDelta) >= zoomDistanceThreshold && Math.abs(spanDelta) > Math.max(Math.abs(dx), Math.abs(dy)) * 0.5) {
        return [{ type: "pointer.zoom", direction: spanDelta > 0 ? "in" : "out" }];
      }

      if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
        return [];
      }

      if (settings.scrollAcceleration) {
        const factor = accelerationFactor(Math.hypot(dx, dy), 32, 0.7);
        dx *= factor;
        dy *= factor;
      }

      const direction = settings.scrollDirection === "inverted" ? 1 : -1;
      const wheelDx = settings.horizontalScroll ? round(dx * wheelSensitivity * direction) : 0;
      const wheelDy = settings.verticalScroll ? round(dy * wheelSensitivity * direction) : 0;

      return wheelDx === 0 && wheelDy === 0 ? [] : [{ type: "pointer.wheel", dx: wheelDx, dy: wheelDy }];
    }

    return [];
  }

  end(time: number, settings: TrackpadSettings = defaultTrackpadSettings): GestureOutput[] {
    const duration = time - this.startTime;
    const mode = this.mode;
    const maxDistance = this.maxDistance;
    this.mode = "idle";

    if (settings.tapToClick && mode === "pointer" && duration <= tapMs && maxDistance <= tapDistance) {
      return [{ type: "pointer.button", button: "left", action: "click" }];
    }

    if (mode === "pointer" && duration >= longPressMs && maxDistance <= tapDistance) {
      return [{ type: "pointer.button", button: "right", action: "click" }];
    }

    if (mode === "twoFinger" && duration <= tapMs && maxDistance <= scrollTapDistance) {
      return [{ type: "pointer.button", button: "right", action: "click" }];
    }

    return [];
  }
}

export function touchesFromList(touches: ArrayLike<{ identifier: number; clientX: number; clientY: number }>): TouchPoint[] {
  return Array.from({ length: touches.length }, (_, index) => touches[index]).map((touch) => ({
    id: touch.identifier,
    x: touch.clientX,
    y: touch.clientY
  }));
}

function clonePoints(points: TouchPoint[]): TouchPoint[] {
  return points.map((point) => ({ ...point }));
}

function center(points: TouchPoint[]): TouchPoint {
  return {
    id: -1,
    x: points.reduce((sum, point) => sum + point.x, 0) / points.length,
    y: points.reduce((sum, point) => sum + point.y, 0) / points.length
  };
}

function span(points: TouchPoint[]): number {
  return distance(points[0], points[1]);
}

function distance(a: TouchPoint, b: TouchPoint): number {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

function round(value: number): number {
  const rounded = Math.round(value * 100) / 100;
  return Object.is(rounded, -0) ? 0 : rounded;
}

function speedFactor(pointerSpeed: number): number {
  return Math.max(10, Math.min(100, pointerSpeed)) / 100;
}

function accelerationFactor(distance: number, fullSpeedDistance: number, maximumBoost: number): number {
  return 1 + Math.min(distance / fullSpeedDistance, 1) * maximumBoost;
}

function smoothDelta(dx: number, dy: number, previous: { dx: number; dy: number } | null): { dx: number; dy: number } {
  if (!previous) {
    return { dx, dy };
  }

  return {
    dx: previous.dx * (1 - pointerSmoothingWeight) + dx * pointerSmoothingWeight,
    dy: previous.dy * (1 - pointerSmoothingWeight) + dy * pointerSmoothingWeight
  };
}
