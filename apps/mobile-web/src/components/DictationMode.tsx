import { Mic, Power, RotateCcw, Send } from "lucide-react";

type DictationModeProps = {
  canUseSpeech: boolean;
  dictationText: string;
  isListening: boolean;
  sendText: (text: string) => void;
  setDictationText: React.Dispatch<React.SetStateAction<string>>;
  startSpeech: () => void;
  stopSpeech: () => void;
};

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
    <section className="dictation-mode">
      <textarea value={dictationText} onChange={(event) => setDictationText(event.target.value)} placeholder="Dictated or typed text appears here" />
      <div className="command-row">
        <button onClick={isListening ? stopSpeech : startSpeech} disabled={!canUseSpeech}>
          {isListening ? <Power aria-hidden="true" /> : <Mic aria-hidden="true" />}
          <span>{isListening ? "Stop" : "Listen"}</span>
        </button>
        <button onClick={sendDictationText}>
          <Send aria-hidden="true" />
          <span>Send</span>
        </button>
        <button onClick={() => setDictationText("")}>
          <RotateCcw aria-hidden="true" />
          <span>Clear</span>
        </button>
      </div>
      {!canUseSpeech && <p className="hint">Browser speech recognition is unavailable. Use your phone keyboard dictation in the text box, then send.</p>}
    </section>
  );
}
