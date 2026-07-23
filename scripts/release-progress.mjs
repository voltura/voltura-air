import { performance } from "node:perf_hooks";

const ansi = {
  cyan: "\u001b[36m",
  dim: "\u001b[2m",
  green: "\u001b[32m",
  red: "\u001b[31m",
  reset: "\u001b[0m",
  yellow: "\u001b[33m"
};

export function formatDuration(milliseconds) {
  const totalSeconds = Math.max(0, Math.round(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (hours > 0) {
    return `${hours}h ${String(minutes).padStart(2, "0")}m ${String(seconds).padStart(2, "0")}s`;
  }
  if (minutes > 0) {
    return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
  }
  return `${seconds}s`;
}

function createPainter(enabled) {
  return (color, text) => enabled ? `${ansi[color]}${text}${ansi.reset}` : text;
}

export function createReleaseProgress({
  totalSteps,
  stream = process.stdout,
  clock = () => performance.now(),
  useColor = Boolean(process.stdout.isTTY && !process.env.NO_COLOR)
}) {
  const paint = createPainter(useColor);
  const releaseStartedAt = clock();
  let currentStep = 0;
  let currentTitle = "";
  let stepStartedAt = releaseStartedAt;

  const write = (text = "") => stream.write(`${text}\n`);
  const elapsed = () => formatDuration(clock() - releaseStartedAt);

  return {
    start(title, detail) {
      currentStep += 1;
      currentTitle = title;
      stepStartedAt = clock();
      write();
      write(paint("cyan", "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"));
      write(paint("yellow", `Performing step ${currentStep} out of ${totalSteps}: ${title}`));
      if (detail) {
        write(`  ${detail}`);
      }
      write(paint("dim", `  Total elapsed: ${elapsed()}`));
      write(paint("cyan", "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"));
    },

    complete() {
      const stepDuration = formatDuration(clock() - stepStartedAt);
      write(paint("green", `✓ Step ${currentStep} completed in ${stepDuration}`));
    },

    success(summary) {
      const totalDuration = elapsed();
      write();
      write(paint("green", "╔══════════════════════════════════════════════════════════╗"));
      write(paint("green", "║  GREEN = SUCCESS                                         ║"));
      write(paint("green", "╚══════════════════════════════════════════════════════════╝"));
      write(summary);
      write(`Total release time: ${totalDuration}`);
    },

    issue(error) {
      const message = error instanceof Error ? error.message : String(error);
      write();
      write(paint("red", "╔══════════════════════════════════════════════════════════╗"));
      write(paint("red", "║  RED = ISSUE                                             ║"));
      write(paint("red", "╚══════════════════════════════════════════════════════════╝"));
      if (currentTitle) {
        write(`Stopped during step ${currentStep} of ${totalSteps}: ${currentTitle}`);
      }
      write(paint("red", message));
      write(`Total elapsed before issue: ${elapsed()}`);
    }
  };
}
