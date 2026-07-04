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

## Mobile Input Polish

Current state:

- Trackpad pointer speed, tap-to-click, two-finger vertical/horizontal scroll,
  pinch zoom, expanded trackpad mode, split mode, and PC volume controls exist.
- Trackpad settings are stored per saved PC profile.
- Scroll direction is exposed as Natural scrolling and Traditional scrolling.
- Optional pointer smoothing, pointer acceleration, scroll acceleration, haptic
  click feedback, left-handed button layout, and larger click buttons exist.
- Physical left/right trackpad buttons can be held down while another finger
  moves on the trackpad, enabling window dragging and resizing.
- A host-disabled-by-default gesture debug screen can show touch count, raw
  movement, active settings, and recognizer output without sending messages to
  the PC.
- The trackpad surface blocks browser scrolling, text selection, image dragging,
  touch callouts, and cancelled gestures from turning into clicks.

Near-term work:

1. Validate pinch zoom in Chrome/Edge, Photos, a PDF viewer, and Office or
   PowerPoint.
2. Device-test held-button dragging, resizing, and the input surface on iPhone
   Safari, Android Chrome, iPad Safari,
   Android tablet Chrome, and ChromeOS browser.
3. Tune pointer smoothing and acceleration constants from real-device feedback
   before changing defaults.

Implementation notes:

- Keep input protocol changes rare; prefer client-side settings for feel and
  layout changes unless Windows needs a new primitive.
- Keep new motion behavior opt-in until it has been validated on real phones
  and tablets.
