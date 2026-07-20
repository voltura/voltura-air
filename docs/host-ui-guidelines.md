# Windows host UI guidelines

These notes capture host-specific UI decisions. `docs/ui-system.md` is the
product-wide authority for design tokens, primitives, layout ownership,
adaptive states, AI-assisted UI work, and the UI definition of done.

## WPF Host UI

- The Windows host UI is WPF-first. WinForms is allowed only for tray interop.
- Use one primary `Voltura Air` window with page navigation for Connect,
  Devices, Connection, Preferences, and Diagnostics.
- Use a real startup window for host initialization. It should appear
  immediately, stay visible for at least the configured minimum duration, and
  transition to an error state if startup fails.
- Prefer WPF device-independent layout over manual pixel work. Use `Grid`
  with `Auto` rows for headers/actions and `*` rows for growing content.
- Use `SpacingStackPanel` and `SpacingWrapPanel` for tokenized gaps between WPF
  siblings. Leaf controls and reusable content components have zero external
  adjacency margin; the composition container inserts one gap and ignores
  collapsed children. Margins remain valid for page insets, overlay placement,
  control-template geometry, and documented optical corrections.
- Put `ScrollViewer` only around content that can grow. Action rows and primary
  navigation must remain outside scrollable regions.
- Closing the host window hides it to the notification area rather than exiting
  the host. On the first such close, show a tray notification that paired
  devices remain able to control the PC and directs the user to the
  notification-area icon to reopen or exit.
- On Connect, keep the QR code and its immediate pairing actions visible. Put
  network-adapter information, including its selection warning, and technical
  pairing details in the collapsed Details accordion; the Details viewer owns
  their scrolling.
- Preferences uses themed accordion sections. Start them collapsed, allow only
  one section to be expanded, and keep each header a full-width keyboard and
  pointer target while individual actions remain content-sized. Expanded
  content uses the shared balanced inset on every side; individual first or
  last children must not compensate for the accordion boundary. Order sections
  from broad application and appearance settings through control defaults and
  host behavior, then permissions, platform policy, and advanced tools.
- Nested Preferences disclosures use the shared nested-accordion variant, while
  the enclosing settings stack owns the standard gap from the preceding visible
  group. Neither the accordion nor the preceding button, checkbox, or hint owns
  that external separation.
- Devices is a full-width virtualized accordion list. Every collapsed device
  header keeps its name, connection-status pill, and recent activity visible;
  opening one device closes the others. Its metadata appears directly below the
  header, while Appearance, Trackpad profile, and Permissions use the shared nested accordion
  treatment and start collapsed. Permission choices use wrapping compact cards
  so each permission label and its choices stay together without creating a
  separate narrow detail column. The page list owns scrolling and its bottom
  action row remains visible. Keep list virtualization enabled and use pixel
  scrolling so content inside a device taller than the viewport remains
  reachable without jumping to the next device.
- Each device permission is a three-state choice. An inherited value fills
  **Use global** with the accent colour and outlines the currently effective
  **Allow** or **Block** choice with that colour. An explicit **Allow** or
  **Block** choice is accent-filled. Apply a choice in place so its card updates
  immediately without collapsing the device or Permissions accordion. All three
  buttons use the same fixed width, including room for the checkmark, and never
  stretch with the permission card or host window.
- Applying **Save speed** or **Use global** in a device's Trackpad profile keeps
  both the device and Trackpad profile accordions expanded so the result remains
  in context.
- Device disclosure state is local to the current Devices-page visit. Keep it
  while editing on that page, but collapse every device after navigating away
  and returning so the overview is restored.
- Appearance, Trackpad profile, and Permissions form a single-open nested disclosure group
  within each device. Opening any child accordion collapses the other two.
  Appearance offers the three-state **Use global**, **Show**, and **Hide** mode-button
  preference. Collapsing the parent device also collapses all children, so reopening a
  device always starts with its nested sections folded.
