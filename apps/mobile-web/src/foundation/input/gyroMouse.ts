export type GyroAvailability = "ready" | "insecure" | "missing-api" | "denied" | "no-data";

export interface GyroActivationRequest {
  id: number;
  permission: Promise<boolean>;
}

type PermissionResult = "granted" | "denied";
interface PermissionConstructor { requestPermission?: () => Promise<PermissionResult> }
type RotationRateConvention = "current" | "legacy";

const POINTER_PIXELS_PER_DEGREE = 24;

export function requestGyroPermission(): Promise<boolean> {
  if (!window.isSecureContext) {
    return Promise.resolve(false);
  }
  const motion = window.DeviceMotionEvent as unknown as PermissionConstructor | undefined;
  const orientation = window.DeviceOrientationEvent as unknown as PermissionConstructor | undefined;
  if (!motion && !orientation) {
    return Promise.resolve(false);
  }

  // Invoke both before awaiting either so iOS observes the same user activation.
  const requests = [invokePermission(motion), invokePermission(orientation)].filter(
    (request): request is Promise<PermissionResult> => request !== undefined
  );
  return Promise.all(requests.map(async (request) => {
    try { return await request; } catch { return "denied" as const; }
  })).then((results) => results.every((result) => result === "granted"));
}

export function getGyroInitialAvailability(): GyroAvailability {
  if (!window.isSecureContext) {return "insecure";}
  if (typeof window.DeviceMotionEvent === "undefined" && typeof window.DeviceOrientationEvent === "undefined") {return "missing-api";}
  return "ready";
}

export interface GyroDelta { dx: number; dy: number }

export class GyroMotionProcessor {
  private motionBias = { x: 0, y: 0 };
  private motionSmoothed = { x: 0, y: 0 };
  private orientationSmoothed = { x: 0, y: 0 };
  private lastTime: number | null = null;
  private lastOrientation: Quaternion | null = null;
  private lastOrientationTime: number | null = null;
  private lastMotionRate: { alpha: number; beta: number; gamma: number; time: number } | null = null;
  private rotationConvention: RotationRateConvention = "current";
  private rotationConventionLocked = false;
  private conventionScores = { current: 0, legacy: 0, samples: 0 };
  private idleMotionCandidate: { x: number; y: number; samples: number } | null = null;
  private recentOrientationRate: { magnitude: number; time: number } | null = null;

  reset(): void {
    this.motionSmoothed = { x: 0, y: 0 };
    this.orientationSmoothed = { x: 0, y: 0 };
    this.lastTime = null;
    this.lastOrientation = null;
    this.lastOrientationTime = null;
  }

  resetMapping(): void {
    this.reset();
    this.motionBias = { x: 0, y: 0 };
    this.idleMotionCandidate = null;
    this.recentOrientationRate = null;
  }

  resetAll(): void {
    this.resetMapping();
    this.lastMotionRate = null;
    this.rotationConvention = "current";
    this.conventionScores = { current: 0, legacy: 0, samples: 0 };
    this.rotationConventionLocked = false;
  }

  setRotationRateConvention(convention: RotationRateConvention, locked = false): void {
    if (convention !== this.rotationConvention) {
      this.rotationConvention = convention;
      this.resetMapping();
    }
    this.rotationConventionLocked = locked;
  }

  private learnBias(bias: { x: number; y: number }, x: number, y: number): void {
    if (!Number.isFinite(x) || !Number.isFinite(y)) {return;}
    bias.x = bias.x * 0.9 + x * 0.1;
    bias.y = bias.y * 0.9 + y * 0.1;
  }

  motion(event: DeviceMotionEvent, _screenAngle: number, sensitivity: number, engaged: boolean): GyroDelta | null {
    const rate = event.rotationRate;
    if (!rate || !Number.isFinite(event.timeStamp)) {return null;}
    // Current Device Motion uses x/y/z; older WebKit exposes z/x/y. Nearby
    // orientation deltas calibrate the convention without user-agent sniffing.
    const deviceX = this.rotationConvention === "current" ? rate.alpha : rate.beta;
    const deviceZ = this.rotationConvention === "current" ? rate.gamma : rate.alpha;
    if (!finite(deviceX) || !finite(deviceZ)) {return null;}
    this.lastMotionRate = {
      alpha: finite(rate.alpha) ? rate.alpha ?? 0 : 0,
      beta: finite(rate.beta) ? rate.beta ?? 0 : 0,
      gamma: finite(rate.gamma) ? rate.gamma ?? 0 : 0,
      time: event.timeStamp
    };
    // Cursor axes follow the physical top edge, independent of UI orientation.
    // Gravity determines which gyro axes mean aim-right and aim-up after roll.
    const raw = topEdgeAimRates(deviceX, deviceZ, event.accelerationIncludingGravity);
    if (!engaged) {
      this.observeIdleMotion(raw.x, raw.y, event.timeStamp);
      this.lastTime = event.timeStamp;
      return { dx: 0, dy: 0 };
    }
    return this.integrate(raw.x, raw.y, event.timeStamp, sensitivity, this.motionBias);
  }

