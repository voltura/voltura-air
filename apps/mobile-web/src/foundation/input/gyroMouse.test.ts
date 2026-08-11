import { afterEach, describe, expect, it, vi } from "vitest";
import { GyroMotionProcessor, requestGyroPermission } from "./gyroMouse";

describe("gyro mouse sensor processing", () => {
  it("rejects non-finite motion and accepts valid stationary readings", () => {
    const processor = new GyroMotionProcessor();
    expect(processor.motion(motion({ alpha: Number.NaN, beta: 0, gamma: 0 }, 10), 0, 1, true)).toBeNull();
    expect(processor.motion(motion({ alpha: 0, beta: 0, gamma: Number.NaN }, 10), 0, 1, true)).toBeNull();
    expect(processor.motion(motion({ alpha: 0, beta: 0, gamma: Number.POSITIVE_INFINITY }, 10), 0, 1, true)).toBeNull();
    expect(processor.motion(motion({ alpha: 0, beta: 0, gamma: 0 }, 10), 0, 1, false)).toEqual({ dx: 0, dy: 0 });
  });

  it("distinguishes unavailable required axes from numeric stationary zero", () => {
    const current = new GyroMotionProcessor();
    expect(current.motion(motion({ alpha: null, beta: null, gamma: null }, 10), 0, 1, true)).toBeNull();
    expect(current.motion(motion({ alpha: 0, beta: null, gamma: null }, 20), 0, 1, true)).toBeNull();
    expect(current.motion(motion({ alpha: null, beta: null, gamma: 0 }, 30), 0, 1, true)).toBeNull();
    expect(current.motion(motion({ alpha: 0, beta: null, gamma: 0 }, 40), 0, 1, false)).toEqual({ dx: 0, dy: 0 });

    const legacy = new GyroMotionProcessor();
    legacy.setRotationRateConvention("legacy", true);
    expect(legacy.motion(motion({ alpha: null, beta: null, gamma: null }, 10), 0, 1, true)).toBeNull();
    expect(legacy.motion(motion({ alpha: 0, beta: null, gamma: null }, 20), 0, 1, true)).toBeNull();
    expect(legacy.motion(motion({ alpha: null, beta: 0, gamma: null }, 30), 0, 1, true)).toBeNull();
    expect(legacy.motion(motion({ alpha: 0, beta: 0, gamma: null }, 40), 0, 1, false)).toEqual({ dx: 0, dy: 0 });
  });

  it("accepts current-convention pointing axes when the unused twist axis is unavailable", () => {
    const processor = new GyroMotionProcessor();
    processor.motion(motion({ alpha: 0, beta: Number.NaN, gamma: 30 }, 0), 0, 1, true);
    const delta = processor.motion(motion({ alpha: 0, beta: Number.NaN, gamma: 30 }, 20), 0, 1, true)!;
    expect(delta.dx).toBeGreaterThan(0);
    expect(Number.isFinite(delta.dy)).toBe(true);
  });

  it("scales movement sensitivity and clamps suspended gaps", () => {
    const normal = new GyroMotionProcessor();
    const fast = new GyroMotionProcessor();
    normal.motion(motion({ alpha: 0, beta: 0, gamma: 30 }, 0), 0, 1, true);
    fast.motion(motion({ alpha: 0, beta: 0, gamma: 30 }, 0), 0, 2, true);
    const normalDelta = normal.motion(motion({ alpha: 0, beta: 0, gamma: 30 }, 1000), 0, 1, true);
    const fastDelta = fast.motion(motion({ alpha: 0, beta: 0, gamma: 30 }, 1000), 0, 2, true);
    expect(fastDelta!.dx).toBeCloseTo(normalDelta!.dx * 2);
    expect(Math.abs(normalDelta!.dx)).toBeGreaterThan(14);
    expect(Math.abs(normalDelta!.dx)).toBeLessThan(25);
  });

  it("maps WebKit pitch up to cursor up and remote-style yaw to horizontal movement", () => {
    const pitch = new GyroMotionProcessor();
    const yaw = new GyroMotionProcessor();
    pitch.setRotationRateConvention("legacy", true);
    yaw.setRotationRateConvention("legacy", true);
    pitch.motion(motion({ alpha: 0, beta: -30, gamma: 0 }, 0), 0, 1, true);
    yaw.motion(motion({ alpha: 30, beta: 0, gamma: 0 }, 0), 0, 1, true);
    const pitchDelta = pitch.motion(motion({ alpha: 0, beta: -30, gamma: 0 }, 20), 0, 1, true)!;
    const yawDelta = yaw.motion(motion({ alpha: 30, beta: 0, gamma: 0 }, 20), 0, 1, true)!;
    expect(pitchDelta.dy).toBeLessThan(0);
    expect(Math.abs(pitchDelta.dx)).toBeLessThan(0.001);
    expect(yawDelta.dx).toBeGreaterThan(0);
    expect(Math.abs(yawDelta.dy)).toBeLessThan(0.001);
  });

  it("maps the current iPhone X/Y/Z stream to physical top-edge pointing", () => {
    const aimUp = new GyroMotionProcessor();
    const aimDown = new GyroMotionProcessor();
    const aimLeft = new GyroMotionProcessor();
    const aimRight = new GyroMotionProcessor();
    const gravity = { x: 0, y: 0, z: 9.8 };
    const cases = [
      [aimUp, { alpha: -30, beta: 0, gamma: 0 }],
      [aimDown, { alpha: 30, beta: 0, gamma: 0 }],
      [aimLeft, { alpha: 0, beta: 0, gamma: -30 }],
      [aimRight, { alpha: 0, beta: 0, gamma: 30 }]
    ] as const;
    for (const [processor, rate] of cases) {
      processor.motion(motion(rate, 0, gravity), 0, 1, true);
      processor.motion(motion(rate, 20, gravity), 0, 1, true);
    }
    const up = aimUp.motion(motion({ alpha: -30, beta: 0, gamma: 0 }, 40, gravity), 0, 1, true)!;
    const down = aimDown.motion(motion({ alpha: 30, beta: 0, gamma: 0 }, 40, gravity), 0, 1, true)!;
    const left = aimLeft.motion(motion({ alpha: 0, beta: 0, gamma: -30 }, 40, gravity), 0, 1, true)!;
    const right = aimRight.motion(motion({ alpha: 0, beta: 0, gamma: 30 }, 40, gravity), 0, 1, true)!;
    expect(up.dy).toBeLessThan(0);
    expect(Math.abs(up.dx)).toBeLessThan(0.001);
    expect(down.dy).toBeGreaterThan(0);
    expect(Math.abs(down.dx)).toBeLessThan(0.001);
    expect(left.dx).toBeLessThan(0);
    expect(Math.abs(left.dy)).toBeLessThan(0.001);
    expect(right.dx).toBeGreaterThan(0);
    expect(Math.abs(right.dy)).toBeLessThan(0.001);
  });

  it("keeps current iPhone axes when motion signs oppose orientation deltas", () => {
    const processor = new GyroMotionProcessor();
    processor.orientation(orientation(0, 0, 0, 0), 0, 1, true);
    processor.motion(motion({ alpha: -20, beta: 0, gamma: 0 }, 20), 0, 1, true);
    processor.orientation(orientation(0, 0.4, 0, 20), 0, 1, true);
    processor.motion(motion({ alpha: -20, beta: 0, gamma: 0 }, 40), 0, 1, true);
    processor.orientation(orientation(0, 0.8, 0, 40), 0, 1, true);
    processor.resetMapping();

    processor.motion(motion({ alpha: 0, beta: 0, gamma: 20 }, 60), 0, 1, true);
    const delta = processor.motion(motion({ alpha: 0, beta: 0, gamma: 20 }, 80), 0, 1, true)!;
    expect(delta.dx).toBeGreaterThan(0);
    expect(Math.abs(delta.dy)).toBeLessThan(0.001);
  });

  it("learns released bias and resets orientation fallback baselines", () => {
    const processor = new GyroMotionProcessor();
    for (let index = 0; index < 20; index += 1) {
      processor.motion(motion({ alpha: 0, beta: 0.6, gamma: 0 }, index * 16), 0, 1, false);
    }
    const biased = processor.motion(motion({ alpha: 0, beta: 0.6, gamma: 0 }, 400), 0, 1, true);
    expect(Math.abs(biased!.dx)).toBeLessThan(0.1);
    expect(processor.orientation(orientation(10, 20, 30), 0, 1, true)).toEqual({ dx: 0, dy: 0 });
    processor.reset();
    expect(processor.orientation(orientation(40, 50, 60), 0, 1, true)).toEqual({ dx: 0, dy: 0 });
  });

  it("does not learn deliberate released movement as stationary bias", () => {
    const processor = new GyroMotionProcessor();
    for (let index = 0; index < 20; index += 1) {
      processor.motion(motion({ alpha: 0, beta: 0, gamma: 4 }, index * 16), 0, 1, false);
    }
    expect(Math.abs(processor.motion(motion({ alpha: 0, beta: 0, gamma: 4 }, 400), 0, 1, true)!.dx)).toBeGreaterThan(0);
  });

  it.each([
    ["portrait", { x: 0, y: 0, z: 9.8 }, { alpha: 20, beta: 0, gamma: 0 }, { alpha: 0, beta: -20, gamma: 0 }],
    ["landscape right", { x: 9.8, y: 0, z: 0 }, { alpha: 0, beta: 20, gamma: 0 }, { alpha: 20, beta: 0, gamma: 0 }],
    ["landscape left", { x: -9.8, y: 0, z: 0 }, { alpha: 0, beta: -20, gamma: 0 }, { alpha: -20, beta: 0, gamma: 0 }]
  ])("keeps physical aim axes in %s", (_posture, gravity, aimRight, aimUp) => {
    const horizontal = new GyroMotionProcessor();
    const vertical = new GyroMotionProcessor();
    horizontal.setRotationRateConvention("legacy", true);
    vertical.setRotationRateConvention("legacy", true);
    horizontal.motion(motion(aimRight, 0, gravity), 0, 1, true);
    vertical.motion(motion(aimUp, 0, gravity), 0, 1, true);
    const horizontalDelta = horizontal.motion(motion(aimRight, 20, gravity), 0, 1, true)!;
    const verticalDelta = vertical.motion(motion(aimUp, 20, gravity), 0, 1, true)!;
    expect(horizontalDelta.dx).toBeGreaterThan(0);
    expect(Math.abs(horizontalDelta.dy)).toBeLessThan(0.001);
    expect(verticalDelta.dy).toBeLessThan(0);
    expect(Math.abs(verticalDelta.dx)).toBeLessThan(0.001);
  });

  it("keeps orientation fallback aim axes independent of UI orientation", () => {
    const fallback = new GyroMotionProcessor();
    fallback.orientation(orientation(0, 0, 0), 0, 1, true);
    const delta = fallback.orientation(orientation(-12, 0, 0, 20), 90, 1, true)!;
    expect(delta.dx).toBeGreaterThan(0);
    expect(Math.abs(delta.dy)).toBeLessThan(0.001);

    const vertical = new GyroMotionProcessor();
    vertical.orientation(orientation(0, 0, 0), 0, 1, true);
    const verticalDelta = vertical.orientation(orientation(0, 12, 0, 20), 270, 1, true)!;
    expect(verticalDelta.dy).toBeLessThan(0);
    expect(Math.abs(verticalDelta.dx)).toBeLessThan(0.001);
  });

  it("calibrates legacy WebKit rotation-rate axes from orientation deltas", () => {
    const processor = new GyroMotionProcessor();
    processor.orientation(orientation(0, 0, 0, 0), 0, 1, true);
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 20), 0, 1, true);
    processor.orientation(orientation(0.4, 0, 0, 20), 0, 1, true);
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 40), 0, 1, true);
    processor.orientation(orientation(0.8, 0, 0, 40), 0, 1, true);
    processor.resetMapping();
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 60), 0, 1, true);
    const delta = processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 80), 0, 1, true)!;
    expect(delta.dx).toBeGreaterThan(0);
  });

  it("clears axis-correlation evidence across activation sessions", () => {
    const processor = new GyroMotionProcessor();
    processor.orientation(orientation(0, 0, 0, 0), 0, 1, true);
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 20), 0, 1, true);
    processor.orientation(orientation(0.4, 0, 0, 20), 0, 1, true);
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 40), 0, 1, true);
    processor.orientation(orientation(0.8, 0, 0, 40), 0, 1, true);

    processor.resetAll();
    processor.orientation(orientation(0, 0, 0, 100), 0, 1, true);
    processor.motion(motion({ alpha: 20, beta: 0, gamma: 0 }, 120), 0, 1, true);
    processor.orientation(orientation(0.4, 0, 0, 120), 0, 1, true);
    processor.resetMapping();

    processor.motion(motion({ alpha: 0, beta: 0, gamma: 20 }, 140), 0, 1, true);
    const currentDelta = processor.motion(motion({ alpha: 0, beta: 0, gamma: 20 }, 160), 0, 1, true)!;
    expect(currentDelta.dx).toBeGreaterThan(0);
  });

  it("treats a suspended orientation gap as a new baseline", () => {
    const processor = new GyroMotionProcessor();
    processor.orientation(orientation(0, 0, 0, 0), 0, 1, true);
    expect(processor.orientation(orientation(0, 0, 45, 2000), 0, 1, true)).toEqual({ dx: 0, dy: 0 });
  });

  it("accepts low-frequency orientation fallback and isolates calibration from motion smoothing", () => {
    const fallback = new GyroMotionProcessor();
    fallback.orientation(orientation(0, 0, 0, 0), 0, 1, true);
    expect(fallback.orientation(orientation(-8, 0, 0, 100), 0, 1, true)!.dx).toBeGreaterThan(0);

    const plain = new GyroMotionProcessor();
    const calibrated = new GyroMotionProcessor();
    plain.motion(motion({ alpha: 0, beta: 20, gamma: 0 }, 0), 0, 1, true);
    calibrated.motion(motion({ alpha: 0, beta: 20, gamma: 0 }, 0), 0, 1, true);
    calibrated.orientation(orientation(0, 0, 0, 0), 0, 1, true, false);
    calibrated.orientation(orientation(0, 0, 8, 20), 0, 1, true, false);
    const plainDelta = plain.motion(motion({ alpha: 0, beta: 20, gamma: 0 }, 40), 0, 1, true)!;
    const calibratedDelta = calibrated.motion(motion({ alpha: 0, beta: 20, gamma: 0 }, 40), 0, 1, true)!;
    expect(calibratedDelta.dx).toBeCloseTo(plainDelta.dx);
  });
});

