import { act, fireEvent, render, screen } from "@testing-library/react";
import { useRef, useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { liveKeyboardSentinel, toLiveKeyboardValue } from "../../../foundation/input/keyboardDelta";
import { KeyboardMode } from "./KeyboardMode";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

interface HarnessProps {
  connected?: boolean;
  initialText?: string;
  liveKeyboard?: boolean;
  liveTypingInfoOpen?: boolean;
  onLiveTypingInfoOpenChange?: (isOpen: boolean) => void;
  onSleep?: () => void;
  sendSpecial?: (key: string, modifiers?: string[]) => void;
  sendText?: (text: string) => void;
  showArrowKeys?: boolean;
  showControlKeys?: boolean;
  showFunctionKeys?: boolean;
  showSleepButton?: boolean;
}

function KeyboardModeHarness({
  connected = true,
  initialText = "",
  liveKeyboard = true,
  liveTypingInfoOpen,
  onLiveTypingInfoOpenChange,
  onSleep = vi.fn(),
  sendSpecial = vi.fn(),
  sendText = vi.fn(),
  showArrowKeys = true,
  showControlKeys = true,
  showFunctionKeys = false,
  showSleepButton = false,
}: HarnessProps) {
  const [keyboardText, setKeyboardText] = useState(initialText);
  const [isLive, setIsLive] = useState(liveKeyboard);
  const committedKeyboardTextRef = useRef(initialText);
  const keyboardTextareaRef = useRef<HTMLTextAreaElement | null>(null);
  const isComposingRef = useRef(false);

  return (
    <KeyboardMode
      connected={connected}
      committedKeyboardTextRef={committedKeyboardTextRef}
      isComposingRef={isComposingRef}
      keyboardText={keyboardText}
      keyboardTextareaRef={keyboardTextareaRef}
      liveKeyboard={isLive}
      liveTypingInfoOpen={liveTypingInfoOpen}
      onKeyboardTextChange={(next) => {
        setKeyboardText(next);
        committedKeyboardTextRef.current = next;
      }}
      onLiveTypingInfoOpenChange={onLiveTypingInfoOpenChange}
      onSleep={onSleep}
      placeLiveKeyboardCaret={vi.fn()}
      sendEmptyDelete={vi.fn(() => false)}
      sendSpecial={sendSpecial}
      sendText={sendText}
      setKeyboardText={setKeyboardText}
      setLiveTyping={setIsLive}
      showArrowKeys={showArrowKeys}
      showControlKeys={showControlKeys}
      showFunctionKeys={showFunctionKeys}
      showSleepButton={showSleepButton}
      toLiveKeyboardValue={toLiveKeyboardValue}
    />
  );
}

function KeyboardOrientationHarness() {
  const [isSplit, setIsSplit] = useState(false);
  const [isLiveTypingInfoOpen, setIsLiveTypingInfoOpen] = useState(false);
  const keyboard = (
    <KeyboardModeHarness
      liveTypingInfoOpen={isLiveTypingInfoOpen}
      onLiveTypingInfoOpenChange={setIsLiveTypingInfoOpen}
    />
  );

  return (
    <>
      <button
        type="button"
        onClick={() => {
          setIsSplit((current) => !current);
        }}
      >
        Rotate
      </button>
      {isSplit ? <div className="split-keyboard-pane">{keyboard}</div> : keyboard}
    </>
  );
}

describe("KeyboardMode live typing", () => {
  it("keeps the replacement held key when the earlier finger releases", () => {
    vi.useFakeTimers();
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);
    fireEvent.pointerDown(screen.getByRole("button", { name: "Backspace" }), {
      button: 0,
      pointerId: 1,
    });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Delete" }), {
      button: 0,
      pointerId: 2,
    });
    fireEvent.pointerUp(screen.getByRole("button", { name: "Backspace" }), { pointerId: 1 });
    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs);
    });
    expect(sendSpecial.mock.calls).toEqual([["Backspace"], ["Delete"], ["Delete"]]);
    vi.useRealTimers();
  });
  it("does not stop a held key when an unrelated pointer releases", () => {
    vi.useFakeTimers();
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);
    const backspace = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspace, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(backspace, { pointerId: 2 });
    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs);
    });
    expect(sendSpecial).toHaveBeenCalledTimes(2);
    fireEvent.pointerUp(backspace, { pointerId: 1 });
    act(() => {
      vi.advanceTimersByTime(repeatIntervalMs * 3);
    });
    expect(sendSpecial).toHaveBeenCalledTimes(2);
    vi.useRealTimers();
  });
  it("stops a held key when its page departs", () => {
    vi.useFakeTimers();
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);
    fireEvent.pointerDown(screen.getByRole("button", { name: "Backspace" }), {
      button: 0,
      pointerId: 1,
    });
    window.dispatchEvent(new Event("pagehide"));
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(sendSpecial).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });
  it("preserves a buffered draft instead of clearing it while disconnected", () => {
    const sendText = vi.fn();
    render(
      <KeyboardModeHarness
        liveKeyboard={false}
        initialText="Unsent draft"
        connected={false}
        sendText={sendText}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(sendText).not.toHaveBeenCalled();
    expect(screen.getByRole<HTMLTextAreaElement>("textbox").value).toBe("Unsent draft");
  });
  it("stops an already-held live key when the connection drops", () => {
    vi.useFakeTimers();
    const sendSpecial = vi.fn();
    const view = render(<KeyboardModeHarness initialText="abcdef" sendSpecial={sendSpecial} />);
    fireEvent.pointerDown(screen.getByRole("button", { name: "Backspace" }), {
      button: 0,
      pointerId: 1,
    });
    view.rerender(
      <KeyboardModeHarness initialText="abcdef" connected={false} sendSpecial={sendSpecial} />,
    );
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(sendSpecial).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });

  it("explains live typing and buffered send in a compact info dialog", () => {
    render(<KeyboardModeHarness />);

    fireEvent.click(screen.getByRole("button", { name: "About Live typing" }));

    expect(screen.getByRole("dialog", { name: "Live typing" })).toBeTruthy();
    expect(
      screen.getByText(
        "Sends each character to the focused application on your PC as you type. Turn it off to compose text on your device first, then press Send.",
      ),
    ).toBeTruthy();
  });

  it("keeps live typing guidance open when orientation changes the keyboard layout", () => {
    render(<KeyboardOrientationHarness />);

    fireEvent.click(screen.getByRole("button", { name: "About Live typing" }));
    fireEvent.click(screen.getByRole("button", { name: "Rotate" }));

    expect(screen.getByRole("dialog", { name: "Live typing" })).toBeTruthy();
  });

  it("focuses the text input when live typing is enabled", () => {
    render(<KeyboardModeHarness liveKeyboard={false} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    const liveTypingSwitch = screen.getByRole("switch", { name: "Live typing" });
    expect(liveTypingSwitch.getAttribute("aria-checked")).toBe("false");

    fireEvent.click(liveTypingSwitch);

    expect(document.activeElement).toBe(textarea);
    expect(liveTypingSwitch.getAttribute("aria-checked")).toBe("true");
  });

  it("switches the device keyboard input mode and focuses the text input", () => {
    render(<KeyboardModeHarness />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    const textModeButton = screen.getByRole("tab", { name: "Show regular keyboard" });
    const numericModeButton = screen.getByRole("tab", { name: "Show numeric keyboard" });

    expect(textarea.inputMode).toBe("text");
    expect(textModeButton.getAttribute("aria-selected")).toBe("true");
    expect(numericModeButton.getAttribute("aria-selected")).toBe("false");

    fireEvent.click(numericModeButton);

    expect(textarea.inputMode).toBe("numeric");
    expect(document.activeElement).toBe(textarea);
    expect(textModeButton.getAttribute("aria-selected")).toBe("false");
    expect(numericModeButton.getAttribute("aria-selected")).toBe("true");

    fireEvent.click(textModeButton);

    expect(textarea.inputMode).toBe("text");
    expect(document.activeElement).toBe(textarea);
    expect(textModeButton.getAttribute("aria-selected")).toBe("true");
    expect(numericModeButton.getAttribute("aria-selected")).toBe("false");
  });

  it("mirrors Backspace, Enter, Tab, and Space buttons into the live textbox", () => {
    const sendSpecial = vi.fn();
    const sendText = vi.fn();
    render(<KeyboardModeHarness initialText="ab" sendSpecial={sendSpecial} sendText={sendText} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    textarea.setSelectionRange(textarea.value.length, textarea.value.length);

    fireEvent.click(screen.getByRole("button", { name: "Backspace" }));
    expect(textarea.value).toBe(`${liveKeyboardSentinel}a`);
    expect(sendSpecial).toHaveBeenCalledWith("Backspace");

    fireEvent.click(screen.getByRole("button", { name: "Enter" }));
    expect(textarea.value).toBe(`${liveKeyboardSentinel}a\n`);
    expect(sendSpecial).toHaveBeenCalledWith("Enter");

    fireEvent.click(screen.getByRole("button", { name: "Tab" }));
    expect(textarea.value).toBe(`${liveKeyboardSentinel}a\n\t`);
    expect(sendSpecial).toHaveBeenCalledWith("Tab");

    fireEvent.click(screen.getByRole("button", { name: "Space" }));
    expect(textarea.value).toBe(`${liveKeyboardSentinel}a\n\t `);
    expect(sendSpecial).toHaveBeenCalledWith("Space");
    expect(sendText).not.toHaveBeenCalled();
  });

  it("mirrors Delete and navigation buttons into the live textbox", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness initialText={"ab\ncd"} sendSpecial={sendSpecial} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    textarea.setSelectionRange(1 + liveKeyboardSentinel.length, 1 + liveKeyboardSentinel.length);

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(textarea.value).toBe(`${liveKeyboardSentinel}a\ncd`);
    expect(sendSpecial).toHaveBeenLastCalledWith("Delete");

    fireEvent.click(screen.getByRole("button", { name: "Page Down" }));
    expect(sendSpecial).toHaveBeenLastCalledWith("PageDown");

    fireEvent.click(screen.getByRole("button", { name: "Home" }));
    expect(sendSpecial).toHaveBeenLastCalledWith("Home");

    fireEvent.click(screen.getByRole("button", { name: "End" }));
    expect(sendSpecial).toHaveBeenLastCalledWith("End");

    fireEvent.click(screen.getByRole("button", { name: "Page Up" }));
    expect(sendSpecial).toHaveBeenLastCalledWith("PageUp");
  });
});

describe("KeyboardMode repeatable keys", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  afterEach(() => {
    vi.useRealTimers();
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  it("sends one key press for a normal pointer press", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);

    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(backspaceButton, { pointerId: 1 });
    fireEvent.click(backspaceButton, { detail: 1 });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Backspace");
  });

  it.each(["pointerCancel", "pointerLeave", "lostPointerCapture"] as const)(
    "does not duplicate a repeatable key after %s",
    (boundary) => {
      const sendSpecial = vi.fn();
      render(<KeyboardModeHarness sendSpecial={sendSpecial} />);
      const backspaceButton = screen.getByRole("button", { name: "Backspace" });

      fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });
      fireEvent[boundary](backspaceButton, { pointerId: 1 });
      fireEvent.click(backspaceButton, { detail: 1 });

      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Backspace");
    },
  );

  it("repeats Backspace in live typing mode until release", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness initialText="abc" sendSpecial={sendSpecial} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.setSelectionRange(textarea.value.length, textarea.value.length);
    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Backspace");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Backspace");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "Backspace");
    expect(textarea.value).toBe(liveKeyboardSentinel);

    fireEvent.pointerUp(backspaceButton, { pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatIntervalMs * 2);
    });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
  });

  it.each(["blur", "hidden", "lost capture", "unmount"] as const)(
    "stops repeat ownership on %s",
    (boundary) => {
      const sendSpecial = vi.fn();
      const view = render(<KeyboardModeHarness initialText="abcdef" sendSpecial={sendSpecial} />);
      const backspaceButton = screen.getByRole("button", { name: "Backspace" });
      fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });

      if (boundary === "blur") {
        fireEvent.blur(window);
      } else if (boundary === "hidden") {
        Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
        fireEvent(document, new Event("visibilitychange"));
      } else if (boundary === "lost capture") {
        fireEvent.lostPointerCapture(backspaceButton, { pointerId: 1 });
      } else {
        view.unmount();
      }
      act(() => {
        vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs * 2);
      });

      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Backspace");
    },
  );

  it("stops an active repeat when live typing mode changes", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness initialText="abc" sendSpecial={sendSpecial} />);
    fireEvent.pointerDown(screen.getByRole("button", { name: "Backspace" }), {
      button: 0,
      pointerId: 1,
    });

    fireEvent.click(screen.getByRole("switch", { name: "Live typing" }));
    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Backspace");
  });

  it("uses the latest command callback for subsequent repeat ticks", () => {
    const firstSend = vi.fn();
    const secondSend = vi.fn();
    const view = render(<KeyboardModeHarness initialText="abc" sendSpecial={firstSend} />);
    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });

    view.rerender(<KeyboardModeHarness initialText="abc" sendSpecial={secondSend} />);
    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    expect(firstSend).toHaveBeenCalledExactlyOnceWith("Backspace");
    expect(secondSend).toHaveBeenCalledTimes(2);
  });

  it("replaces an earlier repeat timer when a second key starts", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness initialText="abc" sendSpecial={sendSpecial} />);
    fireEvent.pointerDown(screen.getByRole("button", { name: "Backspace" }), {
      button: 0,
      pointerId: 1,
    });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Delete" }), {
      button: 0,
      pointerId: 2,
    });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs);
    });

    expect(sendSpecial.mock.calls).toEqual([["Backspace"], ["Delete"], ["Delete"]]);
  });

  it("repeats Backspace against the local textarea in buffered mode", () => {
    render(<KeyboardModeHarness initialText="local" liveKeyboard={false} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    textarea.setSelectionRange(5, 5);

    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs * 2);
    });

    expect(textarea.value).toBe("l");

    fireEvent.pointerUp(backspaceButton, { pointerId: 1 });
  });

  it.each([
    ["Enter", "Enter"],
    ["Tab", "Tab"],
    ["Delete", "Delete"],
    ["Home", "Home"],
    ["End", "End"],
    ["Page Up", "PageUp"],
    ["Page Down", "PageDown"],
    ["Arrow up", "ArrowUp"],
    ["Arrow down", "ArrowDown"],
    ["Arrow left", "ArrowLeft"],
    ["Arrow right", "ArrowRight"],
  ])("repeats %s through the same live key path", (buttonName, key) => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);

    const button = screen.getByRole("button", { name: buttonName });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs);
    });

    fireEvent.pointerCancel(button, { pointerId: 1 });

    expect(sendSpecial).toHaveBeenCalledTimes(2);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, key);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, key);
  });
});