  orientation(event: DeviceOrientationEvent, _screenAngle: number, sensitivity: number, engaged: boolean, outputEnabled = true): GyroDelta | null {
    if (!finite(event.alpha) || !finite(event.beta) || !finite(event.gamma) || !Number.isFinite(event.timeStamp)) {return null;}
    const current = orientationQuaternion(event.alpha ?? 0, event.beta ?? 0, event.gamma ?? 0);
    const previous = this.lastOrientation;
    const previousTime = this.lastOrientationTime;
    this.lastOrientation = current;
    this.lastOrientationTime = event.timeStamp;
    if (!previous) {return { dx: 0, dy: 0 };}
    const elapsedMs = previousTime === null ? 0 : event.timeStamp - previousTime;
    if (elapsedMs <= 0 || elapsedMs > 1000) {
      this.recentOrientationRate = null;
      return { dx: 0, dy: 0 };
    }
    const rotation = quaternionRotationVector(multiplyQuaternion(conjugateQuaternion(previous), current));
    this.calibrateRotationConvention(rotation, previousTime, event.timeStamp);
    this.recentOrientationRate = {
      magnitude: Math.hypot(rotation.x, rotation.y, rotation.z) / (elapsedMs / 1000),
      time: event.timeStamp
    };
    const previousAim = aimAngles(previous);
    const currentAim = aimAngles(current);
    const aimDelta = clampVectorMagnitude({
      x: shortestAngle(currentAim.yaw - previousAim.yaw),
      y: currentAim.elevation - previousAim.elevation,
      z: 0
    }, 12);
    const delta = { x: aimDelta.x, y: -aimDelta.y };
    if (!engaged || !outputEnabled) {
      return { dx: 0, dy: 0 };
    }
    return this.filterDelta(delta.x, delta.y, sensitivity * POINTER_PIXELS_PER_DEGREE, this.orientationSmoothed);
  }

  private integrate(x: number, y: number, time: number, sensitivity: number, bias: { x: number; y: number }): GyroDelta {
    const elapsed = this.lastTime === null ? 0 : Math.min(50, Math.max(0, time - this.lastTime));
    this.lastTime = time;
    return this.filterDelta(
      (x - bias.x) * elapsed / 1000,
      (y - bias.y) * elapsed / 1000,
      sensitivity * POINTER_PIXELS_PER_DEGREE,
      this.motionSmoothed
    );
  }

  private filterDelta(x: number, y: number, scale: number, smoothed: { x: number; y: number }): GyroDelta {
    const magnitude = Math.hypot(x, y);
    const deadZone = Math.min(0.35, 0.045 + magnitude * 0.08);
    const factor = magnitude <= deadZone ? 0 : (magnitude - deadZone) / magnitude;
    const nextX = x * factor;
    const nextY = y * factor;
    smoothed.x = smoothed.x * 0.45 + nextX * 0.55;
    smoothed.y = smoothed.y * 0.45 + nextY * 0.55;
    return {
      dx: clamp(smoothed.x * scale, -120, 120),
      dy: clamp(smoothed.y * scale, -120, 120)
    };
  }

  private observeIdleMotion(x: number, y: number, time: number): void {
    const orientationShowsStationary = !this.recentOrientationRate ||
      time - this.recentOrientationRate.time > 100 || this.recentOrientationRate.magnitude < 0.5;
    const candidate = this.idleMotionCandidate;
    if (!orientationShowsStationary || Math.hypot(x, y) > 2 ||
        (candidate && Math.hypot(x - candidate.x, y - candidate.y) > 0.2)) {
      this.idleMotionCandidate = null;
      return;
    }
    const samples = (candidate?.samples ?? 0) + 1;
    this.idleMotionCandidate = { x, y, samples };
    if (samples >= 8) {
      this.learnBias(this.motionBias, x, y);
    }
  }

  private calibrateRotationConvention(rotation: { x: number; y: number; z: number }, previousTime: number | null, time: number): void {
    if (this.rotationConventionLocked) {
      return;
    }
    const motion = this.lastMotionRate;
    if (!motion || previousTime === null || Math.abs(time - motion.time) > 100) {
      return;
    }
    const elapsed = Math.max(0.001, (time - previousTime) / 1000);
    const measured = { x: rotation.x / elapsed, y: rotation.y / elapsed, z: rotation.z / elapsed };
    if (Math.hypot(measured.x, measured.y, measured.z) < 3) {
      return;
    }
    this.conventionScores.current += axisConventionError(measured, { x: motion.alpha, y: motion.beta, z: motion.gamma });
    this.conventionScores.legacy += axisConventionError(measured, { x: motion.beta, y: motion.gamma, z: motion.alpha });
    this.conventionScores.samples += 1;
    if (this.conventionScores.samples >= 2) {
      const nextConvention = this.conventionScores.legacy < this.conventionScores.current ? "legacy" : "current";
      if (nextConvention !== this.rotationConvention) {
        this.rotationConvention = nextConvention;
        this.motionBias = { x: 0, y: 0 };
        this.motionSmoothed = { x: 0, y: 0 };
        this.lastTime = null;
        this.idleMotionCandidate = null;
      }
    }
  }
}

