import { performance } from "node:perf_hooks";

import { formatDuration } from "./release-progress.mjs";

const ansi = {
  cyan: "\u001b[36m",
  dim: "\u001b[2m",
  green: "\u001b[32m",
  reset: "\u001b[0m",
  yellow: "\u001b[33m"
};

function createPainter(enabled) {
  return (color, text) => enabled ? `${ansi[color]}${text}${ansi.reset}` : text;
}

export function createDevProgress({
  totalSteps,
  stream = process.stdout,
  clock = () => performance.now(),
  useColor = Boolean(process.stdout.isTTY && !process.env.NO_COLOR)
}) {
  const paint = createPainter(useColor);
  const startedAt = clock();
  let currentStep = 0;
  let stepStartedAt = startedAt;

  const write = (text = "") => stream.write(`${text}\n`);

  return {
    start(title, detail) {
      currentStep += 1;
      stepStartedAt = clock();
      write();
      write(paint("cyan", "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"));
      write(paint("yellow", `Performing step ${currentStep} out of ${totalSteps}: ${title}`));
      if (detail) {
        write(`  ${detail}`);
      }
      write(paint("dim", `  Total elapsed: ${formatDuration(clock() - startedAt)}`));
      write(paint("cyan", "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"));
    },

    complete() {
      write(paint("green", `✓ Step ${currentStep} completed in ${formatDuration(clock() - stepStartedAt)}`));
    }
  };
}

export function writeDevReady({
  stepDurationMilliseconds,
  totalDurationMilliseconds,
  stream = process.stdout,
  useColor = Boolean(process.stdout.isTTY && !process.env.NO_COLOR)
}) {
  const paint = createPainter(useColor);
  stream.write(`${paint("green", `✓ Step 3 completed in ${formatDuration(stepDurationMilliseconds)}`)}\n`);
  stream.write(`${paint("green", `Development host ready in ${formatDuration(totalDurationMilliseconds)} total.`)}\n`);
}
