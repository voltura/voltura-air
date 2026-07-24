# Voltura Air UI system

## Product character

Voltura Air is quiet, immediate, dependable, and native to its context. Mobile
is a touch-first control surface; Windows is a compact system utility. Familiar
behavior, useful control space, status, and recovery outrank decoration. Accent
communicates selection or activation; the focus color communicates keyboard or
controller focus.

## Experience contract

1. Give each surface one clear job and one dominant next action when needed.
2. Show pending, success, and failure feedback where an action begins.
3. Explain denial, disconnection, unsupported capability, invalid input, and
   failure with the safest useful recovery.
4. Keep frequent controls visible; disclose policy, diagnostics, and uncommon
   settings progressively.
5. Keep primary controls spatially stable and assign content growth to one
   explicit scroll owner.
6. Omit unsupported capability; explain supported but disabled capability.
7. Keep reversible low-risk actions direct. Separate and confirm destructive or
   session-ending actions in proportion to their consequence.

## Tokens and primitives

`assets/ui-tokens.json` owns shared colors, spacing, radii, control sizes,
editable-text typography, and motion. `npm run ui:tokens:generate` produces
React and WPF outputs; generated files are not edited.

Use a token for repeated or meaningful visual choices. Local values are for
intrinsic artwork, one-pixel optical corrections, data-derived geometry, or
platform constraints whose reason is clear. Platform-specific semantic tokens
are allowed when mobile and desktop have different purposes.

The shared primitive set covers:

- buttons, icon buttons, segmented choices, fields, selects, ranges, switches,
  labels, hints, validation, and settings rows;
- surfaces, cards, notices, headings, stacks, clusters, grids, and page frames;
- disclosures, tabs, mode selection, drawers, sheets, dialogs, scrims, toasts,
  local feedback, and canonical unavailable/error states.

Primitives own visual variants, interaction states, geometry, and accessible
semantics. Features own labels, content, intent, domain state, and composition.
Extend the system explicitly when a needed concept is absent; do not hide a new
primitive in a feature helper.

Composition owns gaps between adjacent elements. Leaf controls do not acquire
margins based on their current siblings. Page insets, overlay offsets,
template-internal geometry, and documented optical corrections are not sibling
gaps.

### Anchored guidance

Anchored guidance is brief education attached to a visible control after a
user-triggered state change—not operation feedback, requested explanation, or
durable recovery.

The shared primitive owns placement, pointer geometry, viewport tracking, and a
non-interactive live region. It follows the visual viewport, observes viewport,
anchor, and hint size changes, chooses the first fitting placement, and clamps
inside safe visible bounds. The feature owns copy and show/dismiss policy.

### Dialogs

Shared dialog owns modal semantics, scrim, focus, dismissal, safe-area/visual
viewport bounds. Feature owns title, body, actions, validation, and presence.

Dialogs use intrinsic height up to the visible maximum; only the body scrolls.
Title, relevant input/validation, and actions remain available when a software
keyboard reduces the viewport. Wide landscape may place content and actions
side by side, but stacks before either becomes unusable. Use a wide variant only
when content needs working width. Provide visible touch dismissal; Escape is a
secondary keyboard path.

## Layout contract

Express relationships, not coordinates:

- intrinsic content stays `auto`; the working region grows;
- only expected growing content scrolls;
- navigation, current status, and action rows stay outside content scrollers
  unless the whole surface is intentionally a document;
- shrinkable grid/flex tracks use `minmax(0, 1fr)` or the platform equivalent;
- normal structure uses Grid, Flexbox, WPF layout, containment, and intrinsic
  sizing; viewport- or anchor-related overlays may position absolutely.

A non-trivial composition must make clear what stays fixed, grows, scrolls,
collapses, or moves into disclosure; its minimum usable size; and long-text or
software-keyboard behavior.

### Mobile viewport and safe area

