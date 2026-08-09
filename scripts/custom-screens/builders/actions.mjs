export const shortcut = (key, modifiers = []) => ({ kind: "shortcut", key, modifiers });
export const builtIn = (builtIn) => ({ kind: "builtIn", builtIn });
export const text = (value) => ({ kind: "text", text: value });
export const openWebsite = (url) => ({ kind: "urlOpen", url });
export const knownApp = (actionId) => ({ kind: "knownApp", actionId });
export const hostAction = (actionId) => ({ kind: "hostAction", actionId });

export const allowedPortableActionKinds = new Set([
  "text", "shortcut", "builtIn", "urlOpen", "knownApp", "hostAction"
]);
