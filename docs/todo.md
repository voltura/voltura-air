# TODO

This file tracks near-term product and engineering follow-ups that should stay
visible without becoming detailed implementation documentation.

## Settings UI Redesign

Goal: reduce stacks of floating settings windows and make host configuration
feel like one coherent Windows settings surface.

1. Refactor `SettingsForm` into a Settings shell with persistent left
   navigation and a changing right content pane.
2. Move current application and appearance controls into Settings pages.
3. Move global permissions into a Settings permissions page instead of opening
   a separate global permissions window.
4. Move connection settings into a Settings connection page, either during the
   same cleanup or as a follow-up if the first pass should stay smaller.
5. Keep per-device permissions reachable from Device Manager as a focused
   device-specific editor.

Implementation notes:

- Preserve the existing themed form chrome, controls, and application look and
  feel.
- Keep the left navigation visible while switching pages on the right.
- Do not let content overlap action rows or render behind buttons.
- Use bounded scroll regions for growing page content.
- Keep fixed dimensions, gaps, margins, and row heights DPI-scaled.
- Validate in both light and dark mode.
