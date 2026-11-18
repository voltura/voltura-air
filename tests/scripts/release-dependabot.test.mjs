import assert from "node:assert/strict";
import test from "node:test";
import { checkReleaseDependabot } from "../../scripts/check-release-dependabot.mjs";

const repository = "voltura/voltura-air";

test("release ignores pull requests opened by people", () => {
  checkReleaseDependabot(repository, (args) => {
    assert.deepEqual(args, [
      "api",
      "--paginate",
      "--slurp",
      `repos/${repository}/pulls?state=open&base=main&per_page=100`,
    ]);
    return JSON.stringify([[{ number: 1, user: { login: "contributor" } }]]);
  });
  checkReleaseDependabot(repository, () => "[[]]");
});

test("release reports every matching Dependabot pull request across pages", () => {
  const pulls = [
    [{ number: 85, user: { login: "dependabot[bot]" } }],
    [{ number: 86, user: { login: "dependabot[bot]" }, draft: true }],
  ];
  assert.throws(() => checkReleaseDependabot(repository, () => JSON.stringify(pulls)), {
    message: `Resolve the open Dependabot pull requests targeting ${repository} main before releasing:\nhttps://github.com/${repository}/pull/85\nhttps://github.com/${repository}/pull/86`,
  });
});

test("release stops when GitHub cannot verify the pull requests", () => {
  assert.throws(
    () =>
      checkReleaseDependabot(repository, () => {
        throw new Error("unavailable");
      }),
    /Could not check open Dependabot/u,
  );
  assert.throws(
    () => checkReleaseDependabot(repository, () => "{}"),
    /unexpected pull request response/u,
  );
});
