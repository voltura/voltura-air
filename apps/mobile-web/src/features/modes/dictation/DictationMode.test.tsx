import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DictationMode } from "./DictationMode";

function DictationModeHarness({
  connected = true,
  canUseSpeech = true,
  sendText = vi.fn(),
  isStarting = false,
  stopSpeech = vi.fn(),
}: {
  connected?: boolean;
  canUseSpeech?: boolean;
  sendText?: (text: string) => void;
  isStarting?: boolean;
  stopSpeech?: () => void;
}) {
  const [dictationText, setDictationText] = useState("Hello Windows");

  return (
    <DictationMode
      connected={connected}
      canUseSpeech={canUseSpeech}
      dictationText={dictationText}
      isListening={false}
      isStarting={isStarting}
      sendText={sendText}
      setDictationText={setDictationText}
      speechError={null}
      startSpeech={vi.fn()}
      stopSpeech={stopSpeech}
    />
  );
}

describe("DictationMode", () => {
  it("preserves dictated text when Send is pressed while disconnected", () => {
    const sendText = vi.fn();
    render(<DictationModeHarness connected={false} sendText={sendText} />);
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(sendText).not.toHaveBeenCalled();
    expect(screen.getByRole<HTMLTextAreaElement>("textbox").value).toBe("Hello Windows");
  });
  it("shows starting without claiming to listen and keeps Pause available", () => {
    const stopSpeech = vi.fn();
    render(<DictationModeHarness isStarting stopSpeech={stopSpeech} />);
    expect(screen.getByText("Starting microphone…")).toBeTruthy();
    expect(screen.queryByText("Listening")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Pause" }));
    expect(stopSpeech).toHaveBeenCalledOnce();
  });
  it("clears the dictation text after sending", () => {
    const sendText = vi.fn();
    render(<DictationModeHarness sendText={sendText} />);

    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(sendText).toHaveBeenCalledExactlyOnceWith("Hello Windows");
    expect(screen.getByRole("textbox")).toHaveProperty("value", "");
  });

  it("preserves a draft that exceeds the text input limit", () => {
    const sendText = vi.fn();
    render(<DictationModeHarness sendText={sendText} />);
    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    const draft = "😀".repeat(2049);
    fireEvent.change(textarea, { target: { value: draft } });
    const send = screen.getByRole("button", { name: "Send" });
    expect(screen.getByRole("alert").textContent).toMatch(/4,096 UTF-16 units.*😀 count as two/su);
    expect(send).toHaveProperty("disabled", true);
    fireEvent.click(send);
    expect(sendText).not.toHaveBeenCalled();
    expect(textarea.value).toBe(draft);
  });

  it("omits speech controls when the browser has no speech recognition API", () => {
    render(<DictationModeHarness canUseSpeech={false} />);

    expect(screen.queryByRole("button", { name: "Listen" })).toBeNull();
  });
});
