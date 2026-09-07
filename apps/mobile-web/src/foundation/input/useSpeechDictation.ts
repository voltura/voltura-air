import { useEffect, useLayoutEffect, useRef, useState } from "react";
import {
  pauseSpeechDestination,
  resumeSpeechDestination,
  speechRestartGuidance,
  type SpeechDestination,
} from "./keepAliveSpeechDictation";

export function useSpeechDictation(sendText: (text: string) => void, enabled = true) {
  const [dictationText, setDictationText] = useState("");
  const [isListening, setIsListening] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [speechError, setSpeechError] = useState<string | null>(null);
  const [speechNotice, setSpeechNotice] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const [requested, setRequested] = useState(false);
  const activeRef = useRef(false);
  const enabledRef = useRef(enabled);
  const sendTextRef = useRef(sendText);
  const canUseSpeech = Boolean(window.SpeechRecognition ?? window.webkitSpeechRecognition);

  const [destination] = useState<SpeechDestination>(() => ({
    enabled: () => enabledRef.current && activeRef.current,
    state: (state) => {
      setIsStarting(state === "starting");
      setIsListening(state === "listening");
      if (state === "paused") {
        activeRef.current = false;
        setRequested(false);
        setSpeechNotice(null);
      }
    },
    text: (text) => {
      setSpeechNotice(null);
      setProgress((value) => value + 1);
      setDictationText((current) => `${current}${text}`);
      sendTextRef.current(text);
    },
    error: (message) => {
      activeRef.current = false;
      setRequested(false);
      setSpeechNotice(null);
      setSpeechError(message);
    },
  }));

  useLayoutEffect(() => {
    enabledRef.current = enabled;
    sendTextRef.current = sendText;
    if (!enabled) {
      pauseSpeechDestination(destination);
    }
  }, [enabled, sendText, destination]);

  useLayoutEffect(
    () => () => {
      activeRef.current = false;
      pauseSpeechDestination(destination);
    },
    [destination],
  );

  // One cancellable inactivity timer, only while the user requests dictation.
  // Audio-start is not evidence that any text reached the app.
  useEffect(() => {
    if (!requested || !enabled) {
      return;
    }
    const timer = setTimeout(() => {
      if (activeRef.current && enabledRef.current && document.visibilityState !== "hidden") {
        setSpeechNotice(
          `No text received for 15 seconds. If you have been speaking: ${speechRestartGuidance()}`,
        );
      }
    }, 15000);
    return () => clearTimeout(timer);
  }, [requested, enabled, progress]);

  const startSpeech = () => {
    if (!enabled || activeRef.current || !canUseSpeech || document.visibilityState === "hidden") {
      return;
    }
    activeRef.current = true;
    setRequested(true);
    setSpeechError(null);
    setSpeechNotice(null);
    resumeSpeechDestination(destination);
  };

  return {
    canUseSpeech,
    dictationText,
    isListening,
    isStarting,
    setDictationText,
    speechError,
    speechNotice,
    startSpeech,
    stopSpeech: () => pauseSpeechDestination(destination),
  };
}