The shell uses one stable application frame: `svh` in a normal browser, `lvh`
when installed borderless, and `vh` only as fallback. The layout viewport owns
the shell. The visual viewport is reserved for overlays affected by keyboards,
magnification, or transient browser chrome; opening a keyboard does not resize
the whole shell.

Normalize safe-area insets at the shell and consume each edge once. Edge
surfaces may reach the display edge while interactive content receives the
inset internally. Rotation is a size change, not device identity; do not retain
orientation geometry.

### Adaptive composition

Switch from stacked to side-by-side only when every region meets its declared
minimum width. Peers use equal tracks unless the owner states a content
priority. Stack before clipping or overlap. Dynamic collections use intrinsic
or repeating tracks and never assume an item count.

Canonical checks are 360×640 and 390×844 portrait, 640×360 and 844×390
landscape, 1024×768 wide touch, and Windows 920×620 and 1160×760
device-independent pixels. Add boundary sizes only for a real content
constraint; never add device-specific breakpoints.

### Mobile ownership

The app shell owns the frame, normalized safe areas, navigation, global
overlays, and primary content slot. A mode root owns its responsive composition.
Prefer container queries for component composition and viewport queries only
for viewport facts.

Global CSS is limited to tokens, base behavior, shell layout, and shared
primitives. Feature layout stays with its component. Feature details do not
become shell modifiers.

Mode changes keep navigation stable. Activating the selected mode may toggle
mode navigation but keeps the quick selector. The mode collection is dynamic.
Settings disclosures start collapsed, allow one open section, and minimally
scroll their own region only when the first usable control would be clipped;
keep the focused summary visible and respect reduced motion.

## Interaction states

| State | Required outcome |
| --- | --- |
| Ready | Primary action and current status are unambiguous. |
| Pending | Repeat activation is bounded and local progress is visible. |
| Success | Local confirmation does not block continued use. |
| Empty | Explain absence and offer a useful next action if one exists. |
| Disconnected | Control surfaces stop implying control and offer reconnect/pairing recovery. |
| Denied | Name the responsible permission or policy and recovery path. |
| Unsupported | Omit or mark unavailable according to capability semantics. |
| Invalid | Preserve the value and explain the valid input. |
| Failed | Restore partial state and offer retry or diagnostics. |

Model applicable states before layout so recovery is not fitted into a
happy-path-only surface.

## Input, content, and accessibility

Mobile prioritizes touch on phones/tablets; mouse, trackpad, hardware navigation,
and keyboard are compatibility paths. Windows prioritizes keyboard and mouse and
also supports touch. Preserve semantic controls and assistive access.

- Use specific labels and outcome verbs. Revoking remembered pairing is
  **Remove**, not **Disconnect**, and its confirmation states re-pairing impact.
- Buttons use verbs; headings name places or objects; status describes the
  condition without blame.
- Pair color with text, icon, shape, or accessible state. Focus order follows
  visual and task order.
- Static mobile chrome is not selectable; editable and explicitly copyable text
  remains selectable. Editable values use body-sized text of at least `1rem`;
  never disable zoom to avoid browser magnification.
- A blocking dialog with a clear primary action focuses that action, not a
  static heading.
- Touch targets meet the tokenized minimum. Critical instructions/errors wrap;
  compact status may truncate only when the full value remains available.
- Gesture ownership is declared before contact. Tap, scroll, long-press, drag,
  cancellation, and release remain distinct and accessible.

## UI completion by risk

Routine copy, token, and contained fixes proceed with the scoped static/build
gate and focused visual verification. Significant UI—new or substantially
reworked surfaces, layout direction, navigation, or multi-state
interaction—defaults to the root visual checkpoint.

Check only affected states, canonical sizes, themes, focus/input methods,
scrolling, accessibility, and resource ownership. Use focused behavior tests
for changed state or persistence. Use the real browser/WPF engine when unit/DOM
tests cannot represent the boundary. Run `npm run test:ui` only when its real
pairing/smoke workflow is affected. Leave one maintained presentation path.