interface Quaternion { w: number; x: number; y: number; z: number }

function orientationQuaternion(alpha: number, beta: number, gamma: number): Quaternion {
  const halfZ = alpha * Math.PI / 360;
  const halfX = beta * Math.PI / 360;
  const halfY = gamma * Math.PI / 360;
  const z = { w: Math.cos(halfZ), x: 0, y: 0, z: Math.sin(halfZ) };
  const x = { w: Math.cos(halfX), x: Math.sin(halfX), y: 0, z: 0 };
  const y = { w: Math.cos(halfY), x: 0, y: Math.sin(halfY), z: 0 };
  return multiplyQuaternion(multiplyQuaternion(z, x), y);
}

function multiplyQuaternion(a: Quaternion, b: Quaternion): Quaternion {
  return {
    w: a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
    x: a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
    y: a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
    z: a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w
  };
}

function conjugateQuaternion(value: Quaternion): Quaternion {
  return { w: value.w, x: -value.x, y: -value.y, z: -value.z };
}

function aimAngles(orientation: Quaternion): { yaw: number; elevation: number } {
  const aim = rotateVector(orientation, { x: 0, y: 1, z: 0 });
  return {
    yaw: Math.atan2(aim.x, aim.y) * 180 / Math.PI,
    elevation: Math.atan2(aim.z, Math.hypot(aim.x, aim.y)) * 180 / Math.PI
  };
}

function rotateVector(orientation: Quaternion, vector: { x: number; y: number; z: number }): { x: number; y: number; z: number } {
  const rotated = multiplyQuaternion(
    multiplyQuaternion(orientation, { w: 0, ...vector }),
    conjugateQuaternion(orientation)
  );
  return { x: rotated.x, y: rotated.y, z: rotated.z };
}

function quaternionRotationVector(value: Quaternion): { x: number; y: number; z: number } {
  const sign = value.w < 0 ? -1 : 1;
  const w = Math.max(-1, Math.min(1, value.w * sign));
  const angle = 2 * Math.acos(w);
  const denominator = Math.sqrt(Math.max(0, 1 - w * w));
  if (denominator < 0.000001) {
    return { x: value.x * sign * 2, y: value.y * sign * 2, z: value.z * sign * 2 };
  }
  const scale = angle / denominator * 180 / Math.PI * sign;
  return { x: value.x * scale, y: value.y * scale, z: value.z * scale };
}

function finite(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function topEdgeAimRates(
  x: number,
  z: number,
  gravity: DeviceMotionEventAcceleration | null
): { x: number; y: number } {
  const gravityX = finite(gravity?.x) ? gravity?.x ?? 0 : 0;
  const gravityY = finite(gravity?.y) ? gravity?.y ?? 0 : 0;
  const gravityZ = finite(gravity?.z) ? gravity?.z ?? 0 : 0;
  const magnitude = Math.hypot(gravityX, gravityY, gravityZ);
  const up = magnitude < 2
    ? { x: 0, z: 1 }
    : { x: gravityX / magnitude, z: gravityZ / magnitude };
  const tangentMagnitude = Math.hypot(up.x, up.z);
  if (tangentMagnitude < 0.15) {
    // The physical top edge is nearly vertical, outside the supported pointing
    // posture. Keep the last useful remote-style basis instead of amplifying noise.
    return { x: -z, y: -x };
  }
  const upX = up.x / tangentMagnitude;
  const upZ = up.z / tangentMagnitude;
  // d(+Y)/dt = angularVelocity × +Y = (-z, 0, x).
  // right = +Y × up = (upZ, 0, -upX).
  const derivativeX = -z;
  const derivativeZ = x;
  const aimRight = derivativeX * upZ - derivativeZ * upX;
  const aimUp = derivativeX * upX + derivativeZ * upZ;
  // rotationRate signs are inverse to the physical aim change represented by
  // these top-edge tangent vectors.
  return { x: -aimRight, y: aimUp };
}

function invokePermission(constructor: PermissionConstructor | undefined): Promise<PermissionResult> | undefined {
  if (!constructor?.requestPermission) {
    return undefined;
  }
  try {
    return Promise.resolve(constructor.requestPermission());
  } catch {
    return Promise.resolve("denied");
  }
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.max(minimum, Math.min(maximum, value));
}

function shortestAngle(value: number): number {
  return ((value + 180) % 360 + 360) % 360 - 180;
}

function vectorError(a: { x: number; y: number; z: number }, b: { x: number; y: number; z: number }): number {
  return Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
}

function axisConventionError(a: { x: number; y: number; z: number }, b: { x: number; y: number; z: number }): number {
  return Math.min(vectorError(a, b), vectorError(a, { x: -b.x, y: -b.y, z: -b.z }));
}

function clampVectorMagnitude(vector: { x: number; y: number; z: number }, maximum: number): { x: number; y: number; z: number } {
  const magnitude = Math.hypot(vector.x, vector.y, vector.z);
  if (magnitude <= maximum || magnitude === 0) {
    return vector;
  }
  const scale = maximum / magnitude;
  return { x: vector.x * scale, y: vector.y * scale, z: vector.z * scale };
}