describe("gyro permission", () => {
  const originalSecureContext = window.isSecureContext;
  const originalMotion = window.DeviceMotionEvent;
  const originalOrientation = window.DeviceOrientationEvent;

  afterEach(() => {
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: originalSecureContext });
    Object.defineProperty(window, "DeviceMotionEvent", { configurable: true, value: originalMotion });
    Object.defineProperty(window, "DeviceOrientationEvent", { configurable: true, value: originalOrientation });
  });

  it("requests both browser permissions in the initiating call", async () => {
    const calls: string[] = [];
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    Object.defineProperty(window, "DeviceMotionEvent", { configurable: true, value: { requestPermission: vi.fn(() => { calls.push("motion"); return Promise.resolve("granted"); }) } });
    Object.defineProperty(window, "DeviceOrientationEvent", { configurable: true, value: { requestPermission: vi.fn(() => { calls.push("orientation"); return Promise.resolve("granted"); }) } });
    const result = requestGyroPermission();
    expect(calls).toEqual(["motion", "orientation"]);
    await expect(result).resolves.toBe(true);
  });

  it("reports denial and rejected permission promises", async () => {
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    Object.defineProperty(window, "DeviceMotionEvent", { configurable: true, value: { requestPermission: () => Promise.resolve("denied") } });
    Object.defineProperty(window, "DeviceOrientationEvent", { configurable: true, value: { requestPermission: () => Promise.reject(new Error("blocked")) } });
    await expect(requestGyroPermission()).resolves.toBe(false);
  });

  it("still requests orientation when the motion permission method throws synchronously", async () => {
    const orientationPermission = vi.fn(() => Promise.resolve("granted" as const));
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    Object.defineProperty(window, "DeviceMotionEvent", { configurable: true, value: { requestPermission: () => { throw new Error("blocked"); } } });
    Object.defineProperty(window, "DeviceOrientationEvent", { configurable: true, value: { requestPermission: orientationPermission } });
    await expect(requestGyroPermission()).resolves.toBe(false);
    expect(orientationPermission).toHaveBeenCalledOnce();
  });
});

function motion(
  rotationRate: { alpha: number | null; beta: number | null; gamma: number | null },
  timeStamp: number,
  gravity: number | { x: number; y: number; z: number } = 9.8
): DeviceMotionEvent {
  const accelerationIncludingGravity = typeof gravity === "number" ? { x: 0, y: 0, z: gravity } : gravity;
  return { accelerationIncludingGravity, rotationRate, timeStamp } as DeviceMotionEvent;
}

function orientation(alpha: number, beta: number, gamma: number, timeStamp = 0): DeviceOrientationEvent {
  return { alpha, beta, gamma, timeStamp } as DeviceOrientationEvent;
}
