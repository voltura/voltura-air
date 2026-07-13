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
- Preferences uses themed accordion sections. Start them collapsed, allow only
  one section to be expanded, and keep each header a full-width keyboard and
  pointer target while individual actions remain content-sized.
- Diagnostics uses a top-level view switch. In the Application log view, the
  record region is the only vertical scroller; filters, status, and Refresh,
  Copy, Open folder, and Delete actions remain visible. Log filters apply as
  they change, and Event supports selecting multiple values.
- Use the shared themed combo-box, text-field, and date-range styles for filters
  and retention controls. New controls must support light, dark, system, hover,
  focus, selected, disabled, warning, and error states as applicable.
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


## Connection feedback

Connection errors are first-class UI states. Do not leave the user on an active
trackpad or keyboard surface when the host reports disconnected status, health checks
fail, input acknowledgement times out, or input dispatch fails. Show a clear
unavailable/retrying panel with recovery actions and keep it scrollable on small
phones and short landscape screens.

## Remote power and display actions

- Keep Lock PC and a configured screen saver in the Power sheet while their host
  acknowledgement or error is pending. Close the sheet after Blackout display is
  requested so it does not obscure the restored display; retain its result for
  the next time Power is opened.
- Show **Turn on screen saver** only when the host reports that Windows has an
  enabled, configured screen saver. Do not show a permanently disabled row for
  a feature the PC does not expose.
- Treat **Blackout display** as the dependable keep-awake alternative: it covers
  all monitors with host-owned black windows and closes on local or remote input.
- Keep **Turn off display** behind confirmation and explain that Windows may
  interpret the native monitor-power command as sleep or Modern Standby.
