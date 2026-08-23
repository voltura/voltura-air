import { useEffect, useRef, useState } from "react";

type SpeechRecognitionConstructor = new () => SpeechRecognition;

interface SpeechRecognition {
  continuous: boolean;
  interimResults: boolean;
  start: () => void;
  stop: () => void;
  onresult: ((event: SpeechRecognitionEvent) => void) | null;
  onend: (() => void) | null;
  onerror: ((event: SpeechRecognitionErrorEvent) => void) | null;
}

interface SpeechRecognitionEvent {
  resultIndex: number;
  results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }>;
}

interface SpeechRecognitionErrorEvent {
  error?: string;
}

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor;
    webkitSpeechRecognition?: SpeechRecognitionConstructor;
  }
}

export function useSpeechDictation(sendText: (text: string) => void, enabled = true) {
  const [dictationText, setDictationText] = useState("");
  const [isListening, setIsListening] = useState(false);
  const [speechError, setSpeechError] = useState<string | null>(null);
  const speechRef = useRef<SpeechRecognition | null>(null);
  const sendTextRef = useRef(sendText);
  const canUseSpeech = Boolean(window.SpeechRecognition ?? window.webkitSpeechRecognition);

  useEffect(() => {
    sendTextRef.current = sendText;
  }, [sendText]);

  useEffect(() => {
    const stopWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        stopRecognition(speechRef, setIsListening);
      }
    };

    document.addEventListener("visibilitychange", stopWhenHidden);
    return () => {
      document.removeEventListener("visibilitychange", stopWhenHidden);
      stopRecognition(speechRef, setIsListening);
    };
  }, []);

  useEffect(() => {
    if (!enabled) {
      stopRecognition(speechRef, setIsListening);
    }
  }, [enabled]);

  const startSpeech = () => {
    if (!enabled || speechRef.current) {
      return;
    }

    const SpeechRecognitionApi = window.SpeechRecognition ?? window.webkitSpeechRecognition;
    if (!SpeechRecognitionApi) {
      return;
    }

    setSpeechError(null);
    const recognition = new SpeechRecognitionApi();
    recognition.continuous = true;
    recognition.interimResults = true;
    const finalizedResultIndexes = new Set<number>();
    recognition.onresult = (event) => {
      if (speechRef.current !== recognition) {
        return;
      }

      for (const [offset, result] of Array.from(event.results).slice(event.resultIndex).entries()) {
        const resultIndex = event.resultIndex + offset;
        if (!result.isFinal || finalizedResultIndexes.has(resultIndex)) {
          continue;
        }

        finalizedResultIndexes.add(resultIndex);
        const text = result[0]?.transcript?.trim() ?? "";
        if (text.length === 0) {
          continue;
        }

        const textToSend = `${text} `;
        setDictationText((current) => `${current}${textToSend}`);
        sendTextRef.current(textToSend);
      }
    };
    speechRef.current = recognition;
    recognition.onend = () => {
      if (speechRef.current === recognition) {
        speechRef.current = null;
        setIsListening(false);
      }
    };
    recognition.onerror = (event) => {
      if (speechRef.current !== recognition) {
        return;
      }

      stopRecognition(speechRef, setIsListening);
      setSpeechError(getSpeechErrorMessage(event.error));
    };

    try {
      recognition.start();
      setIsListening(true);
    } catch {
      if (speechRef.current === recognition) {
        speechRef.current = null;
      }
      setIsListening(false);
      setSpeechError("Speech recognition could not start. Try again.");
    }
  };

  const stopSpeech = () => {
    stopRecognition(speechRef, setIsListening);
  };

  return { canUseSpeech, dictationText, isListening, setDictationText, speechError, startSpeech, stopSpeech };
}

function getSpeechErrorMessage(error: string | undefined): string {
  if (error === "not-allowed" || error === "service-not-allowed") {
    return "Microphone access was denied. Allow microphone access and try again.";
  }

  return "Speech recognition failed. Try again.";
}

function stopRecognition(
  speechRef: React.RefObject<SpeechRecognition | null>,
  setIsListening: React.Dispatch<React.SetStateAction<boolean>>
) {
  const recognition = speechRef.current;
  speechRef.current = null;
  setIsListening(false);

  try {
    recognition?.stop();
  } catch {
    // The recognition instance may already be stopped by the browser.
  }
}
