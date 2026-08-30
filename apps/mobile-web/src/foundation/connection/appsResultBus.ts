import type { AppsServerMessage } from "../protocol/messages";

type Listener = (message: AppsServerMessage) => void;
const listeners = new Set<Listener>();

export function publishAppsResult(message: AppsServerMessage): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribeAppsResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
