import type { TerminalServerMessage } from "../protocol/messages";

type Listener = (message: TerminalServerMessage) => void;
const listeners = new Set<Listener>();

export function publishTerminalResult(message: TerminalServerMessage): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribeTerminalResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
