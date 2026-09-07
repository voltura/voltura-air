import { Mic, Power, RotateCcw, Send } from "lucide-react";

interface DictationModeProps {
  canUseSpeech: boolean;
  dictationText: string;
  isListening: boolean;
  isStarting?: boolean;
  sendText: (text: string) => void;
  setDictationText: React.Dispatch<React.SetStateAction<string>>;
  speechError: string | null;
  speechNotice?: string | null;
  startSpeech: () => void;
  stopSpeech: () => void;
}

export function DictationMode({
  canUseSpeech,
  dictationText,
  isListening,
  isStarting = false,
  sendText,
  setDictationText,
  speechError,
  speechNotice,
  startSpeech,
  stopSpeech,
}: DictationModeProps) {
  const speechActive = isListening || isStarting;
  const sendDictationText = () => {
    sendText(dictationText);
    setDictationText("");
  };

  return (
    <section
      className={`dictation-mode ${isListening ? "is-listening" : ""} ${canUseSpeech ? "" : "speech-unavailable"}`}
    >
      <div className="dictation-status">
        <Mic aria-hidden="true" />
        <div>
          <strong>
            {isListening
              ? "Listening"
              : isStarting
                ? "Starting microphone…"
                : speechError
                  ? "Try again"
                  : canUseSpeech
                    ? "Ready to dictate"
                    : "Speech recognition unavailable"}
          </strong>
          {speechError ? (
            <p className="dictation-feedback error" role="alert">
              {speechError}
            </p>
          ) : (
            <p>
              {canUseSpeech
                ? "Speak to send recognized text to your PC, or type and send it."
                : "Use your phone keyboard dictation in the text box, then send."}
            </p>
          )}
          {speechNotice && <p className="dictation-feedback" role="status">{speechNotice}</p>}
          {canUseSpeech && (
            <p>Pause stops text input, not the microphone. Speech recognition stays active across screens;
              the browser may still process speech. Close the app or browser to release the microphone.</p>
          )}
        </div>
      </div>
      <textarea
        aria-label="Dictation text"
        className="dictation-textarea"
        value={dictationText}
        onChange={(event) => {
          setDictationText(event.target.value);
        }}
        placeholder="Dictated or typed text appears here"
      />
      <div className="dictation-actions" aria-label="Dictation controls">
        {canUseSpeech && (
          <button
            type="button"
            className="dictation-listen-button"
            onClick={speechActive ? stopSpeech : startSpeech}
            aria-pressed={speechActive}
          >
            {speechActive ? <Power aria-hidden="true" /> : <Mic aria-hidden="true" />}
            <span>{speechActive ? "Pause" : "Listen"}</span>
          </button>
        )}
        <button type="button" className="dictation-send-button" onClick={sendDictationText}>
          <Send aria-hidden="true" />
          <span>Send</span>
        </button>
        <button
          type="button"
          className="dictation-clear-button"
          onClick={() => {
            setDictationText("");
          }}
        >
          <RotateCcw aria-hidden="true" />
          <span>Clear</span>
        </button>
      </div>
    </section>
  );
}
