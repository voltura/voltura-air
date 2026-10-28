import { act, renderHook } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import type { KeyboardSpecialMessage, KeyboardTextMessage } from "../protocol/messages";
import { liveKeyboardSentinel } from "./keyboardDelta";
import { useKeyboardInput } from "./useKeyboardInput";

afterEach(() => {
  vi.restoreAllMocks();
});
it("keeps native insertion at a navigated caret instead of moving it to the end", async () => {
  const frames: FrameRequestCallback[] = [];
  vi.spyOn(window, "requestAnimationFrame").mockImplementation((callback) => {
    frames.push(callback);
    return frames.length;
  });
  const send = vi.fn<(message: KeyboardTextMessage | KeyboardSpecialMessage) => void>();
  const { result, unmount } = renderHook(() => useKeyboardInput(send, true));
  const textarea = document.createElement("textarea");
  document.body.append(textarea);
  result.current.keyboardTextareaRef.current = textarea;
  textarea.focus();
  textarea.value = `${liveKeyboardSentinel}abc`;
  act(() => result.current.onKeyboardTextChange(textarea.value));
  act(() => {
    for (const callback of frames.splice(0)) {
      callback(0);
    }
  });
  send.mockClear();
  textarea.value = `${liveKeyboardSentinel}abXc`;
  textarea.setSelectionRange(4, 4);
  act(() => result.current.onKeyboardTextChange(textarea.value));
  await act(async () => {
    await Promise.resolve();
  });
  act(() => {
    for (const callback of frames.splice(0)) {
      callback(0);
    }
  });
  expect(send.mock.calls.map(([message]) => message)).toEqual([
    { type: "keyboard.text", text: "X" },
  ]);
  expect(textarea.selectionStart).toBe(4);
  textarea.value = `${liveKeyboardSentinel}abXYc`;
  textarea.setSelectionRange(5, 5);
  act(() => result.current.onKeyboardTextChange(textarea.value));
  expect(send.mock.lastCall?.[0]).toEqual({ type: "keyboard.text", text: "Y" });
  unmount();
  textarea.remove();
});
