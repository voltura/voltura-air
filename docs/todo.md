# TODO

This file tracks near-term product and engineering work that should stay
visible without becoming detailed implementation documentation.

## Settings UI Redesign

Goal: keep host configuration in one coherent Settings surface and reduce
stacks of floating windows.

1. Move connection settings fully into the Settings `Connection` page so normal
   navigation no longer opens a standalone connection settings form.
2. Move device management fully into the Settings `Devices` page so normal
   navigation no longer opens a standalone device manager form.
3. Keep per-device permissions reachable from the embedded Devices page as a
   focused device-specific editor, then decide whether that editor should also
   become an in-page detail view.

Implementation notes:

- Preserve the existing themed form chrome, controls, and application look and
  feel.
- Keep the left navigation visible while switching pages on the right.
- Do not let content overlap action rows or render behind buttons.
- Use bounded scroll regions for growing page content.
- Keep fixed dimensions, gaps, margins, and row heights DPI-scaled.
- Validate in both light and dark mode.
- Standalone forms may remain as internal transition helpers while migrating,
  but the intended UX is one Settings shell for application, devices,
  permissions, connection, and appearance.
