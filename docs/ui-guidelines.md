# UI Guidelines

These notes capture project-level UI decisions for the Windows host so future
changes do not repeat layout and window-management problems.

## WinForms Dialog Layout

- Dialogs must never rely on content overlapping, floating behind action
  buttons, or extending below the visible work area.
- Use a root `TableLayoutPanel` with explicit rows for header, content, spacer
  or fixed gap, and actions.
- Action buttons belong in a fixed-height bottom row. Content must not share
  that row or render behind it.
- Lists or growing content belong in a bounded viewport. When content exceeds
  the viewport, the viewport scrolls; the dialog action row stays visible.
- Size fixed dimensions, margins, gaps, and row heights through
  `LogicalToDeviceUnits` or an existing `ScaleLogical` helper.
- Account for the custom themed title bar when choosing form sizes. Do not make
  a dialog taller to expose clipped actions if that can push the action row off
  screen; instead reduce unnecessary spacer rows or bound the content area.
- Verify both light and dark themes when adding selected, inherited, disabled,
  warning, or destructive states.
- Add or update automated host UI layout tests for WinForms changes that affect
  form sizing, action rows, scroll regions, or visibility. Prefer deterministic
  .NET WinForms layout tests in `tests/VolturaAir.Host.Tests` for native host
  forms; Playwright is useful for the mobile web app but does not inspect
  native WinForms controls. Native WinForms layout tests must be safe for the
  GitHub release workflow: if they require an interactive desktop, skip that
  specific UI assertion when `GITHUB_ACTIONS=true` while keeping it active for
  local validation.

## Settings Window

Settings uses a single shell with persistent left navigation and a changing
right content pane. Application, devices, permissions, connection, and
appearance all belong in that shell for normal navigation.

- The right content pane fills the available space to the right of the
  navigation; it must not shrink to preferred content width.
- Page content may grow vertically inside a bounded themed viewport.
- The Settings action row stays visible and outside the page viewport.
- Opening Settings from the pairing window hides the pairing window to avoid
  unnecessary window stacks.
- Standalone detail forms should remain transitional only. Prefer moving
  device and connection management into Settings pages when touching those
  areas.
