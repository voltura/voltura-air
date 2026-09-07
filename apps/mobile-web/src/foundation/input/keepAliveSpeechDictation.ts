import { getDisplayMode } from "../platform/clientEnvironment";
import { createSpeechSession, type SpeechSession } from "./speechRecognitionSession";

export interface SpeechDestination {
  enabled: () => boolean;
  state: (state: "starting" | "listening" | "paused") => void;
  text: (text: string) => void;
  error: (message: string) => void;
}

interface SharedDictation {
  session: SpeechSession;
  destination: SpeechDestination | null;
  capturing: boolean;
  ending: boolean;
  // Results are cumulative. Retain indices only, never paused transcripts.
  finalizedThrough: number;
  observedThrough: number;
  discardThrough: number;
}

let shared: SharedDictation | null = null;

export function speechRestartGuidance() {
  return getDisplayMode() === "installed"
    ? "Speech input may need a restart. Fully close and reopen the Voltura Air app."
    : "Speech input may need a restart. Fully close and reopen your web browser.";
}

export function pauseSpeechDestination(destination: SpeechDestination) {
  const current = shared;
  if (current?.destination !== destination) {
    return;
  }
  current.destination = null;
  current.discardThrough = Math.max(current.discardThrough, current.observedThrough);
  current.session.record("text-paused");
  destination.state("paused");
}

export function resumeSpeechDestination(destination: SpeechDestination) {
  if (!destination.enabled() || document.visibilityState === "hidden") {
    return;
  }
  if (shared?.ending) {
    destination.state("paused");
    destination.error(speechRestartGuidance());
    return;
  }
  if (shared) {
    if (shared.destination === destination) {
      return;
    }
    if (shared.destination) {
      pauseSpeechDestination(shared.destination);
    }
    shared.destination = destination;
    // Exclude observed paused/interim results even if their final arrives later.
    shared.discardThrough = Math.max(shared.discardThrough, shared.observedThrough);
    shared.session.record("text-resumed");
    destination.state(shared.capturing ? "listening" : "starting");
    return;
  }

  const Api = window.SpeechRecognition ?? window.webkitSpeechRecognition;
  if (!Api) {
    destination.state("paused");
    return;
  }
  destination.state("starting");
  try {
    const recognition = new Api();
    recognition.continuous = true;
    recognition.interimResults = true;
    const hidden = () => {
      if (document.visibilityState === "hidden" && current.destination) {
        pauseSpeechDestination(current.destination);
      }
    };
    const pageHide = () => {
      if (current.destination) {
        pauseSpeechDestination(current.destination);
      }
      current.ending = true;
      session.abort();
    };
    const session = createSpeechSession(recognition, () => {
      document.removeEventListener("visibilitychange", hidden);
      window.removeEventListener("pagehide", pageHide);
      if (shared === current) {
        shared = null;
      }
      const target = current.destination;
      current.destination = null;
      target?.state("paused");
      if (target) {
        target.error(speechRestartGuidance());
      }
    });
    const current: SharedDictation = {
      session,
      destination,
      capturing: false,
      ending: false,
      finalizedThrough: -1,
      observedThrough: -1,
      discardThrough: -1,
    };
    shared = current;
    recognition.onaudiostart = () => {
      session.record("audio-start");
      current.capturing = true;
      current.destination?.state("listening");
    };
    recognition.onresult = (event) => {
      if (shared !== current || current.ending) {
        return;
      }
      const target = current.destination;
      const accepting = target?.enabled() && document.visibilityState !== "hidden";
      if (!accepting && target) {
        pauseSpeechDestination(target);
      }
      for (let index = event.resultIndex; index < event.results.length; index += 1) {
        const result = event.results[index];
        if (!result) {
          continue;
        }
        current.observedThrough = Math.max(current.observedThrough, index);
        if (!accepting) {
          current.discardThrough = Math.max(current.discardThrough, index);
        }
        if (index <= current.finalizedThrough) {
          continue;
        }
        session.record(result.isFinal ? "final-result" : "interim-result");
        if (!result.isFinal) {
          continue;
        }
        current.finalizedThrough = index;
        if (!accepting || index <= current.discardThrough) {
          session.record("result-discarded");
          continue;
        }
        const text = result[0]?.transcript?.trim();
        if (text && current.destination === target && target?.enabled()) {
          target.state("listening");
          target.text(`${text} `);
        }
      }
    };
    recognition.onerror = (event) => {
      session.record("error", event.error);
      const target = current.destination;
      if (target) {
        pauseSpeechDestination(target);
        target.error(event.error === "not-allowed" || event.error === "service-not-allowed"
          ? "Microphone access was denied. Allow microphone access and try again."
          : speechRestartGuidance());
      }
      current.ending = true;
      session.abort();
    };
    document.addEventListener("visibilitychange", hidden);
    window.addEventListener("pagehide", pageHide);
    session.start();
  } catch {
    destination.state("paused");
    destination.error(speechRestartGuidance());
  }
}
