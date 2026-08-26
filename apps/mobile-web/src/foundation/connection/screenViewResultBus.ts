import type {
  ScreenViewAnswerResultMessage,
  ScreenViewEndedMessage,
  ScreenViewSourceResultMessage,
  ScreenViewSourcesResultMessage,
  ScreenViewStartResultMessage,
  ScreenViewStopResultMessage,
} from "../protocol/messages";

export type ScreenViewControlResult =
  | ScreenViewSourcesResultMessage
  | ScreenViewStartResultMessage
  | ScreenViewAnswerResultMessage
  | ScreenViewSourceResultMessage
  | ScreenViewStopResultMessage
  | ScreenViewEndedMessage;
type Listener = (message: ScreenViewControlResult) => void;
const listeners = new Set<Listener>();

export function publishScreenViewResult(message: ScreenViewControlResult): void {
  for (const listener of listeners) {
    listener(message);
  }
}

export function subscribeScreenViewResults(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
