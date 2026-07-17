import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { Linter } from "eslint";
import { architectureRule } from "./architecture.mjs";

const sourceRoot = path.resolve("src");

function lint(sourceFile, code) {
  const linter = new Linter();
  return linter.verify(
    code,
    {
      files: ["**/*.{js,ts,tsx}"],
      languageOptions: {
        ecmaVersion: "latest",
        sourceType: "module"
      },
      plugins: {
        "voltura-architecture": {
          rules: {
            "dependency-direction": architectureRule
          }
        }
      },
      rules: {
        "voltura-architecture/dependency-direction": "error"
      }
    },
    { filename: path.join(sourceRoot, sourceFile) }
  );
}

test("allows the app root to consume a feature public API", () => {
  assert.deepEqual(lint("App.tsx", 'import { ModeWorkspace } from "./features/modes";'), []);
});

test("rejects app imports of feature internals", () => {
  const messages = lint(
    "App.tsx",
    'import { ModeWorkspace } from "./features/modes/ModeWorkspace";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /public index/u);
});

test("allows feature-local, shared UI, and target foundation dependencies", () => {
  assert.deepEqual(
    lint(
      "features/modes/ModeWorkspace.tsx",
      [
        'import { RemoteMode } from "./remote/RemoteMode";',
        'import { InfoButton } from "../../ui/overlays/InfoButton";',
        'import { ConnectionState } from "../../foundation/connection/connectionTypes";'
      ].join("\n")
    ),
    []
  );
});

test("rejects direct dependencies between feature slices", () => {
  const messages = lint(
    "features/modes/ModeWorkspace.tsx",
    'import { PairingStatus } from "../pairing/PairingStatus";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /must not import private code/u);
});

test("rejects foundation dependencies on features", () => {
  const messages = lint(
    "foundation/connection/useConnection.ts",
    'import { ModeWorkspace } from "../../features/modes";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /Foundation code/u);
});

test("keeps transitional root domains under foundation dependency rules", () => {
  const messages = lint(
    "connection/useConnection.ts",
    'import { ModeWorkspace } from "../features/modes";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /Foundation code/u);
});

test("rejects shared UI dependencies on application foundation", () => {
  const messages = lint(
    "ui/overlays/InfoButton.tsx",
    'import { saveSettings } from "../../appStorage";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /Shared UI/u);
});
