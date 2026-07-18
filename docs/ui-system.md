# Voltura Air UI System

This document is the product-wide authority for user experience, visual design,
layout, and AI-assisted UI development. It applies to the React mobile client,
the WPF Windows host, documentation screenshots, and any future Voltura Air
surface.

Implementation work preserves intentional behavior while conforming presentation
structure to this system.

## Product character

Voltura Air is quiet, immediate, dependable, and native to its context.

- The mobile client is a couch-friendly control surface. Its primary modes give
  as much space as practical to direct manipulation and frequently used actions.
- The Windows host is a compact system utility. It explains system state and
  centralizes detailed policy without behaving like a promotional dashboard.
- Familiar platform behavior wins over novelty. A user should not have to learn
  a visual trick before they can complete a primary action.
- Decoration never competes with control space, status, or recovery guidance.
- The accent color communicates selection, activation, focus, or important
  progress. It is not general decoration.

## Experience principles

1. **One clear job per surface.** A page or sheet has one primary user goal and
   one visually dominant next action where an action is required.
2. **Direct feedback.** Pending, success, and failure feedback appears where the
   action began. Do not make the user infer whether a command was received.
3. **Recoverable failure.** Disconnection, denial, unsupported capabilities,
   invalid input, and system failures explain what happened and offer the safest
   useful next step.
4. **Progressive disclosure.** Frequent controls stay visible. Detailed policy,
   diagnostics, and uncommon configuration remain in focused settings sections,
   sheets, or dialogs.
5. **Stable spatial behavior.** State changes do not unexpectedly move primary
   controls. Content growth is handled by an explicitly owned scroll region.
6. **Input equality.** Touch, mouse, pen, keyboard, screen reader, and switch
   access reach the same outcomes. Platform-only feedback such as vibration is
   supplementary.
7. **No silent degradation.** If a capability is unavailable, either omit it
   because the platform cannot support it or explain why it is disabled.

## Design authority

UI decisions have the following order of authority:

1. User goal and safety.
2. This document and the generated design tokens.
3. An existing approved UI primitive or layout pattern.
4. Feature-specific presentation.

Feature code composes the system. It must not create a parallel button, card,
field, dialog, accordion, toast, spacing scale, or responsive convention.

Composition primitives own spacing between adjacent UI elements. Do not add a
margin to a feature control because of whichever sibling happens to follow it.
When a relationship recurs, such as a nested disclosure following a settings
group, express that relationship as a named layout or component variant.

In WPF, `SpacingStackPanel` and `SpacingWrapPanel` are the standard implementations
of this rule. They consume generated spacing tokens, insert a single gap only
between visible children, and leave leaf controls marginless. Page insets,
overlay offsets, template-internal geometry, and explicit optical corrections
are not sibling gaps and may still use `Padding`, grid tracks, or a documented
local margin as appropriate.

When the system lacks a needed concept, extend the system explicitly and use the
new concept in at least the requesting feature. Do not disguise a new primitive
as a one-off feature class or helper.

## Maintainable vertical slices

The target ownership model is defined in [architecture.md](architecture.md):

```text
app/                    composition, shell state, global overlays
features/<capability>/  capability UI, state, copy, styles, and tests
ui/                     domain-neutral controls and layout primitives
foundation/             connection, protocol, persistence, and platform adapters
```

Dependency direction is strict:

```text
app -> features -> ui
       features -> foundation
app ------------> foundation
```

- `ui` does not import a feature, application state, protocol type, or storage
  model.
- `foundation` does not import React components or a feature.
- A feature does not reach into another feature's private files. Cross-feature
  behavior moves through an explicit typed contract or is promoted to a real
  shared foundation.
- `app` selects and composes features. It does not implement settings forms,
  mode controls, pairing flows, remote controls, or feature-specific feedback.
- A feature folder exposes a small public entry point and keeps its components,
  hooks, models, copy, styles, and tests together.
- Avoid catch-all `components`, `helpers`, `common`, and `utils` buckets. A file
  that cannot be given a clear owner is an architecture question, not a filing
  problem.

Composition roots stay readable at a glance. Source-size review thresholds and
the ownership report are defined in [architecture.md](architecture.md).

Communication foundations expose typed commands, state, and events. They own
transport, retries, validation, capability negotiation, and persistence. UI
slices translate user intent into those contracts and never operate sockets,
registries, native handles, or storage formats directly.

### Web quality gate

The compiler, lint configuration, commands, and TypeScript version boundary are
owned by [the mobile instructions](../apps/mobile-web/AGENTS.md) and checked-in
tool configuration. UI work passes that gate with zero warnings.

