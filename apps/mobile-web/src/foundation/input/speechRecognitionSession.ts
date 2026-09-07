export type SpeechRecognitionConstructor = new () => SpeechRecognition;

interface SpeechRecognition {
  continuous: boolean;
  interimResults: boolean;
  abort: () => void;
  stop: () => void;
  start: () => void;
  onstart: (() => void) | null;
  onaudiostart: (() => void) | null;
  onaudioend: (() => void) | null;
  onspeechstart: (() => void) | null;
  onresult:
    | ((event: {
        resultIndex: number;
        results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal?: boolean }>;
      }) => void)
    | null;
  onend: (() => void) | null;
  onerror: ((event: { error?: string }) => void) | null;
}

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor;
    webkitSpeechRecognition?: SpeechRecognitionConstructor;
  }
}

type SpeechEvent =
  | "start-requested"
  | "started"
  | "audio-start"
  | "audio-end"
  | "speech-start"
  | "interim-result"
  | "final-result"
  | "text-paused"
  | "text-resumed"
  | "result-discarded"
  | "error"
  | "stop-requested"
  | "stop-failed"
  | "stop-timeout"
  | "abort-requested"
  | "abort-failed"
  | "end"
  | "end-timeout";

export interface SpeechSession {
  ended: Promise<boolean>;
  start: () => void;
  stop: () => void;
  abort: () => void;
  finish: (ended: boolean) => void;
  record: (event: SpeechEvent, error?: string) => void;
}

let activeSession: SpeechSession | null = null;
let nextSession = 0;
const events: { session: number; event: SpeechEvent; elapsedMs: number; error?: string }[] = [];
const errorCodes = new Set([
  "no-speech",
  "aborted",
  "audio-capture",
  "network",
  "not-allowed",
  "service-not-allowed",
  "bad-grammar",
  "language-not-supported",
  "start-failed",
]);

export const getActiveSpeechSession = () => activeSession;

// Bounded, memory-only metadata. Never store audio, transcripts, or browser messages.
// Included only when the user requests the existing mobile diagnostics export.
export function getSpeechDiagnostics() {
  return { lifecycleVersion: 4, events: events.map((event) => ({ ...event })) };
}

export function createSpeechSession(
  recognition: SpeechRecognition,
  onFinish: () => void,
): SpeechSession {
  const id = ++nextSession;
  const startedAt = performance.now();
  let resolve!: (ended: boolean) => void;
  let finished = false;
  let recognitionStarted = false;
  let ending: "stop" | "abort" | null = null;
  let endTimer: ReturnType<typeof setTimeout> | undefined;
  const requestEnd = (method: "stop" | "abort") => {
    if (finished || ending === "abort" || (method === "stop" && ending === "stop")) {
      return;
    }
    ending = method;
    clearTimeout(endTimer);
    session.record(method === "stop" ? "stop-requested" : "abort-requested");
    if (!recognitionStarted) {
      session.finish(true);
      return;
    }
    // Normal completion first. A stalled completion may be cancelled, but no
    // new recognition starts until end; missing end fails the queued attempt.
    endTimer = setTimeout(() => {
      if (method === "stop") {
        session.record("stop-timeout");
        session.abort();
      } else {
        session.record("end-timeout");
        session.finish(false);
      }
    }, 2000);
    try {
      recognition[method]();
    } catch {
      session.record(method === "stop" ? "stop-failed" : "abort-failed");
      if (method === "stop") {
        session.abort();
      } else {
        session.finish(false);
      }
    }
  };
  const session: SpeechSession = {
    ended: new Promise<boolean>((done) => {
      resolve = done;
    }),
    start() {
      if (finished || recognitionStarted) {
        return;
      }
      recognitionStarted = true;
      session.record("start-requested");
      try {
        recognition.start();
      } catch {
        recognitionStarted = false;
        recognition.onerror?.({ error: "start-failed" });
        session.abort();
      }
    },
    record(event, error) {
      if (finished) {
        return;
      }
      events.push({
        session: id,
        event,
        elapsedMs: Math.round(performance.now() - startedAt),
        ...(error === undefined ? {} : { error: errorCodes.has(error) ? error : "unknown" }),
      });
      if (events.length > 48) {
        events.shift();
      }
    },
    finish(ended) {
      if (finished) {
        return;
      }
      finished = true;
      clearTimeout(endTimer);
      recognition.onstart = recognition.onaudiostart = recognition.onaudioend = null;
      recognition.onspeechstart =
        recognition.onresult =
        recognition.onerror =
        recognition.onend =
          null;
      if (activeSession === session) {
        activeSession = null;
      }
      onFinish();
      resolve(ended);
    },
    stop: () => requestEnd("stop"),
    abort: () => requestEnd("abort"),
  };
  recognition.onstart = () => session.record("started");
  recognition.onaudioend = () => session.record("audio-end");
  recognition.onspeechstart = () => session.record("speech-start");
  recognition.onend = () => {
    session.record("end");
    session.finish(true);
  };
  activeSession = session;
  return session;
}
