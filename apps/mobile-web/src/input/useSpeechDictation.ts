import { useMemo, useRef, useState } from "react";

type SpeechRecognitionConstructor = new () => SpeechRecognition;

type SpeechRecognition = {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  start: () => void;
  stop: () => void;
  onresult: ((event: SpeechRecognitionEvent) => void) | null;
  onend: (() => void) | null;
  onerror: (() => void) | null;
};

type SpeechRecognitionEvent = {
  results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }>;
};

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
  const canUseSpeech = useMemo(() => Boolean(window.SpeechRecognition || window.webkitSpeechRecognition), []);

  const startSpeech = () => {
    const SpeechRecognitionApi = window.SpeechRecognition ?? window.webkitSpeechRecognition;
    if (!SpeechRecognitionApi) {
      return;
    }

    const recognition = new SpeechRecognitionApi();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = navigator.language || "en-US";
    recognition.onresult = (event) => {
      let finalText = "";
      for (let index = 0; index < event.results.length; index += 1) {
        const result = event.results[index];
        if (result.isFinal) {
          finalText += result[0].transcript;
        }
      }
      if (finalText.trim().length > 0) {
        const text = `${finalText.trim()} `;
        setDictationText((current) => `${current}${text}`);
        sendText(text);
      }
    };
    recognition.onend = () => setIsListening(false);
    recognition.onerror = () => setIsListening(false);
    recognition.start();
    speechRef.current = recognition;
    setIsListening(true);
  };

  const stopSpeech = () => {
    speechRef.current?.stop();
    setIsListening(false);
  };

  return { canUseSpeech, dictationText, isListening, setDictationText, startSpeech, stopSpeech };
}
