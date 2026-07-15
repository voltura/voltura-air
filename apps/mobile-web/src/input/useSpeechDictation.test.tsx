import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useSpeechDictation } from "./useSpeechDictation";

class MockSpeechRecognition {
  static instances: MockSpeechRecognition[] = [];

  continuous = false;
  interimResults = false;
  lang = "";
  onresult: ((event: { results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }> }) => void) | null = null;
  onend: (() => void) | null = null;
  onerror: (() => void) | null = null;
  start = vi.fn();
  stop = vi.fn();

  constructor() {
    MockSpeechRecognition.instances.push(this);
  }
}

function DictationHarness() {
  const { isListening, startSpeech } = useSpeechDictation(vi.fn());

  return <button type="button" onClick={startSpeech}>{isListening ? "Listening" : "Start"}</button>;
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
    const recognition = MockSpeechRecognition.instances[0];

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
    const recognition = MockSpeechRecognition.instances[0];
    view.unmount();

    expect(recognition.stop).toHaveBeenCalledOnce();
  });
});
