# Kodi power menu remote button new logic

This sidecar file documents the intended implementation for `apps/mobile-web/src/components/RemoteMode.tsx`.

Goal:
- Add a Kodi-only power/menu button to the existing Kodi remote UI.
- The button sends Kodi shortcut key `S`, which opens Kodi's power menu.
- Do not add height to the remote layout.
- Keep navigation ring, side actions, FN/Main, and mini-trackpad behavior unchanged.

Recommended placement:
- Replace the existing `Space` media-grid button only when `isKodiMode` is true.
- This avoids adding another row or changing the grid height.
- Kodi already has `Play/Pause` mapped to Space, so the existing separate Space button is duplicate in Kodi mode.

Patch details:

1. Add `Power` to the lucide-react import list:

```tsx
import {
  ...
  Play,
  Power,
  Rewind,
  ...
} from "lucide-react";
```

2. Add a new optional shortcut field to `RemoteShortcutMap`:

```tsx
powerMenu?: RemoteShortcut;
```

3. Add the Kodi shortcut to `remoteShortcutMaps.kodi`:

```tsx
powerMenu: { key: "S" }
```

4. Add the sender beside the existing Kodi sender helpers:

```tsx
const sendPowerMenu = () => shortcuts.powerMenu && sendShortcut(shortcuts.powerMenu);
```

5. Replace the existing unconditional Space button in the media grid:

Current:

```tsx
<RemoteButton label="Space" title={modeCopy.spaceTitle} onClick={sendSpace}>
  <span>Space</span>
</RemoteButton>
```

With:

```tsx
{isKodiMode ? (
  <RemoteButton label="Power menu" title="Kodi power menu" className="remote-icon-button" onClick={sendPowerMenu}>
    <Power aria-hidden="true" />
  </RemoteButton>
) : (
  <RemoteButton label="Space" title={modeCopy.spaceTitle} onClick={sendSpace}>
    <span>Space</span>
  </RemoteButton>
)}
```

Acceptance checks:
- In Kodi mode, the new power icon button sends `S`.
- In non-Kodi modes, the Space button remains unchanged.
- No new rows or height are added.
- Kodi Stop, Info, Subtitles, navigation ring, mini-trackpad, and FN/Main behavior remain unchanged.
