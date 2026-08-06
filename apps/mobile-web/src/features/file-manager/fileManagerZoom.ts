export interface FileManagerTransform { scale: number; x: number; y: number; }
export interface FileManagerPinchStart { distance: number; midpointX: number; midpointY: number; transform: FileManagerTransform; }
export const identityFileManagerTransform: FileManagerTransform = { scale: 1, x: 0, y: 0 };

export function clampFileManagerTransform(transform: FileManagerTransform, width: number, height: number): FileManagerTransform {
  const scale = Math.min(5, Math.max(1, transform.scale));
  if (scale <= 1.01) {return identityFileManagerTransform;}
  return {
    scale,
    x: Math.min(0, Math.max(width * (1 - scale), transform.x)),
    y: Math.min(0, Math.max(height * (1 - scale), transform.y))
  };
}

export function updateFileManagerPinch(
  start: FileManagerPinchStart,
  distance: number,
  midpointX: number,
  midpointY: number,
  width: number,
  height: number
): FileManagerTransform {
  const scale = Math.min(5, Math.max(1, start.transform.scale * distance / Math.max(1, start.distance)));
  if (scale <= 1.01) {return identityFileManagerTransform;}
  const contentX = (start.midpointX - start.transform.x) / start.transform.scale;
  const contentY = (start.midpointY - start.transform.y) / start.transform.scale;
  return clampFileManagerTransform({
    scale,
    x: midpointX - contentX * scale,
    y: midpointY - contentY * scale
  }, width, height);
}

export function fileTouchPair(touches: ArrayLike<{ clientX: number; clientY: number }>, left: number, top: number) {
  const first = touches[0]; const second = touches[1];
  if (!first || !second) {return null;}
  return {
    distance: Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY),
    midpointX: (first.clientX + second.clientX) / 2 - left,
    midpointY: (first.clientY + second.clientY) / 2 - top
  };
}
