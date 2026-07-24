# Windows host UI

WPF-only composition; shared tokens, states, accessibility, and layout remain in
[the UI system](ui-system.md).

## WPF foundation

- WPF owns windows and pages; WinForms is limited to tray interop.
- Declarative XAML, binding, data templates, Grid/DockPanel, and WPF list
  controls express ordinary UI. Programmatic visuals are for dynamic drawing,
  native output, or child structure that is itself an algorithm.
- `SpacingStackPanel` and `SpacingWrapPanel` insert tokenized gaps between
  visible siblings. Leaf controls are marginless; page insets, overlays,
  template geometry, and documented optical corrections remain local.
- Growing content alone owns a `ScrollViewer`; navigation and action rows stay
  fixed. Use recycling virtualization and pixel scrolling for long lists.
- Use the current WPF Fluent foundation and shared dynamic-resource styles.
  Complete control templates require a primitive that property styling cannot
  express.
- Keep the native title bar. Do not use custom scrollbars or runtime
  post-layout shims.

## Shell, startup, and tray

One `Voltura Air` window navigates Connect, Devices, Presentations, Connection,
Preferences, and Diagnostics. Closing hides it to the notification area. The
first close explains that paired devices remain active and that the tray icon
reopens or exits.

The startup window appears immediately and remains for its configured minimum.
It keeps compact dimensions unless startup fails; error actions remain outside
the fallback scroller. Watchdog startup failure offers **Disable watchdog and
restart**, copy details, and close.

Tray menus provide quick access and common presets; Preferences owns complete
configuration. Both operate on service-owned state. Submenu arrows and checked
indicators use theme text color, with a DPI-scaled indicator gutter that also
aligns separators.

## Connect and Connection

Connect keeps the QR code and immediate pairing actions visible. Its collapsed
Details section owns adapter information, selection warnings, technical pairing
details, and their scrolling.

Connection uses one constrained information column. Show active adapter,
endpoint, and automatic/custom state once. **Choose another adapter** owns
adapter recovery. A collapsed custom-port disclosure exposes active or pending
port state in its header. Pending settings are not presented as active: a fixed
change summary lists only changed values and provides **Discard changes** and
**Save and restart**.

## Preferences

Themed sections start collapsed and allow one open section. Headers are
full-width keyboard/pointer targets; their actions remain content-sized.
Expanded content has balanced inset on every side. Order moves from application
and appearance through control defaults and host behavior, permissions,
platform policy, and advanced tools.

Nested settings use the shared nested disclosure. The enclosing stack owns its
external gap. After expansion settles, minimally scroll the Preferences viewer
only when the first usable control is clipped, keeping the focused header
visible without moving focus or animating. Rebuilding after an in-section change
preserves expansion and scroll position.

Presentation laser size/color controls appear only while alpha features are
enabled. Size uses the custom-pointer scale; Red, Green, and Blue are labeled
segmented choices so color is not the sole meaning.

## Devices

Devices is a full-width virtualized accordion list with one open device.
Collapsed headers retain name, connection status, and recent activity. Metadata
follows the header; Appearance, Trackpad profile, and Permissions form a
single-open nested group and start collapsed. Collapsing a device collapses its
children. Disclosure state lasts only for the current page visit.

The page list owns scrolling and its action row remains visible. Pixel scrolling
keeps content taller than the viewport reachable. Up/Down selects a device,
Enter/Space toggles it, and Tab enters its controls; accessibility help states
those keys.

Permissions use wrapping compact cards. Each is a three-state choice:
**Use global** fills with accent and outlines the effective **Allow** or
**Block**; an explicit choice is accent-filled. Equal-width buttons reserve
checkmark space and do not stretch. Applying a choice updates in place without
collapsing the permission/device disclosures.

Trackpad **Save speed** and **Use global** preserve both open disclosures.
Appearance offers **Use global**, **Show**, and **Hide** for the mobile mode
button.

Removal revokes pairing and requires setup again. Use **Remove** and
**Remove all**, with confirmation stating the re-pairing consequence.

## Diagnostics

Diagnostics uses a top-level view switch. In Application log, the content above
the fixed action row owns scrolling; Refresh, Copy, Open folder, and Delete
remain visible. Filters apply as they change and Event supports multiple values.

Automatic refresh runs only while the view is visible and the host is not
minimized. One per-view session permits one read and one latest-filter
follow-up, keeps at most one dispatcher callback pending, shows recoverable read
failure, and releases log/window/dispatcher work on unload. Manual refresh and
logging remain usable after failure.

## Presentations

The archive is newest-first and virtualized. Title, type, device, and date
filters share one control height and apply as they change. Aggregate cards wrap
at compact width. A row exposes Open and responds to double-click; hover and
focus use shared interactive-card states without adding tab stops to
informational children.

Detail replaces the archive in the same page. Its header is
**Presentations > presentation name**, followed by start date/time and captured
device, with the type pill at the far edge. Statistics stay compact; the
timeline preserves chronological break positions; session/break rows are oldest
first with duration and running elapsed time. The footer separates Back,
edit/link, sharing, and destructive Delete actions.

Report dialogs use shared fields, buttons, tooltips, focus, and menus. File/URL
buttons keep stable labels and show a semantic status dot plus above-control
tooltip. Export opens the resulting file through Windows shell association.
Email attaches every requested available file independently of linked URLs and
fails clearly if one disappears; it never opens Explorer as a substitute.

Stored reports remain available when alpha is disabled; new Presentation
controls and saves do not.

## Shared control behavior

- Filters and retention controls use shared combo, field, and date-range styles.
  Peers share height and bottom alignment.
- Keyboard/controller focus recolors the existing one-DIP border; never add the
  default dotted adorner, a second outline, or extra thickness.
- `PillBadge` owns compact status/metadata geometry, typography, and theme.
  Features supply label and semantic tone.
- Tooltips use the shared themed style and default above-control placement.
- Information actions remain separate from checkbox hit targets. Required
  privacy, recovery, or destructive guidance stays visible.
- Modal windows activate without consuming the intended control click; hover
  alone does not activate.
- Selected, inherited, disabled, warning, destructive, and focus states remain
  readable in light, dark, system, and Windows High Contrast themes.

## Validation

Use a warning-free host build and focused tests only for changed behavior.
Significant WPF work follows the root visual checkpoint. Visual verification
covers affected compact/regular layouts, scrolling, focus, theme, and relevant
DPI scaling; use `test:ui` only when its pairing/smoke workflow changed.
