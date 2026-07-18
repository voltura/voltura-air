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

test("rejects source files outside the completed target roots", () => {
  const messages = lint(
    "connection/useConnection.ts",
    "export const connection = true;"
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /must be owned by app/u);
});

test("requires foundation files to declare a domain owner", () => {
  const messages = lint("foundation/protocol.ts", "export const version = 1;");

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /foundation\/<domain>/u);
});

test("rejects shared UI dependencies on application foundation", () => {
  const messages = lint(
    "ui/overlays/InfoButton.tsx",
    'import { saveSettings } from "../../foundation/settings/appStorage";'
  );

  assert.equal(messages.length, 1);
  assert.match(messages[0].message, /Shared UI/u);
});
