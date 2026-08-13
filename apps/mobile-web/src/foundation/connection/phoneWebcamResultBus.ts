import type { PhoneWebcamServerMessage } from "../protocol/messages";

type Listener = (message: PhoneWebcamServerMessage) => void;
const listeners = new Set<Listener>();

export function publishPhoneWebcamResult(message: PhoneWebcamServerMessage): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribePhoneWebcamResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
