# Features

This page is a high-level capability map for Voltura Air. Keep it focused on
what the applications can do, not how each feature is implemented.

## Windows Host

- Runs as a Windows tray application with one primary WPF window for Connect,
  Devices, Connection, Preferences, and Diagnostics pages, plus product page
  and exit commands from the tray menu.
- Hosts the mobile web app on the local network and receives authenticated
  WebSocket commands from paired devices.
- Pairs devices through a short-lived QR pairing token, then allows saved
  reconnects with a stored secret.
- Manages paired devices, active connections, disconnect/remove actions, and
  duplicate cleanup from the host Devices page.
- Enforces host-side permissions for connected devices, with global defaults
  and per-device overrides.
- Injects pointer, keyboard, shortcut, and text input into Windows.
- Handles optional system commands only when the effective host permission
  allows them.
- Controls the default Windows output device volume and mute state.
- Supports light, dark, and system theme modes.
- Provides startup, connection notification, pairing-window, network,
  permission, device, and appearance settings.
- Supports release packaging through a portable zip and per-user NSIS installer.

## Mobile Web Client

- Runs as a React/TypeScript PWA in modern mobile and desktop browsers, and can
  be installed to a phone or tablet home screen.
- Connects to a Windows host from a QR pairing link or saved PC profile.
- Stores saved PC profiles, device identity, device name, and per-device client
  preferences in browser storage.
- Provides trackpad input with tap-to-click, physical left/right buttons,
  two-finger scroll, horizontal scroll, pinch zoom, pointer speed, scroll
  direction, and expanded full-screen trackpad mode.
- Provides keyboard input with live typing, buffered send, navigation/editing
  keys, modifier shortcuts, optional arrow keys, optional control keys, and
  optional function keys.
- Shows optional controls only when both local client settings and
  host-reported capabilities allow them.
- Provides dictation through the browser speech recognition API when supported
  by the browser and origin.
- Provides PC volume and mute controls from the trackpad screen.
- Supports light, dark, and system theme modes.
- Supports split-mode layouts on larger screens.

## Feature Documentation Guidelines

- Keep this file at product capability level.
- Avoid listing implementation details, private types, file names, or protocol
  internals unless they define user-visible capability.
- Prefer updating this file when a new user-visible feature area is added,
  removed, or meaningfully renamed.
- Put detailed behavior, wire format, setup, release, and troubleshooting notes
  in their existing focused docs instead of expanding this page.
