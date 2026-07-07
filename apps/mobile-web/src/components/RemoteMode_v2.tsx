// Draft replacement plan for RemoteMode.tsx.
// This file is intentionally not imported by the app yet.
//
// Kodi power menu change to apply to RemoteMode.tsx:
// 1. Add Power to the lucide-react imports.
// 2. Add `powerMenu?: RemoteShortcut;` to RemoteShortcutMap.
// 3. Add `powerMenu: { key: "S" }` to remoteShortcutMaps.kodi.
// 4. Add `const sendPowerMenu = () => shortcuts.powerMenu && sendShortcut(shortcuts.powerMenu);`.
// 5. Replace the Kodi-mode media grid Space button with a Power icon button.
//
// Exact JSX replacement for the current Space button:
//
// {isKodiMode ? (
//   <RemoteButton label="Power menu" title="Kodi power menu" className="remote-icon-button" onClick={sendPowerMenu}>
//     <Power aria-hidden="true" />
//   </RemoteButton>
// ) : (
//   <RemoteButton label="Space" title={modeCopy.spaceTitle} onClick={sendSpace}>
//     <span>Space</span>
//   </RemoteButton>
// )}
//
// This preserves remote height because it reuses the existing Space slot.

export {};
