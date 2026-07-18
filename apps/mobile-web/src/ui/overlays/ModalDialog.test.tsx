import { useRef } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ModalDialog } from "./ModalDialog";

const originalVisualViewport = window.visualViewport;
const originalScrollIntoView = HTMLElement.prototype.scrollIntoView;

afterEach(() => {
  Object.defineProperty(window, "visualViewport", {
    configurable: true,
    value: originalVisualViewport
  });
  Object.defineProperty(HTMLElement.prototype, "scrollIntoView", {
    configurable: true,
    value: originalScrollIntoView
  });
});

function ManualEntryDialog() {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <ModalDialog
      dismissLabel="Cancel"
      initialFocusRef={inputRef}
      isOpen
      landscapeSize="wide"
      onClose={() => undefined}
      onSubmit={() => false}
      submitLabel="Connect"
      title="Enter host manually"
    >
      <label>
        Host
        <input ref={inputRef} />
      </label>
    </ModalDialog>
  );
}

describe("ModalDialog", () => {
  it("keeps the focused control reachable when the visual viewport shrinks", async () => {
    const visualViewport = Object.assign(new EventTarget(), {
      height: 500,
      offsetLeft: 0,
      offsetTop: 0,
      width: 390
    });
    const scrollIntoView = vi.fn();
    Object.defineProperty(window, "visualViewport", {
      configurable: true,
      value: visualViewport as unknown as VisualViewport
    });
    Object.defineProperty(HTMLElement.prototype, "scrollIntoView", {
      configurable: true,
      value: scrollIntoView
    });

    render(<ManualEntryDialog />);
    expect(screen.getByRole("dialog", { name: "Enter host manually" })
      .classList.contains("modal-dialog-landscape-wide")).toBe(true);
    const input = screen.getByRole("textbox", { name: "Host" });
    expect(document.activeElement).toBe(input);
    scrollIntoView.mockClear();

    visualViewport.height = 190;
    visualViewport.dispatchEvent(new Event("resize"));

    await waitFor(() => {
      expect(scrollIntoView).toHaveBeenCalledWith({ block: "nearest", inline: "nearest" });
    });
    const dialog = screen.getByRole("dialog", { name: "Enter host manually" });
    expect(dialog.hasAttribute("data-visual-viewport-bottom-constrained")).toBe(true);
    expect(dialog.style.getPropertyValue("--modal-visual-viewport-bottom-offset"))
      .toBe(`${window.innerHeight - 190}px`);

    visualViewport.height = window.innerHeight - 20;
    visualViewport.dispatchEvent(new Event("resize"));

    await waitFor(() => {
      expect(dialog.hasAttribute("data-visual-viewport-bottom-constrained")).toBe(false);
    });
  });
});
