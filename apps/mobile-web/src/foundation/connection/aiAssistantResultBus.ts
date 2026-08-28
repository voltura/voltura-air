import type { AiAssistantServerMessage } from "../protocol/messages";

type Listener = (message: AiAssistantServerMessage) => void;
const listeners = new Set<Listener>();

export function publishAiAssistantResult(message: AiAssistantServerMessage): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribeAiAssistantResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
