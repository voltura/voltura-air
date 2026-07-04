# TODO

This file tracks near-term product and engineering work that should stay
visible without becoming detailed implementation documentation.

## Windows Host WPF UI

Current state:

- The Windows host uses a WPF-first shell with a startup screen and one primary
  `Voltura Air` window.
- The main window contains Connect, Devices, Connection, Preferences, and
  Diagnostics pages.
- Tray actions open the primary window focused on the relevant page.
- Devices includes inline per-device permission controls.
- Connection includes network selection, port selection, and save behavior in
  the main shell.

Near-term work:

1. Validate the WPF shell in light mode, dark mode, and system theme mode.
2. Validate the shell at common Windows display scaling levels, especially
   100%, 125%, 150%, 200%, and laptop-sized screens.
3. Refine visual polish for the WPF pages: spacing, selected states, empty
   states, and warning states.
4. Remove legacy WinForms forms once no tests, docs, or fallback paths depend
   on them.
5. Expand automated WPF UI coverage around startup, navigation, connection
   saving, device actions, and permission inheritance.
6. Run the full Windows host build and test path before merging UI shell
   changes.

Implementation notes:

- Keep layout adaptive and device-independent.
- Do not reintroduce custom scrollbars, runtime row resizing, or pixel shims.
- Keep tray interop small and isolated.
