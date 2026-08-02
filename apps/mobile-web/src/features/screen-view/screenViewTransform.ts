export interface ScreenViewTransform {
  scale: number;
  x: number;
  y: number;
}

export interface ScreenViewPinchStart {
  distance: number;
  midpointX: number;
  midpointY: number;
  transform: ScreenViewTransform;
}

export const identityScreenViewTransform: ScreenViewTransform = { scale: 1, x: 0, y: 0 };
export const maxScreenViewScale = 5;

export function updateScreenViewPinch(
  start: ScreenViewPinchStart,
  distance: number,
  midpointX: number,
  midpointY: number,
  viewportWidth: number,
  viewportHeight: number
): ScreenViewTransform {
  const scale = Math.min(maxScreenViewScale, Math.max(1, start.transform.scale * distance / Math.max(1, start.distance)));
  if (scale <= 1.01) {return identityScreenViewTransform;}

  const contentX = (start.midpointX - start.transform.x) / start.transform.scale;
  const contentY = (start.midpointY - start.transform.y) / start.transform.scale;
  const x = midpointX - contentX * scale;
  const y = midpointY - contentY * scale;
  return {
    scale,
    x: Math.min(0, Math.max(viewportWidth * (1 - scale), x)),
    y: Math.min(0, Math.max(viewportHeight * (1 - scale), y))
  };
}

export function touchPairGeometry(touches: ArrayLike<{ clientX: number; clientY: number }>, left: number, top: number) {
  const first = touches[0]; const second = touches[1];
  if (!first || !second) {return null;}
  const dx = second.clientX - first.clientX; const dy = second.clientY - first.clientY;
  return {
    distance: Math.hypot(dx, dy),
    midpointX: (first.clientX + second.clientX) / 2 - left,
    midpointY: (first.clientY + second.clientY) / 2 - top
  };
}
