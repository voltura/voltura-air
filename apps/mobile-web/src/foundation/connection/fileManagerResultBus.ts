import type { FileManagerServerMessage } from "../protocol/messages";

type Listener = (message: FileManagerServerMessage) => void;
const listeners = new Set<Listener>();

export function publishFileManagerResult(message: FileManagerServerMessage): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribeFileManagerResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
