# Mobile web instructions

These instructions apply to `apps/mobile-web`. The product-wide UI target is
defined by `../../docs/ui-system.md`; architecture boundaries are defined by
`../../docs/architecture.md`.

## Target architecture

The physical target is:

```text
src/app/                    composition, shell state, global overlays
src/features/<capability>/  capability UI, state, copy, styles, and tests
src/ui/                     domain-neutral controls and layout primitives
src/foundation/             connection, protocol, persistence, and platform adapters
```

Until P0 is complete, root modules and the `connection`, `input`, `pairing`,
`pwa`, and `settings` directories count as foundation ownership. New foundation
code belongs under `foundation/<domain>`. A refactor moves a coherent ownership
unit and its tests.

Dependency direction is `app -> features -> ui`, with `app` and `features`
allowed to consume foundation contracts. `ui` imports neither features nor
foundation state. Foundation imports neither React presentation nor features.
Features do not import another feature's private files; the app composes their
public entry points.

Keep `App.tsx` thin. Do not grow flat component buckets, global feature CSS,
catch-all helpers, or shell modifiers that encode feature details. Colocate a
slice's components, hooks, models, copy, tests, and narrowly owned styles.

## Language and static quality

- Target React 19 and TypeScript 7. The checked-in TypeScript 7 compiler is the
  only product build and type-checking boundary.
- The separate TypeScript 6 package exists only as the programmatic API adapter
  required by the current type-aware ESLint ecosystem. Never run the product
  typecheck with TypeScript 6, downlevel valid TypeScript 7 code for an analyzer,
  or change either version during a rule review unless versions are in scope.
- Prefer function components, typed hooks, discriminated unions, `satisfies`,
  optional chaining, and standards-based modern platform APIs that work on the
  app's HTTP LAN origin. Do not introduce legacy React or compatibility patterns.
- Treat boundary input as `unknown` and narrow it. Handle promises explicitly.
  Effects synchronize with external systems and clean up every acquired timer,
  listener, observer, request, animation frame, socket, or subscription.
- `npm run lint` runs ESLint and Stylelint with zero warnings. Keep exceptions
  narrow, reviewed, documented in central configuration, and compatible with the
  TypeScript 7 product compiler. Do not change semantics or add indirection only
  to satisfy a tool.

## Web interaction and layout

- Research unfamiliar or repeatedly failing interaction, layout, scrolling,
  touch, pointer, focus, viewport, and compatibility behavior in the relevant
  web standard and official browser documentation before implementing a fix.
  Prefer W3C/WHATWG, MDN, and official Chromium or WebKit material.
- Design for modern Android, iOS/iPadOS, Windows touch, and desktop browsers with
  feature detection and progressive enhancement, not user-agent checks.
- Declare gesture ownership before contact begins. `touch-action` is evaluated at
  gesture start; do not rely on late `preventDefault`, overflow toggles, passive
  listener workarounds, or scroll correction to revoke an active native gesture.
- Keep gesture state machines explicit: tap, pre-activation scroll, long press,
  drag, cancellation, and release. Clean up capture, timers, and listeners on
  every exit and preserve keyboard and accessible operation.
- Keep global CSS limited to generated tokens, base/reset behavior, shell layout,
  and shared primitives. A feature owns its layout and styles.

## Validation

- Run `npm ci` for a reproducible clean install from the checked-in lockfile.
  Use `npm install` when dependency manifests are intentionally changed.
- Run focused Vitest coverage for changed stateful behavior or regressions.
- For presentation-only changes, run the static/build gate and use quick human
  visual verification at the affected canonical sizes, themes, and states. Add
  automation when layout or interaction behavior is stable and meaningfully
  representable, not merely to prove a pixel offset.
- For non-trivial gesture or responsive behavior, cover both directions,
  already-scrolled containers, boundaries, cancellation, and persistence. Use
  Playwright on relevant engines or a real device when DOM tests cannot model
  browser behavior, and state the remaining validation boundary.
- The mobile production gate is:

```powershell
npm run build --workspace apps/mobile-web
npm run test:web
```
