import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScreenViewKeyboard } from "./ScreenViewKeyboard";
import { toLiveKeyboardValue } from "../../foundation/input/keyboardDelta";

describe("Screen View device keyboard capabilities", () => {
  it("resets the composition and text delta when controls are disabled and enabled again", () => {
    const send = vi.fn();
    const view = render(<ScreenViewKeyboard enabled send={send} />);
    const editor = screen.getByRole("textbox");
    fireEvent.change(editor, { target: { value: toLiveKeyboardValue("old session") } });
    fireEvent.compositionStart(editor);
    view.rerender(<ScreenViewKeyboard enabled={false} send={send} />);
    view.rerender(<ScreenViewKeyboard enabled send={send} />);
    expect((editor as HTMLTextAreaElement).value).toBe(toLiveKeyboardValue(""));
    send.mockClear();
    fireEvent.change(editor, { target: { value: toLiveKeyboardValue("new session") } });
    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "keyboard.text",
      text: "new session",
      inputContext: "screen-view",
    });
  });
  it("requests overlay behavior when available and restores it on cleanup", () => {
    const virtualKeyboard = { overlaysContent: false };
    Object.defineProperty(navigator, "virtualKeyboard", {
      configurable: true,
      value: virtualKeyboard,
    });
    try {
      const { unmount } = render(<ScreenViewKeyboard enabled send={vi.fn()} />);
      expect(virtualKeyboard.overlaysContent).toBe(true);
      fireEvent.click(screen.getByRole("button", { name: "Show device keyboard" }));
      const editor = screen.getByRole("textbox");
      expect(document.activeElement).toBe(editor);
      unmount();
      expect(document.activeElement).not.toBe(editor);
      expect(virtualKeyboard.overlaysContent).toBe(false);
    } finally {
      Reflect.deleteProperty(navigator, "virtualKeyboard");
    }
  });

  it.each(["enable", "restore"])("keeps controls usable when the browser refuses to %s the overlay", (phase) => {
    const virtualKeyboard = { overlaysContent: false };
    let refuseWrite = phase === "enable";
    const getOverlay = () => virtualKeyboard.overlaysContent;
    const setOverlay = (value: boolean) => {
      if (refuseWrite) {
        throw new DOMException("Overlay unavailable", "NotAllowedError");
      }
      virtualKeyboard.overlaysContent = value;
    };
    vi.stubGlobal("navigator", {
      virtualKeyboard: {
        get overlaysContent() {
          return getOverlay();
        },
        set overlaysContent(value) {
          setOverlay(value);
        },
      },
    });
    try {
      const { unmount } = render(<ScreenViewKeyboard enabled send={vi.fn()} />);
      fireEvent.click(screen.getByRole("button", { name: "Show device keyboard" }));
      expect(document.activeElement).toBe(screen.getByRole("textbox"));
      refuseWrite = true;
      expect(unmount).not.toThrow();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("hides the actions when the required input-mode capability is unavailable", () => {
    const prototype = HTMLTextAreaElement.prototype;
    const descriptor = Object.getOwnPropertyDescriptor(prototype, "inputMode")!;
    Reflect.deleteProperty(prototype, "inputMode");
    try {
      render(<ScreenViewKeyboard enabled send={vi.fn()} />);
      expect(screen.queryByRole("button")).toBeNull();
      expect(screen.queryByRole("textbox")).toBeNull();
    } finally {
      Object.defineProperty(prototype, "inputMode", descriptor);
    }
  });
});