The enforced outcomes are typed boundary data, correct hook/effect ownership,
accessible JSX, architecture-compliant imports, explicit promise handling,
deterministic cleanup, and token-based authored CSS.

Effects synchronize React with an external system or own a subscription,
timer, animation frame, observer, socket, or similar resource. Every acquired
resource has a cleanup path. Derived presentation state is computed during
render; it is not copied into another state variable by an effect. Async work is
awaited, explicitly discarded with `void`, or cancelled/ignored when its owner
ends. JSON, storage, socket messages, browser events, and other boundary data
enter as `unknown` and are narrowed before use.

Static rules complement focused lifecycle tests for sockets, listeners, timers,
animation frames, observers, and abortable requests. Refactors keep ownership
visible and conceptual complexity proportionate.

## Design tokens

`assets/ui-tokens.json` is the single source for shared colors, spacing, radii,
control sizes, and motion durations. `npm run ui:tokens:generate` produces the
React CSS variables and WPF resources. Generated files are not edited directly.

Tokens represent intentional choices, not every number that can appear in UI
code. Repeated or visually meaningful values must be tokens. A local value is
acceptable for intrinsic artwork geometry, a one-pixel rendering correction, a
data-derived size, or a platform constraint when its reason is clear beside the
code.

Prefer semantic usage such as `--control-min-height`, `--page-gutter`, and
`--color-danger`. The raw spacing scale is available for composition when a
semantic token would add no information.

Desktop and mobile share the same visual language, but they need not force the
same layout density. A platform-specific semantic token is allowed when the
different purpose is explicit.

## Core primitives

The maintained primitive set is deliberately small:

- button and icon button;
- segmented choice;
- text field, select, range, switch, label, hint, and validation message;
- surface/card and notice;
- stack, cluster, responsive grid, and page frame;
- section heading and settings row/group;
- disclosure/accordion;
- tabs and mode selection;
- drawer, sheet, dialog, and scrim;
- toast and local status feedback;
- loading, empty, unavailable, denied, disconnected, and error states.

Primitives own their visual variants and interaction states. Features own labels,
content, intent, domain state, and composition.

Every interactive primitive covers, as applicable:

- default, hover, pressed, selected, focused, disabled, pending, success,
  warning, and error states;
- light, dark, and system themes;
- pointer, touch, and keyboard operation;
- accessible name, role, value, and state;
- reduced motion and high-contrast behavior.

## Layout grammar

Layout is expressed through relationships rather than coordinates.

- `auto` content keeps its intrinsic size.
- the primary working region grows into remaining space;
- only the region expected to grow owns scrolling;
- action rows, navigation, and current status stay outside content scrollers
  unless the whole surface is intentionally a document;
- flex/grid tracks use `minmax(0, 1fr)` or the WPF equivalent when content must
  be allowed to shrink;
- structural layout does not use absolute positioning or runtime measurement
  when Grid, Flexbox, WPF layout, or containment can express the relationship;
- overlays may use fixed/absolute positioning because their relationship is to
  the viewport or an anchor, not document flow.

Every non-trivial surface records its layout contract in code structure, a
focused test, or a short comment:

- what remains fixed;
- what grows;
- what scrolls;
- what may collapse or move into disclosure;
- minimum usable inline and block size;
- long-text and software-keyboard behavior.

### Canonical adaptive states

Design and verify behavior, not device brands:

- **compact portrait:** 360 x 640 CSS pixels;
- **regular portrait:** 390 x 844 CSS pixels;
- **short landscape:** 640 x 360 CSS pixels;
- **regular landscape:** 844 x 390 CSS pixels;
- **wide touch:** 1024 x 768 CSS pixels;
- **Windows compact:** 920 x 620 device-independent pixels;
- **Windows regular:** 1160 x 760 device-independent pixels.

Additional boundary sizes are tested when a feature introduces a real content
constraint. Do not introduce a device-specific breakpoint.

### React layout ownership

- The application shell owns viewport height, safe areas, top-level navigation,
  global overlays, and the one primary content slot.
- A mode root is a named containment context and owns its internal responsive
  behavior.
- Prefer container queries for component and mode composition. Use viewport
  media queries only for viewport-level facts such as safe areas, orientation,
  display mode, or the software keyboard boundary.
- Keep global CSS limited to generated tokens, reset/base behavior, shell layout,
  and truly shared primitive styles. Feature layout is colocated with its React
  component in a CSS module or a narrowly named feature stylesheet while it is
  being migrated.
- A new shell state class requires a shell-level behavior. Feature details must
  not accumulate as application-shell modifiers.
- Settings disclosures start collapsed and allow only one section to be open at
  a time. When an opened section's first usable control would otherwise be
  clipped, scroll only the owning settings region by the minimum amount needed
  to reveal it while keeping the focused summary visible. Do not scroll a
  section that is already usable, move focus into its content, or animate the
  assisted scroll when reduced motion is requested.

