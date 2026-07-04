# TODO

This file tracks near-term product and engineering work that should stay
visible without becoming detailed implementation documentation.

## Settings UI

Current state:

- Settings is the primary host configuration surface.
- Application, Devices, Permissions, Connection, and Appearance are available from
  the left navigation.
- Devices is embedded in Settings and contains paired device rows, per-device
  Permissions actions, duplicate cleanup, disconnect, and remove actions.
- Connection is embedded in Settings and contains current host URL, network
  selection, port selection, and save behavior.
- Standalone Device Manager and Connection Settings forms may remain as internal
  wrappers around the same panels, but normal Settings navigation should not open
  extra windows for those pages.

Near-term work:

1. Decide whether per-device permissions should remain as a focused dialog or
   become an in-page detail view under Settings > Devices.
2. Validate Settings in light mode, dark mode, and system theme mode.
3. Validate Settings at common Windows display scaling levels, especially 100%,
   125%, 150%, and laptop-sized screens.
4. Confirm embedded Devices and Connection pages keep their scroll regions,
   action rows, spacing, and button sizing clean when the Settings window is near
   its minimum or maximum size.
5. Run the full Windows host build and test path before merging Settings shell
   changes.

Implementation notes:

- Preserve the existing themed form chrome, controls, and application look and
  feel.
- Keep the left navigation visible while switching pages on the right.
- Do not let content overlap action rows or render behind buttons.
- Use bounded scroll regions for growing page content.
- Keep fixed dimensions, gaps, margins, and row heights DPI-scaled.
