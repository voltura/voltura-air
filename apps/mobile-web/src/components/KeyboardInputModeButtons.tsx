export type KeyboardInputMode = "text" | "numeric";

type KeyboardInputModeButtonsProps = {
  inputMode: KeyboardInputMode;
  onInputModeChange: (inputMode: KeyboardInputMode) => void;
};

export function KeyboardInputModeButtons({ inputMode, onInputModeChange }: KeyboardInputModeButtonsProps) {
  return (
    <div className="keyboard-input-mode-buttons segmented-control" role="tablist" aria-label="Device keyboard type">
      <button
        type="button"
        className={inputMode === "text" ? "active" : ""}
        aria-label="Show regular keyboard"
        aria-selected={inputMode === "text"}
        role="tab"
        onClick={() => onInputModeChange("text")}
      >
        ABC
      </button>
      <button
        type="button"
        className={inputMode === "numeric" ? "active" : ""}
        aria-label="Show numeric keyboard"
        aria-selected={inputMode === "numeric"}
        role="tab"
        onClick={() => onInputModeChange("numeric")}
      >
        123
      </button>
    </div>
  );
}
