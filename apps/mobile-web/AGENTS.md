# Mobile web

Inherits root; read relevant UI-system/architecture guidance.

## Structure

- Owners: `app`=composition; `features/<capability>`=slices/styles/public API;
  `ui`=domain-neutral; `foundation/<domain>`=non-presentational.
- Flow/imports: `app -> features -> ui`; app/features may use foundation. `ui`
  imports no feature/foundation state; foundation imports no React
  presentation/features; no cross-feature private imports.
- Keep `App.tsx` thin; no catch-all buckets.

## Contracts

- TS7 builds product code; TS6 is ESLint-API-only. Never downlevel valid TS7
  for analyzers.
- Narrow boundary values from `unknown`; clean up effect-acquired resources.
- Before contact, declare gesture ownership and fix `touch-action`. Accessibly
  handle tap/scroll/long-press/drag/cancel/release. Feature-detect; never
  UA-detect.

## Verify

- Default: `npm run check --workspace apps/mobile-web`; focused Vitest only for
  changed behavior/state.
- Add production build for bundle/dependency/entry-point/broad-integration
  changes. Full `npm run test:web` only for broad/shared
  foundation/protocol/app-shell changes.
- Follow root UI checkpoint. Responsive/gesture validation covers directions,
  scroll boundaries, cancellation, persistence; use Playwright/real hardware
  beyond DOM fidelity.
