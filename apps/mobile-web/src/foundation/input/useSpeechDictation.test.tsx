import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useSpeechDictation } from "./useSpeechDictation";

class MockSpeechRecognition {
  static instances: MockSpeechRecognition[] = [];

  continuous = false;
  interimResults = false;
  lang = "";
  onresult: ((event: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }> }) => void) | null = null;
  onend: (() => void) | null = null;
  onerror: (() => void) | null = null;
  start = vi.fn();
  stop = vi.fn();

  constructor() {
    MockSpeechRecognition.instances.push(this);
  }
}

function DictationHarness({ onText }: { onText?: (text: string) => void } = {}) {
  const { dictationText, isListening, startSpeech } = useSpeechDictation();

  return <><button type="button" onClick={startSpeech}>{isListening ? "Listening" : "Start"}</button><output data-testid="dictation-draft">{dictationText}</output>{onText && <button type="button" onClick={() => { onText(dictationText); }}>Send</button>}</>;
}

afterEach(() => {
  MockSpeechRecognition.instances = [];
  vi.unstubAllGlobals();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
});

describe("useSpeechDictation", () => {
  it("owns at most one recognition session", () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    render(<DictationHarness />);

    const button = screen.getByRole("button", { name: "Start" });
    fireEvent.click(button);
    fireEvent.click(button);

    expect(MockSpeechRecognition.instances).toHaveLength(1);
  });

  it("stops recognition when the page is hidden", () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    render(<DictationHarness />);

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const recognition = MockSpeechRecognition.instances.at(0)!;

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    fireEvent(document, new Event("visibilitychange"));

    expect(recognition.start).toHaveBeenCalledOnce();
    expect(recognition.stop).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
  });

  it("stops recognition when the dictation owner unmounts", () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    const view = render(<DictationHarness />);

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const recognition = MockSpeechRecognition.instances.at(0)!;
    view.unmount();

    expect(recognition.stop).toHaveBeenCalledOnce();
  });

  it("keeps final speech as an editable draft and ignores already-reported final results", () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    const onText = vi.fn();
    render(<DictationHarness onText={onText} />);

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const recognition = MockSpeechRecognition.instances.at(0)!;
    const hello = Object.assign([{ transcript: "hello" }], { isFinal: true });
    const world = Object.assign([{ transcript: "world" }], { isFinal: true });
    act(() => { recognition.onresult?.({ resultIndex: 0, results: [hello] }); });
    act(() => { recognition.onresult?.({ resultIndex: 1, results: [hello, world] }); });

    expect(screen.getByTestId("dictation-draft").textContent).toBe("hello world ");
    expect(onText).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(onText).toHaveBeenCalledOnce();
    expect(onText).toHaveBeenCalledWith("hello world ");
  });
});