- In the Devices list, Up and Down select a device, Enter or Space expands or
  collapses it, and Tab moves through the expanded device controls. Accessibility
  help text states those exact keys.
- Removing a device revokes its pairing record and requires it to pair again.
  Use **Remove** and **Remove all**, never **Disconnect**, for those actions;
  both require a confirmation that states the re-pairing consequence.
- After an accordion expands and layout settles, scroll the Preferences viewer
  only when its first usable control is clipped. Reveal that control with the
  minimum offset that keeps the focused header visible; do not move focus or
  animate the adjustment. Rebuilding Preferences after an in-section setting
  change must keep that section expanded at the same scroll position.
- Diagnostics uses a top-level view switch. In the Application log view, the
  record region is the only vertical scroller; filters, status, and Refresh,
  Copy, Open folder, and Delete actions remain visible. Log filters apply as
  they change, and Event supports selecting multiple values.
- Use the shared themed combo-box, text-field, and date-range styles for filters
  and retention controls. New controls must support light, dark, system, hover,
  focus, selected, disabled, warning, and error states as applicable.
- Do not expose the default dashed or dotted WPF focus adorner. Bordered controls
  show keyboard and controller focus by recoloring their existing one-DIP border
  with the theme focus color; focus must not add a second outline or increase
  the border thickness.
- Lists should use WPF list controls with recycling virtualization. Keep the
  virtualizing items host responsible for scrolling; when rows need separation,
  reserve the tokenized gap inside the collection-owned item template instead
  of replacing the items host or adding a feature-control margin.
- Use the shared `PillBadge` primitive for compact status and metadata labels.
  Filled accent, success, and danger tones communicate current state; outlined
  neutral, accent, and danger tones identify log metadata without overstating it.
  Features provide the label and semantic tone rather than recreating badge
  geometry, typography, or theme brushes.
- Do not use custom scrollbars or runtime layout shims to move controls after
  layout. Fix the layout contract instead.
- Keep the native Windows title bar unless there is a focused reason to revisit
  window chrome.
- Verify light, dark, system, and Windows High Contrast modes when adding
  selected, inherited, disabled, warning, or destructive states. In High
  Contrast, preserve readable semantic state and keyboard-focus feedback using
  system-visible colors rather than relying on custom theme colors alone.
- Add or update host UI tests for navigation, startup behavior, settings save
  behavior, device actions, and permission state. Manual DPI checks should cover
  100%, 125%, 150%, and 200% display scaling on Windows.


## Connection feedback

Connection errors are first-class UI states. Do not leave the user on an active
trackpad or keyboard surface when the host reports disconnected status, health checks
fail, input acknowledgement times out, or input dispatch fails. Show a clear
unavailable/retrying panel with recovery actions and keep it scrollable on small
phones and short landscape screens.

## Remote action surfaces

- Keep the mobile surface focused on current state and the smallest useful
  action set. Detailed policy and platform-specific configuration belong to the
  Windows host; do not duplicate them in the mobile client.
- Place related actions in one responsive sheet when permanent buttons would
  crowd the primary control surface. Preserve the user's context when the sheet
  opens and return focus predictably when it closes.
- Show supported but host-disabled actions with a permission explanation. Omit
  actions for capabilities the host or Windows does not expose at all.
- Keep reversible, low-risk actions direct. Use a dedicated warning state and an
  explicit confirmation gesture for disruptive or session-ending actions.
- Show pending, success, and failure state where the action was initiated. Keep
  the surface open while acknowledgement is useful; close it when leaving it
  open would obscure the action's result, while preserving feedback for later.
- Use tray menus for quick access and common presets. Put complete configuration
  in Preferences, and have both surfaces operate on the same service-owned state.
- Tray submenu arrows and selected-state checkmarks use the active theme text
  color. Checked menus reserve a DPI-scaled indicator gutter so the glyph never
  overlaps labels; separators align with that gutter.
