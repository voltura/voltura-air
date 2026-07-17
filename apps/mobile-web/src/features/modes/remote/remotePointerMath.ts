export function roundRemoteDelta(value: number): number {
  const rounded = Math.round(value * 100) / 100;
  return Object.is(rounded, -0) ? 0 : rounded;
}

export function isInteractiveRemoteTarget(target: EventTarget): boolean {
  return target instanceof Element && target.closest("button, a, input, textarea, select, [role='button']") !== null;
}
