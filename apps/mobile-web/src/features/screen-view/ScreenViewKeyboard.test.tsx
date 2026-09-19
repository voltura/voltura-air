import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScreenViewKeyboard } from "./ScreenViewKeyboard";

describe("Screen View device keyboard capabilities", () => {
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
