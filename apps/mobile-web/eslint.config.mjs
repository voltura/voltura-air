// @ts-check
import js from "@eslint/js";
import { defineConfig, globalIgnores } from "eslint/config";
import { createTypeScriptImportResolver } from "eslint-import-resolver-typescript";
import importX from "eslint-plugin-import-x";
import jsxA11y from "eslint-plugin-jsx-a11y";
import reactHooks from "eslint-plugin-react-hooks";
import reactWebApi from "eslint-plugin-react-web-api";
import globals from "globals";
import tseslint from "typescript-eslint";
import { architectureRule } from "./eslint-rules/architecture.mjs";

const sourceFiles = ["**/*.{ts,tsx}"];

export default defineConfig(
  globalIgnores(["dist/**", "coverage/**"]),
  {
    files: ["eslint-rules/**/*.mjs", "stylelint.config.mjs"],
    extends: [js.configs.recommended],
    languageOptions: {
      globals: globals.node
    },
    linterOptions: {
      noInlineConfig: true,
      reportUnusedDisableDirectives: "error"
    }
  },
  {
    files: sourceFiles,
    extends: [
      js.configs.recommended,
      tseslint.configs.recommendedTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      importX.flatConfigs.recommended,
      importX.flatConfigs.typescript,
      reactHooks.configs.flat["recommended-latest"],
      reactWebApi.configs.recommended,
      jsxA11y.flatConfigs.strict
    ],
    plugins: {
      "voltura-architecture": {
        rules: {
          "dependency-direction": architectureRule
        }
      }
    },
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.es2022
      },
      parserOptions: {
        projectService: {
          allowDefaultProject: ["*.config.ts"]
        },
        tsconfigRootDir: import.meta.dirname
      }
    },
    linterOptions: {
      noInlineConfig: true,
      reportUnusedDisableDirectives: "error"
    },
    settings: {
      "import-x/resolver-next": [createTypeScriptImportResolver({ project: "./tsconfig.json" })]
    },
    rules: {
      "@typescript-eslint/consistent-type-imports": ["error", { prefer: "type-imports" }],
      "@typescript-eslint/no-deprecated": "error",
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-misused-promises": "error",
      "@typescript-eslint/restrict-template-expressions": ["error", { allowNumber: true }],
      "@typescript-eslint/switch-exhaustiveness-check": "error",
      curly: ["error", "all"],
      eqeqeq: ["error", "always"],
      "no-console": ["error", { allow: ["warn", "error"] }],
      "no-implicit-coercion": "error",
      "prefer-object-spread": "error",
      "import-x/no-cycle": ["error", { ignoreExternal: true }],
      "react-web-api/no-leaked-event-listener": "error",
      "react-web-api/no-leaked-fetch": "error",
      "react-web-api/no-leaked-intersection-observer": "error",
      "react-web-api/no-leaked-interval": "error",
      "react-web-api/no-leaked-resize-observer": "error",
      "react-web-api/no-leaked-timeout": "error",
      "voltura-architecture/dependency-direction": "error",
      "jsx-a11y/no-noninteractive-element-interactions": ["error", { handlers: ["onClick"] }],
      "jsx-a11y/no-static-element-interactions": ["error", { handlers: ["onClick"] }]
    }
  },
  {
    files: ["**/*.test.{ts,tsx}"],
    rules: {
      "@typescript-eslint/no-non-null-assertion": "off",
      // TS7 and the TS6 compatibility API can disagree about Testing Library's
      // generic DOM narrowing. Keep explicit test element types authoritative.
      "@typescript-eslint/no-unnecessary-type-assertion": "off",
      "@typescript-eslint/unbound-method": "off"
    }
  },
  {
    // Clipboard API writes are unavailable on ordinary HTTP LAN origins. Keep
    // the narrowly isolated execCommand fallback until a standards-based API
    // supports that deployment context.
    files: ["src/foundation/diagnostics/mobileDiagnostics.ts"],
    rules: {
      "@typescript-eslint/no-deprecated": "off"
    }
  },
  {
    // These small transport-operation hooks intentionally invalidate request
    // state when their external WebSocket session ends. The effects express
    // lifecycle ownership directly; a generic wrapper would hide that behavior
    // and add indirection solely to satisfy this rule.
    files: [
      "src/foundation/connection/useAppLaunch.ts",
      "src/foundation/connection/useAwakeControl.ts",
      "src/foundation/connection/useClipboardRead.ts",
      "src/foundation/connection/usePowerControl.ts",
      "src/foundation/connection/usePresentationControl.ts",
      "src/foundation/connection/useTextTransfer.ts",
      "src/foundation/connection/useUrlOpen.ts"
    ],
    rules: {
      "react-hooks/set-state-in-effect": "off"
    }
  },
  {
    files: ["vite.config.ts", "vitest.config.ts"],
    languageOptions: {
      globals: globals.node
    }
  }
);
