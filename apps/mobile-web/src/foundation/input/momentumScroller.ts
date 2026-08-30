export type ScrollAxis = "horizontal" | "vertical";

interface MomentumDependencies {
  cancelFrame?: (frame: number) => void;
  now?: () => number;
  requestFrame?: (callback: FrameRequestCallback) => number;
}

const minimumStartVelocity = 0.08;
const minimumContinueVelocity = 0.015;
const maximumVelocity = 3;
const maximumSampleMilliseconds = 100;
const maximumFrameMilliseconds = 32;
const maximumFrameGapMilliseconds = 100;
const decayMilliseconds = 325;

export class MomentumScroller {
  private axis: ScrollAxis | null = null;
  private velocity = 0;
  private lastSampleTime = 0;
  private lastFrameTime = 0;
  private frame: number | null = null;
  private readonly cancelFrame: (frame: number) => void;
  private readonly now: () => number;
  private readonly requestFrame: (callback: FrameRequestCallback) => number;

  constructor(
    private readonly scroll: (axis: ScrollAxis, distance: number) => void,
    dependencies: MomentumDependencies = {},
  ) {
    this.cancelFrame = dependencies.cancelFrame ?? window.cancelAnimationFrame.bind(window);
    this.now = dependencies.now ?? performance.now.bind(performance);
    this.requestFrame = dependencies.requestFrame ?? window.requestAnimationFrame.bind(window);
  }

  begin() {
    this.stop();
    this.lastSampleTime = this.now();
  }

  move(axis: ScrollAxis, distance: number) {
    const time = this.now();
    const elapsed = time - this.lastSampleTime;
    if (this.axis !== axis) {
      this.axis = axis;
      this.velocity = 0;
    }
    this.scroll(axis, distance);
    if (elapsed > 0 && elapsed <= maximumSampleMilliseconds) {
      const sampleVelocity = Math.max(
        -maximumVelocity,
        Math.min(maximumVelocity, distance / elapsed),
      );
      this.velocity = this.velocity * 0.2 + sampleVelocity * 0.8;
    } else {
      this.velocity = 0;
    }
    this.lastSampleTime = time;
  }

  end() {
    const time = this.now();
    if (
      this.axis === null ||
      time - this.lastSampleTime > maximumSampleMilliseconds ||
      Math.abs(this.velocity) < minimumStartVelocity
    ) {
      this.stop();
      return;
    }
    this.lastFrameTime = time;
    this.frame = this.requestFrame(this.tick);
  }

  stop() {
    if (this.frame !== null) {
      this.cancelFrame(this.frame);
    }
    this.frame = null;
    this.axis = null;
    this.velocity = 0;
  }

  private readonly tick = (time: number) => {
    this.frame = null;
    const axis = this.axis;
    if (axis === null) {
      return;
    }
    const frameGap = time - this.lastFrameTime;
    if (frameGap < 0 || frameGap > maximumFrameGapMilliseconds) {
      this.stop();
      return;
    }
    const elapsed = Math.min(maximumFrameMilliseconds, frameGap);
    this.lastFrameTime = time;
    if (elapsed > 0) {
      this.scroll(axis, this.velocity * elapsed);
      this.velocity *= Math.exp(-elapsed / decayMilliseconds);
    }
    if (Math.abs(this.velocity) < minimumContinueVelocity) {
      this.stop();
      return;
    }
    this.frame = this.requestFrame(this.tick);
  };
}