describe("KeyboardMode shortcut keys", () => {
  it("hides shortcut keys when control keys are disabled", () => {
    render(<KeyboardModeHarness showControlKeys={false} />);

    expect(screen.queryByRole("button", { name: "Select all" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Redo" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Next app/ })).toBeNull();
    expect(screen.queryByLabelText("Keyboard shortcuts")).toBeNull();
  });

  it("renders app switching shortcuts without visible key-combo notation", () => {
    render(<KeyboardModeHarness />);

    expect(screen.getByRole("button", { name: "Next app" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Previous app" })).toBeTruthy();
    expect(screen.queryByText("Alt+Tab")).toBeNull();
    expect(screen.queryByText("Shift+Alt+Tab")).toBeNull();
  });
});

describe("KeyboardMode navigation keys", () => {
  it("does not reclaim focus after the user moves to another control", () => {
    const frames: FrameRequestCallback[] = [];
    vi.spyOn(window, "requestAnimationFrame").mockImplementation((callback) => {
      frames.push(callback);
      return frames.length;
    });
    render(<KeyboardModeHarness initialText="abc" liveKeyboard={false} />);
    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    fireEvent.click(screen.getByRole("button", { name: "Home" }));
    const other = screen.getByRole("button", { name: "Send" });
    other.focus();
    act(() => frames.forEach((callback) => callback(0)));
    expect(document.activeElement).toBe(other);
    vi.restoreAllMocks();
  });
  it("retires buffered caret work when live typing is enabled before the frame", () => {
    const frames: FrameRequestCallback[] = [];
    vi.spyOn(window, "requestAnimationFrame").mockImplementation((callback) => {
      frames.push(callback);
      return frames.length;
    });
    render(<KeyboardModeHarness initialText="abc" liveKeyboard={false} />);
    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    fireEvent.click(screen.getByRole("button", { name: "Home" }));
    fireEvent.click(screen.getByRole("switch", { name: /live typing/i }));
    textarea.setSelectionRange(2, 2);
    act(() => frames.forEach((callback) => callback(0)));
    expect(textarea.selectionStart).toBe(2);
    vi.restoreAllMocks();
  });

  it.each([false, true])(
    "keeps Home on a leading blank line and lets Arrow down leave it (live %s)",
    (live) => {
      vi.spyOn(window, "requestAnimationFrame").mockImplementation((callback) => {
        callback(0);
        return 1;
      });
      render(<KeyboardModeHarness initialText={"\nabc"} liveKeyboard={live} />);
      const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
      const offset = live ? liveKeyboardSentinel.length : 0;
      textarea.setSelectionRange(offset, offset);
      fireEvent.click(screen.getByRole("button", { name: "Home" }));
      expect(textarea.selectionStart).toBe(offset);
      fireEvent.click(screen.getByRole("button", { name: "Arrow down" }));
      expect(textarea.selectionStart).toBe(offset + 1);
      vi.restoreAllMocks();
    },
  );

  it.each([
    ["a\n😀b", 1, "Arrow down", 2],
    ["😀b\na", 5, "Arrow up", 0],
  ] as const)(
    "keeps vertical navigation on Unicode boundaries in %s",
    (text, caret, direction, expected) => {
      render(<KeyboardModeHarness initialText={text} liveKeyboard={false} />);
      const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
      textarea.setSelectionRange(caret, caret);
      fireEvent.click(screen.getByRole("button", { name: direction }));
      expect(textarea.selectionStart).toBe(expected);
    },
  );

  it.each([false, true])("moves horizontal arrows across complete graphemes (live %s)", (live) => {
    vi.spyOn(window, "requestAnimationFrame").mockImplementation((callback) => {
      callback(0);
      return 1;
    });
    render(<KeyboardModeHarness initialText="a👩‍💻b" liveKeyboard={live} />);
    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    const offset = live ? liveKeyboardSentinel.length : 0;
    textarea.setSelectionRange(offset + 1, offset + 1);
    fireEvent.click(screen.getByRole("button", { name: "Arrow right" }));
    expect(textarea.selectionStart).toBe(offset + 6);
    fireEvent.click(screen.getByRole("button", { name: "Arrow left" }));
    expect(textarea.selectionStart).toBe(offset + 1);
    vi.restoreAllMocks();
  });
  it("sends buffered multiline text unchanged before clearing the editor", () => {
    const sendText = vi.fn();
    render(
      <KeyboardModeHarness
        initialText={"First line\nSecond line"}
        liveKeyboard={false}
        sendText={sendText}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(sendText).toHaveBeenCalledExactlyOnceWith("First line\nSecond line");
    expect(screen.getByRole<HTMLTextAreaElement>("textbox").value).toBe("");
  });

  it("adds Space to the local textarea without sending text in buffered mode", () => {
    const sendText = vi.fn();
    render(<KeyboardModeHarness initialText="ab" liveKeyboard={false} sendText={sendText} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    textarea.setSelectionRange(1, 1);

    fireEvent.click(screen.getByRole("button", { name: "Space" }));

    expect(textarea.value).toBe("a b");
    expect(textarea.selectionStart).toBe(2);
    expect(textarea.selectionEnd).toBe(2);
    expect(document.activeElement).toBe(textarea);
    expect(sendText).not.toHaveBeenCalled();
  });

  it("edits and moves through the local textarea in buffered mode", () => {
    render(<KeyboardModeHarness initialText={"ab\ncd"} liveKeyboard={false} />);

    const textarea = screen.getByRole<HTMLTextAreaElement>("textbox");
    textarea.focus();
    textarea.setSelectionRange(1, 1);

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(textarea.value).toBe("a\ncd");
    expect(textarea.selectionStart).toBe(1);

    fireEvent.click(screen.getByRole("button", { name: "Page Down" }));
    expect(textarea.selectionStart).toBe(textarea.value.length);

    fireEvent.click(screen.getByRole("button", { name: "Home" }));
    expect(textarea.selectionStart).toBe(2);

    fireEvent.click(screen.getByRole("button", { name: "End" }));
    expect(textarea.selectionStart).toBe(textarea.value.length);

    fireEvent.click(screen.getByRole("button", { name: "Page Up" }));
    expect(textarea.selectionStart).toBe(0);
  });
});

describe("KeyboardMode arrow keys", () => {
  it("hides arrow keys when arrow keys are disabled", () => {
    render(<KeyboardModeHarness showArrowKeys={false} />);

    expect(screen.queryByRole("button", { name: "Arrow up" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Arrow down" })).toBeNull();
  });
});

describe("KeyboardMode function keys", () => {
  it("hides function keys by default", () => {
    render(<KeyboardModeHarness />);

    expect(screen.queryByRole("button", { name: "F1" })).toBeNull();
    expect(screen.queryByLabelText("Function keys")).toBeNull();
  });

  it("renders and sends function keys when enabled", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} showFunctionKeys />);

    fireEvent.click(screen.getByRole("button", { name: "F1" }));

    expect(screen.getByRole("button", { name: "F12" })).toBeTruthy();
    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("F1");
  });
});

describe("KeyboardMode command callbacks", () => {
  it.each([
    ["Esc", "Escape", undefined],
    ["Win", "Win", undefined],
    ["F1", "F1", undefined],
    ["Select all", "A", ["Control"]],
    ["Cut", "X", ["Control"]],
    ["Copy", "C", ["Control"]],
    ["Paste", "V", ["Control"]],
    ["Undo", "Undo", undefined],
    ["Redo", "Redo", undefined],
    [/Next app/, "Tab", ["Alt"]],
    [/Previous app/, "Tab", ["Shift", "Alt"]],
  ] as const)("preserves %s command callback", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} showFunctionKeys />);

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });
});

describe("KeyboardMode sleep button", () => {
  it("hides the sleep button by default", () => {
    render(<KeyboardModeHarness />);

    expect(screen.queryByRole("button", { name: "Sleep" })).toBeNull();
  });

  it("does not sleep when the confirmation is cancelled", () => {
    const onSleep = vi.fn();
    render(<KeyboardModeHarness onSleep={onSleep} showSleepButton />);

    fireEvent.click(screen.getByRole("button", { name: "Sleep" }));

    expect(screen.getByRole("dialog", { name: "Put PC to sleep?" })).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" }));
    expect(onSleep).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog", { name: "Put PC to sleep?" })).toBeNull();
    expect(onSleep).not.toHaveBeenCalled();
  });

  it("sleeps only after confirmation", () => {
    const onSleep = vi.fn();
    render(<KeyboardModeHarness onSleep={onSleep} showSleepButton />);

    fireEvent.click(screen.getByRole("button", { name: "Sleep" }));
    fireEvent.click(screen.getByRole("button", { name: "Sleep PC" }));

    expect(onSleep).toHaveBeenCalledOnce();
    expect(screen.queryByRole("dialog", { name: "Put PC to sleep?" })).toBeNull();
  });
});
