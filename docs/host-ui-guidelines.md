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

One `Voltura Air` window navigates Connect, Devices, Custom screens,
Presentations, Phone webcam, Connection, Preferences, and Diagnostics. Closing hides the
window to the notification area. The first close explains that paired devices
remain active and that the tray icon reopens or exits.

The updater contributes one stateful tray item above **Open product page** and a collapsed **Update** accent button first above navigation only when a verified installer is ready. Both surfaces share the update feature state; neither polls or owns update work.

The topmost startup window appears immediately and is rendered before startup
initialization continues. A non-blocking 1.5-second minimum-display timer runs
concurrently with initialization; on success it closes after both readiness and
the timer complete. Startup errors remain visible immediately. It keeps compact
dimensions unless startup fails; error actions remain outside the fallback
scroller.

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

Connection begins with exclusive Direct LAN and Cloud relay choices. Direct
shows the adapter and port surfaces above. Relay replaces them with safe
connection state, retry, a used-versus-remaining monthly screen-transfer bar,
2/4/8 Mbps quality, and a collapsed custom HTTPS endpoint. Relay failure never
silently opens Direct. The first failed connection in an outage shows one danger-tone host
toast while retries remain automatic and quiet; restoration shows one success
toast. Initial successful connection does not show a restoration toast.

The Connection method card provides a compact themed **How connections work**
dialog. It opens on the pending method, keeps its Direct and Relay selector
read-only with respect to settings, and provides an illustration-only enhanced-
features toggle that does not change the Connection preference. Labelled routes
remain clear without motion, and the moving track glow is removed after two
local-use passes or when the dialog closes. Automatic playback respects reduced
motion; the explicit **Play flow** action replays the illustration. Enhanced
Direct presents secure web-app loading once, followed by two normal local
communication passes; Standard Local shows only the route without a stage heading.
The Play flow coloured border glow appears only while the route is idle; normal
button chrome replaces it during playback so it does not compete with the route
animation. Enhanced-device guidance
appears only in the Direct view because Relay always includes those features.

Preferences owns Direct Screen View quality. Automatic is the recommended
adaptive default and preserves the Windows-scaling-derived readability floor.
Quality keeps the selected display's full resolution while adapting frame rate.
Data saver provides the explicit 4 Mbps mode and may use dimensions below the
readability floor. Changes apply to newly started views without a host restart.

## Phone webcam

Phone webcam shows one compact Windows-camera state surface and one fixed-height
camera-output area. The state surface reports optional VB-CABLE readiness without
implying that third-party software is bundled, and directs per-device Phone webcam
access changes to Devices rather than presenting an obsolete global permission.
When no phone is streaming, the output area shows concise start guidance and
does not open the virtual camera merely to display its waiting frame. While
streaming, it consumes the registered virtual camera exactly like another
Windows application and stops that preview on navigation, removal, or shutdown.
An active microphone-enabled session exposes one explicit **Test audio** action;
starting it monitors `CABLE Output` through the default speakers, and stopping,
navigation, or session teardown releases it. The page warns that audible speakers
near the phone can cause echo or feedback.
No inner scroller, repeated explanatory footer, or second media path is used.

## Preferences

Search stays fixed above the Preferences scroller. It matches section,
nested-disclosure, checkbox, and field labels as the user types; help text,
current values, option values, and action captions are not results. Selecting a
result opens its required disclosures, scrolls the setting into view, and
focuses its control. The query survives an in-page Preferences rebuild but is
not persisted.

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

Presentation laser size/color controls are always available. Size uses the
custom-pointer scale; Red, Green, and Blue are labeled segmented choices so
color is not the sole meaning.

## Devices

Devices is a full-width virtualized accordion list with one open device.
Collapsed headers retain name, connection status, access profile, and recent activity. Metadata
follows the header; Appearance, Trackpad profile, and Permissions form a
single-open nested group and start collapsed. Collapsing a device collapses its
children. Disclosure state lasts only for the current page visit.

The page list owns scrolling and its action row remains visible. Pixel scrolling
keeps content taller than the viewport reachable. Up/Down selects a device,
Enter/Space toggles it, and Tab enters its controls; accessibility help states
those keys.

Permissions begin with the per-device **My device**, **Remote controls**, or
**Custom** selector. Profile-managed permission cards show explicit **Allow** and
**Block** choices; editing a built-in profile materializes Custom and updates in
place without collapsing the permission/device disclosures. The separate
protected-file card retains **Use global**, **Hide**, and **Show**. Equal-width
buttons reserve checkmark space and do not stretch. Notification navigation can
open one device by stable ID, reveal Permissions, and focus the profile selector.

Trackpad **Save speed** and **Use global** preserve both open disclosures.
Appearance offers **Use global**, **Show**, and **Hide** for the mobile mode
button.

Removal revokes pairing and requires setup again. Use **Remove** and
**Remove all**, with confirmation stating the re-pairing consequence.

## Custom screens

The library keeps New plus independent delete- and hide-confirmation settings
outside its scroller.
Each saved-screen card gives Edit, Preview, Duplicate, and Delete equal-width
actions, with a compact right-edge grip for drag ordering. Preview opens a
read-only WPF window whose fixed themed controls select device and orientation
or rotate the embedded mobile rendering. Only the rendering is HTML, loaded
through loopback. The device selector names Generic phone/tablet, the selected
paired Mobile device dimensions when applicable, and the maintained phone and
tablet UI-validation sizes; it never labels a choice only as “Selected.”
Successful navigation away from Custom screens closes all of its preview
windows.
Dragging moves the actual card live through the list without a detached card
preview. Assignment changes, duplicate, reorder, delete, Preview, and Save use
the shared host toast and write sanitized operation outcomes to the optional
Application log.

