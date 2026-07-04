# UI Guidelines

These notes capture project-level UI decisions for the Windows host.

## WPF Host UI

- The Windows host UI is WPF-first. WinForms is allowed only for tray interop.
- Use one primary `Voltura Air` window with page navigation for Connect,
  Devices, Connection, Preferences, and Diagnostics.
- Use a real startup window for host initialization. It should appear
  immediately, stay visible for at least the configured minimum duration, and
  transition to an error state if startup fails.
- Prefer WPF device-independent layout over manual pixel work. Use `Grid`
  with `Auto` rows for headers/actions and `*` rows for growing content.
- Put `ScrollViewer` only around content that can grow. Action rows and primary
  navigation must remain outside scrollable regions.
- Lists should use WPF list controls with virtualization where possible.
- Do not use custom scrollbars or runtime layout shims to move controls after
  layout. Fix the layout contract instead.
- Keep the native Windows title bar unless there is a focused reason to revisit
  window chrome.
- Verify light, dark, and system theme modes when adding selected, inherited,
  disabled, warning, or destructive states.
- Add or update host UI tests for navigation, startup behavior, settings save
  behavior, device actions, and permission state. Manual DPI checks should cover
  100%, 125%, 150%, and 200% display scaling on Windows.
