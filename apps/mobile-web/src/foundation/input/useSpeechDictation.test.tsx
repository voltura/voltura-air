import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useSpeechDictation } from "./useSpeechDictation";
import { getActiveSpeechSession, getSpeechDiagnostics } from "./speechRecognitionSession";

class MockSpeechRecognition {
  static instances: MockSpeechRecognition[] = [];
  static deferEnd = false;
  static deferStart = false;

  continuous = false;
  interimResults = false;
  lang = "";
  onresult:
    | ((event: {
        resultIndex: number;
        results: ArrayLike<
          ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }
        >;
      }) => void)
    | null = null;
  onend: (() => void) | null = null;
  onstart: (() => void) | null = null;
  onaudiostart: (() => void) | null = null;
  onaudioend: (() => void) | null = null;
  onspeechstart: (() => void) | null = null;
  onerror: ((event: { error?: string }) => void) | null = null;
  abort = vi.fn(() => {
    if (!MockSpeechRecognition.deferEnd) {
      this.onend?.();
    }
  });
  start = vi.fn(() => {
    if (!MockSpeechRecognition.deferStart) {
      this.onstart?.();
      this.onaudiostart?.();
    }
  });
  stop = vi.fn(() => {
    if (!MockSpeechRecognition.deferEnd) {
      this.onend?.();
    }
  });

  constructor() {
    MockSpeechRecognition.instances.push(this);
  }
}

function installMockSpeechRecognition() {
  vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === "(display-mode: browser)",
  }));
}

function DictationHarness({
  active = true,
  onText = vi.fn(),
}: { active?: boolean; onText?: (text: string) => void } = {}) {
  const {
    dictationText,
    isListening,
    isStarting,
    speechError,
    speechNotice,
    startSpeech,
    stopSpeech,
  } = useSpeechDictation(onText, active);

  return (
    <>
      <button type="button" onClick={startSpeech}>
        {isListening ? "Listening" : isStarting ? "Starting" : "Start"}
      </button>
      <button type="button" onClick={stopSpeech}>
        Stop
      </button>
      <output data-testid="dictation-draft">{dictationText}</output>
      <output data-testid="speech-error">{speechError ?? ""}</output>
      <output data-testid="speech-notice">{speechNotice ?? ""}</output>
    </>
  );
}

afterEach(() => {
  cleanup();
  getActiveSpeechSession()?.finish(true);
  vi.useRealTimers();
  MockSpeechRecognition.instances = [];
  MockSpeechRecognition.deferEnd = false;
  MockSpeechRecognition.deferStart = false;
  vi.unstubAllGlobals();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
});

function final(text: string) {
  return Object.assign([{ transcript: text }], { isFinal: true });
}

