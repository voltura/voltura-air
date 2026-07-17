import { Mic, Power, RotateCcw, Send } from "lucide-react";

interface DictationModeProps {
  canUseSpeech: boolean;
  dictationText: string;
  isListening: boolean;
  sendText: (text: string) => void;
  setDictationText: React.Dispatch<React.SetStateAction<string>>;
  startSpeech: () => void;
  stopSpeech: () => void;
}

export function DictationMode({
  canUseSpeech,
  dictationText,
  isListening,
  sendText,
  setDictationText,
  startSpeech,
  stopSpeech
}: DictationModeProps) {
  const sendDictationText = () => {
    sendText(dictationText);
    setDictationText("");
  };

  return (
    <section className={`dictation-mode ${isListening ? "is-listening" : ""} ${canUseSpeech ? "" : "speech-unavailable"}`}>
      <div className="dictation-status">
        <Mic aria-hidden="true" />
        <div>
          <strong>{isListening ? "Listening" : canUseSpeech ? "Ready to dictate" : "Speech recognition unavailable"}</strong>
          <p>{canUseSpeech ? "Speak, edit the text, then send it to Windows." : "Use your phone keyboard dictation in the text box, then send."}</p>
        </div>
      </div>
      <textarea
        aria-label="Dictation text"
        className="dictation-textarea"
        value={dictationText}
        onChange={(event) => { setDictationText(event.target.value); }}
        placeholder="Dictated or typed text appears here"
      />
      <div className="dictation-actions" aria-label="Dictation controls">
        <button className="dictation-listen-button" onClick={isListening ? stopSpeech : startSpeech} disabled={!canUseSpeech}>
          {isListening ? <Power aria-hidden="true" /> : <Mic aria-hidden="true" />}
          <span>{isListening ? "Stop" : "Listen"}</span>
        </button>
        <button className="dictation-send-button" onClick={sendDictationText}>
          <Send aria-hidden="true" />
          <span>Send</span>
        </button>
        <button className="dictation-clear-button" onClick={() => { setDictationText(""); }}>
          <RotateCcw aria-hidden="true" />
          <span>Clear</span>
        </button>
      </div>
    </section>
  );
}
