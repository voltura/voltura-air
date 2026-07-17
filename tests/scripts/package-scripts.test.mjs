import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const packageJson = JSON.parse(readFileSync(new URL("../../package.json", import.meta.url), "utf8"));

test("clean stops the host before deleting locked build outputs", () => {
  const steps = packageJson.scripts.clean.split(" && ");

  assert.equal(steps[0], "npm run cache:purge");
  assert.ok(steps.indexOf("npm run cache:purge") < steps.indexOf("npm run clean:temp"));
});
