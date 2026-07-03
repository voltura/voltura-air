import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DictationMode } from "./DictationMode";

function DictationModeHarness({ sendText = vi.fn() }: { sendText?: (text: string) => void }) {
  const [dictationText, setDictationText] = useState("Hello Windows");

  return (
    <DictationMode
      canUseSpeech
      dictationText={dictationText}
      isListening={false}
      sendText={sendText}
      setDictationText={setDictationText}
      startSpeech={vi.fn()}
      stopSpeech={vi.fn()}
    />
  );
}

describe("DictationMode", () => {
  it("clears the dictation text after sending", () => {
    const sendText = vi.fn();
    render(<DictationModeHarness sendText={sendText} />);

    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(sendText).toHaveBeenCalledExactlyOnceWith("Hello Windows");
    expect(screen.getByRole("textbox")).toHaveProperty("value", "");
  });
});