describe("keep-alive dictation", () => {
  it("keeps one recognizer across pause and both page-switch directions", () => {
    installMockSpeechRecognition();
    const view = render(<DictationHarness key="assistant" />);
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const native = MockSpeechRecognition.instances[0]!;
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    for (const key of ["dictation", "assistant-again"]) {
      view.rerender(<DictationHarness key={key} />);
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
    }
    expect(MockSpeechRecognition.instances).toHaveLength(1);
    expect(native.start).toHaveBeenCalledOnce();
    expect(native.stop).not.toHaveBeenCalled();
    expect(native.abort).not.toHaveBeenCalled();
  });

  it("discards paused words and delayed final revisions without replaying old results", () => {
    installMockSpeechRecognition();
    const onText = vi.fn();
    render(<DictationHarness onText={onText} />);
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    const native = MockSpeechRecognition.instances[0]!;
    const first = final("first");
    const paused = Object.assign([{ transcript: "private" }], { isFinal: false });
    act(() => native.onresult?.({ resultIndex: 0, results: [first] }));
    fireEvent.click(screen.getByRole("button", { name: "Stop" }));
    act(() => native.onresult?.({ resultIndex: 1, results: [first, paused] }));
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    act(() => native.onresult?.({ resultIndex: 1, results: [first, final("private")] }));
    const resumed = [first, final("private"), final("resumed")];
    act(() => native.onresult?.({ resultIndex: 2, results: resumed }));
    act(() => native.onresult?.({ resultIndex: 0, results: resumed }));
    expect(onText.mock.calls).toEqual([["first "], ["resumed "]]);
    expect(getSpeechDiagnostics().events).toContainEqual(
      expect.objectContaining({ event: "result-discarded" }),
    );
    expect(JSON.stringify(getSpeechDiagnostics())).not.toContain("private");
  });

  it.each(["disable", "hide", "unmount"])(
    "detaches delivery without killing capture on %s",
    (action) => {
      installMockSpeechRecognition();
      const onText = vi.fn();
      const view = render(<DictationHarness onText={onText} />);
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      const native = MockSpeechRecognition.instances[0]!;
      if (action === "disable") {
        view.rerender(<DictationHarness active={false} onText={onText} />);
      }
      if (action === "unmount") {
        view.unmount();
      }
      if (action === "hide") {
        Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
        fireEvent(document, new Event("visibilitychange"));
        Object.defineProperty(document, "visibilityState", {
          configurable: true,
          value: "visible",
        });
        fireEvent(document, new Event("visibilitychange"));
      }
      act(() => native.onresult?.({ resultIndex: 0, results: [final("ignored")] }));
      expect(onText).not.toHaveBeenCalled();
      expect(native.abort).not.toHaveBeenCalled();
      expect(native.stop).not.toHaveBeenCalled();
    },
  );

  it.each(["browser", "installed"])(
    "shows conditional %s restart guidance after 15 seconds",
    (mode) => {
      vi.useFakeTimers();
      installMockSpeechRecognition();
      vi.stubGlobal("matchMedia", (query: string) => ({
        matches:
          query ===
          (mode === "installed" ? "(display-mode: standalone)" : "(display-mode: browser)"),
      }));
      render(<DictationHarness />);
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      act(() => {
        vi.advanceTimersByTime(14999);
      });
      expect(screen.getByTestId("speech-notice").textContent).toBe("");
      act(() => {
        vi.advanceTimersByTime(1);
      });
      expect(screen.getByTestId("speech-notice").textContent).toContain(
        "If you have been speaking",
      );
      expect(screen.getByTestId("speech-notice").textContent).toContain(
        mode === "installed" ? "Voltura Air app" : "web browser",
      );
      const native = MockSpeechRecognition.instances[0]!;
      act(() => native.onresult?.({ resultIndex: 0, results: [final("working")] }));
      expect(screen.getByTestId("speech-notice").textContent).toBe("");
      act(() => {
        vi.advanceTimersByTime(15000);
      });
      expect(screen.getByTestId("speech-notice").textContent).not.toBe("");
      fireEvent.click(screen.getByRole("button", { name: "Stop" }));
      expect(screen.getByTestId("speech-notice").textContent).toBe("");
      act(() => {
        vi.advanceTimersByTime(15000);
      });
      expect(screen.getByTestId("speech-notice").textContent).toBe("");
    },
  );

  it("also warns when audio-start never arrives, without restarting capture", () => {
    vi.useFakeTimers();
    installMockSpeechRecognition();
    MockSpeechRecognition.deferStart = true;
    render(<DictationHarness />);
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    act(() => {
      vi.advanceTimersByTime(15000);
    });
    expect(screen.getByTestId("speech-notice").textContent).toContain("restart");
    expect(MockSpeechRecognition.instances[0]!.start).toHaveBeenCalledOnce();
  });

  it("cancels the inactivity warning when hidden and requires an explicit resume", () => {
    vi.useFakeTimers();
    installMockSpeechRecognition();
    render(<DictationHarness />);
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    fireEvent(document, new Event("visibilitychange"));
    act(() => {
      vi.advanceTimersByTime(20000);
    });
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
    fireEvent(document, new Event("visibilitychange"));
    expect(screen.getByTestId("speech-notice").textContent).toBe("");
    expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
  });

  it("releases listeners on native end and preserves specific permission errors", () => {
    installMockSpeechRecognition();
    render(<DictationHarness />);
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    act(() => MockSpeechRecognition.instances[0]!.onerror?.({ error: "not-allowed" }));
    expect(screen.getByTestId("speech-error").textContent).toContain(
      "Microphone access was denied",
    );
    expect(getActiveSpeechSession()).toBeNull();
    expect(MockSpeechRecognition.instances).toHaveLength(1);
  });
});
