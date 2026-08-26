import assert from "node:assert/strict";
import test from "node:test";

import {
  freewareNotice,
  getGeneralReleaseNotices,
  unsignedReleaseNotice,
} from "../../scripts/release-tools.mjs";

const requiredNotices = `${freewareNotice}\n\n${unsignedReleaseNotice}`;

test("general notices remove HTML comments until sanitization is stable", () => {
  const notes = `## General notices\n\n<<!-- hidden -->!-- second -->\n\n${requiredNotices}\n`;
  assert.equal(getGeneralReleaseNotices(notes), requiredNotices);
});

test("general notices reject malformed HTML comment markers", () => {
  const notes = `## General notices\n\n<!<!-- hidden -->-->\n\n${requiredNotices}\n`;
  assert.throws(() => getGeneralReleaseNotices(notes), /malformed HTML comments/u);
});