The editor keeps screen name, Back, Undo, Redo, Preview, and Save above a
three-column workspace: component palette, scalable device preview, and context
properties. Preview opens the saved revision and stays disabled for a new or
dirty draft until Save succeeds. Its initial native-window device and
orientation match the editor selectors.
The themed dividers on either side of the preview resize the palette and
properties columns from their default minimum widths. Their widths persist per
signed-in Windows user, while the preview keeps the remaining space and scales
the virtual device uniformly. Palette action labels wrap at words instead of
clipping when their available width is narrow.
The component palette and properties column scroll independently when their
content does not fit. Layout, Hidden controls, and Editing are collapsed by
default and reuse the inspector's compact `+`/`−` disclosure treatment.
Available components uses the same treatment and starts expanded. Hidden
controls is a separate row shown when orientation layouts are enabled. Compact
header actions expand or collapse all four left-column sections. Layout places
**Show Back and screen title** immediately below orientation layouts. The device
preview owns scrolling. Device and
orientation selectors share their row proportionally at narrow widths; their
themed borders must not clip. Property choices use shared themed selects.
Orientation and delete-confirmation labels wrap instead of clipping, and the
checkbox glyph stays vertically centered beside wrapped text. Context
properties use compact `+`/`−` disclosure rows rather than full accordion
chrome. Action starts expanded; generated Name and Label values also start open.
Compact header actions expand or collapse every property group. The active
action type's dependent fields share one subtle surface and border.

Regular, collapsible, trackpad, collapsible-trackpad, volume-slider, and navigation-ring
components, rows, and buttons use accent selection. A collapsible panel retains
regular panel properties but
requires its name as the header. Its accessible preview header folds or unfolds
the draft, and that state is the saved device default; **Expanded by default**
provides the keyboard-accessible property equivalent. Clicking anywhere in an
explicit row outside a control selects it as the target for **+ Button**.
Palette components are named Panel, Collapsible panel, Button, Volume slider,
Trackpad, Collapsible trackpad, and Navigation ring and use a normal click-to-add body plus a compact six-dot
drag grip. Trackpad variants use the standard panel width, wrapping,
content/fill, fill-weight, and orientation controls; their Trackpad group owns
click-button order and the optional fullscreen control. Volume slider is a
standalone responsive component with 25%, 50%, 75%, and 100% widths and reuses
the standard mobile volume surface. Navigation ring is a standalone component
with 50%, 67%, 75%, and 100% widths, content/fill height, repeatable directions,
and a compact ring representation in the editor; mobile Preview places the ring
on the regular gridded trackpad surface. A button panel's Buttons group selects
Start, Center, End, Space between, Space around, or Space evenly placement;
Start keeps compact buttons grouped by default. Editor dragging
uses a live component snapshot that preserves the pointer's grab point plus
strong before/after or row targets; properties retain keyboard-accessible Move
and destination controls. A nested control exclusively owns a drag that starts
on it; a panel drag starts only from non-interactive panel space.
The full device workspace remains a drop target when no panels exist. Dropping
a palette button on open workspace creates a regular panel and its button;
dropping an existing button there creates a regular panel and moves that button
into it.
The snapshot is custom WPF feedback and does not depend on the Windows
show-window-content setting. Buttons and panel cards use a compact themed
context menu. In orientation mode it offers a local Hide action and explicit
Delete everywhere; otherwise it offers Delete. Delete and orientation-local
Hide have independent confirmation settings and use the shared themed dialog.
A draft deletion or hide remains undoable until explicit Save.

Enabling orientation layouts copies the current responsive composition into
peer Portrait and Landscape layouts. Existing components initially appear in
both; later additions appear only in the active layout. Component identity and
behavior remain shared, while visibility, order, width/size, and button row are
orientation-owned. The component palette provides a separate **Hidden controls**
section for the active canvas, and each selected component's Visibility group
exposes both orientation states.
Showing a hidden panel in the active orientation also shows every control it
contains there, so its contents and ownership are immediately understandable.
Hidden button rows identify their containing panel beside the Show action.

The key-or-shortcut action editor is a staged composer. Each selected modifier
is removed from the available modifier buttons and appended to the Command
preview. F1-F12 have a dedicated Function key selector. Backspace, Delete,
Enter, Insert, Page up/down, Home, End, and arrows have a dedicated Special key
selector. A-Z and 0-9 share the Letter or number selector and retain their
visible selection. The editor prevents selecting AltGr together with Ctrl or
Alt; the host still reads existing stored combinations. Reset clears the staged
sequence. Common punctuation uses the Symbol key selector. **Save
command** remains disabled until the sequence contains any valid non-modifier
final key. A letter, digit, function, special, or symbol key qualifies; a
modifier-only sequence does not. Saving it updates the button action and
participates in normal draft Undo/Redo.
Literal-text and key/shortcut action types force the button Visual choice to
Label; icon and icon-plus-label choices remain available for built-ins and
approved applications.

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
