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

## Settings Window Direction

When the current floating settings dialogs are revisited, prefer a single
Settings shell with persistent left navigation and a changing right pane:

1. Refactor `SettingsForm` into left navigation plus content pane.
2. Move current application and appearance controls into Settings pages.
3. Move global permissions into a Settings permissions page.
4. Move connection settings into a Settings connection page, either during the
   same cleanup or as a follow-up.
5. Keep per-device permissions reachable from Device Manager as a focused
   device-specific editor.

The goal is to avoid stacks of floating windows for general application
settings while keeping device-specific actions close to the device row that
opened them.
