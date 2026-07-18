import { useEffect, useRef, useState } from "react";

type SpeechRecognitionConstructor = new () => SpeechRecognition;

interface SpeechRecognition {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  start: () => void;
  stop: () => void;
  onresult: ((event: SpeechRecognitionEvent) => void) | null;
  onend: (() => void) | null;
  onerror: (() => void) | null;
}

interface SpeechRecognitionEvent {
  results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }>;
}

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor;
    webkitSpeechRecognition?: SpeechRecognitionConstructor;
  }
}

export function useSpeechDictation(sendText: (text: string) => void) {
  const [dictationText, setDictationText] = useState("");
  const [isListening, setIsListening] = useState(false);
  const speechRef = useRef<SpeechRecognition | null>(null);
  const canUseSpeech = Boolean(window.SpeechRecognition ?? window.webkitSpeechRecognition);

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

  const startSpeech = () => {
    if (speechRef.current) {
      return;
    }

    const SpeechRecognitionApi = window.SpeechRecognition ?? window.webkitSpeechRecognition;
    if (!SpeechRecognitionApi) {
      return;
    }

    const recognition = new SpeechRecognitionApi();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = navigator.language.trim().length > 0 ? navigator.language : "en-US";
    recognition.onresult = (event) => {
      if (speechRef.current !== recognition) {
        return;
      }

      let finalText = "";
      for (const result of Array.from(event.results)) {
        if (result.isFinal) {
          finalText += result[0]?.transcript ?? "";
        }
      }
      if (finalText.trim().length > 0) {
        const text = `${finalText.trim()} `;
        setDictationText((current) => `${current}${text}`);
        sendText(text);
      }
    };
    speechRef.current = recognition;
    recognition.onend = () => {
      if (speechRef.current === recognition) {
        speechRef.current = null;
        setIsListening(false);
      }
    };
    recognition.onerror = recognition.onend;

    try {
      recognition.start();
      setIsListening(true);
    } catch {
      if (speechRef.current === recognition) {
        speechRef.current = null;
      }
      setIsListening(false);
    }
  };

  const stopSpeech = () => {
    stopRecognition(speechRef, setIsListening);
  };

  return { canUseSpeech, dictationText, isListening, setDictationText, startSpeech, stopSpeech };
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