### WPF layout ownership

- Pages and reusable sections are declarative XAML `UserControl`s or data
  templates. C# builds data and handles behavior; it does not manually assemble
  ordinary visual trees.
- Use `Grid` with `Auto` and `*`, `DockPanel`, `StackPanel`, `ItemsControl`,
  `ListView`, `ScrollViewer`, shared-size groups, binding, and data templates.
- Programmatic visuals are reserved for genuinely dynamic drawing, native
  interop output, or a control whose child structure is itself the algorithm.
- Prefer built-in control behavior and the current WPF Fluent foundation. A
  complete `ControlTemplate` is justified only when property styling cannot
  express an approved primitive.
- Page code does not set theme brushes directly. Dynamic resources and primitive
  styles own theme application.

## Interaction state contract

Before implementation, enumerate every applicable state:

| State | Required outcome |
| --- | --- |
| Ready | The primary action and current status are unambiguous. |
| Pending | Repeat activation is bounded and progress is visible locally. |
| Success | Confirmation appears near the initiating control and does not block continued use. |
| Empty | The absence of content is explained and a useful next action is offered when one exists. |
| Disconnected | Input surfaces stop implying control and offer reconnect or pairing guidance. |
| Denied | The responsible permission or policy is named with a recovery path. |
| Unsupported | The control is omitted or explicitly unavailable according to capability semantics. |
| Invalid | The field retains the user's value, identifies the problem, and describes valid input. |
| Failed | Expected boundary failures are caught, partial state is restored, and retry or diagnostics are available. |

State is modeled before layout so error and recovery UI is not bolted onto space
that only fits the happy path.

## Content and accessibility

- Use direct, specific labels. Prefer **Copy pairing link** to **Copy** when
  context is not persistent.
- Buttons use verbs. Headings name places or objects. Status text describes the
  current condition rather than blaming the user.
- Do not encode meaning by color alone. Pair color with text, iconography, shape,
  or accessible state.
- Preserve visible focus. Focus order follows visual and task order.
- In the mobile client, static labels and control chrome are not user-selectable
  and do not open WebKit touch callouts. Text entry, editable content, and
  surfaces explicitly intended for copying opt into normal selection through
  the shared base interaction contract.
- When a blocking dialog has a clear primary action, move initial focus to that
  control instead of making a static heading focusable. Pending and success
  feedback keep focus visible and bound without allowing repeat activation.
- Touch targets are at least the tokenized minimum control size unless the
  platform supplies a larger accessible hit target.
- Text must wrap or truncate intentionally. Critical instructions and errors
  wrap; compact status labels may truncate when the full value remains available.
- Destructive or session-ending actions are visually and spatially distinct and
  require confirmation proportionate to their consequence.

## AI-assisted UI workflow

For every non-trivial UI change, establish:

1. the user's goal and primary action;
2. existing primitives and layout patterns to reuse;
3. the component/layout hierarchy;
4. adaptive behavior at relevant canonical states;
5. applicable interaction states from the state contract;
6. touch, keyboard, focus, scrolling, accessibility, and validation behavior;
7. the system-level purpose of each new token, primitive, shell modifier, or
   breakpoint.

Implementation rules:

- Identify the violated layout contract before changing offsets.
- Put the fix in the owning primitive or composition and remove superseded
  workarounds.
- Replace weak presentation abstractions while preserving intentional behavior,
  tests, and domain/service ownership.
- Give each breakpoint, global selector, complete WPF template, and visual
  primitive a system-level purpose.
- Leave one maintained presentation path.

## Definition of done

A user-visible UI change is complete only when the items applicable to its risk
and behavior are satisfied:

- a new or changed primary action works through its production boundary;
- applicable ready, pending, empty, disconnected, denied, invalid, success, and
  failure states are handled;
- it composes approved primitives or explicitly extends the system;
- relevant canonical layouts have been checked in portrait and landscape or at
  Windows compact and regular sizes;
- affected long-content, scrolling, focus, keyboard, accessibility, and theme
  behavior is verified;
- sizing, scrolling, focus, and gesture behavior each have one clear owner;
- repeated visual decisions can be changed centrally;
- stateful behavior, persistence, regressions, and resource ownership have
  focused tests where they can be represented reliably;
- presentation-only changes receive a quick human visual check instead of a
  disposable pixel-test harness when the user is available;
- browser-engine or WPF behavior is checked on the real engine where DOM/unit
  tests cannot faithfully represent it, with any remaining boundary stated;
- superseded presentation code has been removed;
- the mobile production build or warning-free host build succeeds as applicable.
