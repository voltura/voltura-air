import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("cached accent bootstrap uses the accent foreground role", async () => {
  const html = await readFile(new URL("../../apps/mobile-web/index.html", import.meta.url), "utf8");

  assert.match(html, /style\.setProperty\("--accent-contrast", palette\.onAccent\);/u);
});
