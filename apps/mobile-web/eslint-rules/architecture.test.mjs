import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { classify, dependencyError } from "./architecture.mjs";

const sourceRoot = path.resolve("src");
const source = (relativePath) => classify(path.join(sourceRoot, relativePath));

function dependencyMessage(sourceFile, targetFile) {
  const targetPath = path.join(sourceRoot, targetFile);
  return dependencyError(source(sourceFile), classify(targetPath), targetPath);
}

test("allows the app root to consume a feature public API", () => {
  assert.equal(dependencyMessage("App.tsx", "features/modes"), null);
});

test("rejects app imports of feature internals", () => {
  assert.match(dependencyMessage("App.tsx", "features/modes/ModeWorkspace") ?? "", /public index/u);
});

test("allows feature-local, shared UI, and target foundation dependencies", () => {
  assert.equal(
    dependencyMessage("features/modes/ModeWorkspace.tsx", "features/modes/remote/RemoteMode"),
    null,
  );
  assert.equal(
    dependencyMessage("features/modes/ModeWorkspace.tsx", "ui/overlays/InfoButton"),
    null,
  );
  assert.equal(
    dependencyMessage("features/modes/ModeWorkspace.tsx", "foundation/connection/connectionTypes"),
    null,
  );
});

test("rejects direct dependencies between feature slices", () => {
  assert.match(
    dependencyMessage("features/modes/ModeWorkspace.tsx", "features/pairing/PairingStatus") ?? "",
    /must not import private code/u,
  );
});

test("rejects foundation dependencies on features", () => {
  assert.match(
    dependencyMessage("foundation/connection/useConnection.ts", "features/modes") ?? "",
    /Foundation code/u,
  );
});

test("classifies source files outside the completed target roots as invalid", () => {
  assert.deepEqual(source("connection/useConnection.ts"), { layer: "invalid" });
  assert.deepEqual(source("foundation/protocol.ts"), { layer: "invalid" });
});

test("rejects shared UI dependencies on application foundation", () => {
  assert.match(
    dependencyMessage("ui/overlays/InfoButton.tsx", "foundation/settings/appStorage") ?? "",
    /Shared UI/u,
  );
});
