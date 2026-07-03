import { act, fireEvent, render, screen } from "@testing-library/react";
import { useRef, useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { liveKeyboardSentinel, toLiveKeyboardValue } from "../keyboardDelta";
import { KeyboardMode } from "./KeyboardMode";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

type HarnessProps = {
  initialText?: string;
  liveKeyboard?: boolean;
  sendSpecial?: (key: string, modifiers?: string[]) => void;
  sendText?: (text: string) => void;
  showArrowKeys?: boolean;
  showControlKeys?: boolean;
  showFunctionKeys?: boolean;
};

function KeyboardModeHarness({
  initialText = "",
  liveKeyboard = true,
  sendSpecial = vi.fn(),
  sendText = vi.fn(),
  showArrowKeys = true,
  showControlKeys = true,
  showFunctionKeys = false
}: HarnessProps) {
  const [keyboardText, setKeyboardText] = useState(initialText);
  const [isLive, setIsLive] = useState(liveKeyboard);
  const committedKeyboardTextRef = useRef(initialText);
  const keyboardTextareaRef = useRef<HTMLTextAreaElement | null>(null);
  const isComposingRef = useRef(false);

  return (
    <KeyboardMode
      committedKeyboardTextRef={committedKeyboardTextRef}
      isComposingRef={isComposingRef}
      keyboardText={keyboardText}
      keyboardTextareaRef={keyboardTextareaRef}
      liveKeyboard={isLive}
      onKeyboardTextChange={(next) => {
        setKeyboardText(next);
        committedKeyboardTextRef.current = next;
      }}
      placeLiveKeyboardCaret={vi.fn()}
      sendEmptyDelete={vi.fn(() => false)}
      sendSpecial={sendSpecial}
      sendText={sendText}
      setKeyboardText={setKeyboardText}
      setLiveTyping={setIsLive}
      showArrowKeys={showArrowKeys}
      showControlKeys={showControlKeys}
      showFunctionKeys={showFunctionKeys}
      toLiveKeyboardValue={toLiveKeyboardValue}
    />
  );
}

describe("KeyboardMode live typing", () => {
  it("focuses the text input when live typing is enabled", () => {
    render(<KeyboardModeHarness liveKeyboard={false} />);

    const textarea = screen.getByRole("textbox");
    fireEvent.click(screen.getByRole("checkbox", { name: "Live typing" }));

    expect(document.activeElement).toBe(textarea);
  });

  it("switches the device keyboard input mode and focuses the text input", () => {
    render(<KeyboardModeHarness />);

    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
    expect(textarea.inputMode).toBe("text");

    fireEvent.click(screen.getByRole("button", { name: "Show numeric keyboard" }));

    expect(textarea.inputMode).toBe("numeric");
    expect(document.activeElement).toBe(textarea);

    fireEvent.click(screen.getByRole("button", { name: "Show regular keyboard" }));

    expect(textarea.inputMode).toBe("text");
    expect(document.activeElement).toBe(textarea);
  });

  it("mirrors Backspace, Enter, Tab, and Space buttons into the live textbox", () => {
    const sendSpecial = vi.fn();
    const sendText = vi.fn();
    render(<KeyboardModeHarness initialText="ab" sendSpecial={sendSpecial} sendText={sendText} />);

    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
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
    expect(sendText).toHaveBeenCalledWith(" ");
  });
});

describe("KeyboardMode repeatable keys", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("sends one key press for a normal pointer press", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);

    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(backspaceButton, { pointerId: 1 });
    fireEvent.click(backspaceButton);

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Backspace");
  });

  it("repeats Backspace in live typing mode until release", () => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);

    const backspaceButton = screen.getByRole("button", { name: "Backspace" });
    fireEvent.pointerDown(backspaceButton, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Backspace");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Backspace");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "Backspace");

    fireEvent.pointerUp(backspaceButton, { pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatIntervalMs * 2);
    });

    expect(sendSpecial).toHaveBeenCalledTimes(3);
  });

  it("repeats Backspace against the local textarea in buffered mode", () => {
    render(<KeyboardModeHarness initialText="local" liveKeyboard={false} />);

    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
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
    ["Arrow up", "ArrowUp"],
    ["Arrow down", "ArrowDown"],
    ["Arrow left", "ArrowLeft"],
    ["Arrow right", "ArrowRight"]
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
  it.each([
    ["Ctrl A", "A", ["Control"]],
    ["Ctrl C", "C", ["Control"]],
    ["Ctrl V", "V", ["Control"]],
    ["Ctrl Z", "Undo", undefined],
    ["Ctrl Y", "Redo", undefined]
  ] as const)("sends %s", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    render(<KeyboardModeHarness sendSpecial={sendSpecial} />);

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });

  it("hides shortcut keys when control keys are disabled", () => {
    render(<KeyboardModeHarness showControlKeys={false} />);

    expect(screen.queryByRole("button", { name: "Ctrl A" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Ctrl Y" })).toBeNull();
    expect(screen.queryByLabelText("Keyboard shortcuts")).toBeNull();
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
