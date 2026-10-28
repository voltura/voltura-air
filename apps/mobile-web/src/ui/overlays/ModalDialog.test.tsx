import { useRef } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ModalDialog } from "./ModalDialog";

const originalVisualViewport = window.visualViewport;
const originalScrollIntoView = HTMLElement.prototype.scrollIntoView;

afterEach(() => {
  Object.defineProperty(window, "visualViewport", {
    configurable: true,
    value: originalVisualViewport,
  });
  Object.defineProperty(HTMLElement.prototype, "scrollIntoView", {
    configurable: true,
    value: originalScrollIntoView,
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
  it("does not submit a containing dialog when a nested portal form submits", () => {
    const outer = vi.fn(() => false),
      inner = vi.fn(() => false);
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={vi.fn()} onSubmit={outer} title="Outer">
        <ModalDialog dismissLabel="Done" isOpen onClose={vi.fn()} onSubmit={inner} title="Inner">
          <input aria-label="Nested value" />
        </ModalDialog>
      </ModalDialog>,
    );
    fireEvent.submit(screen.getByRole("textbox", { name: "Nested value" }).closest("form")!);
    expect(inner).toHaveBeenCalledOnce();
    expect(outer).not.toHaveBeenCalled();
  });
  it("does not cancel a containing dialog when a nested portal dialog is canceled", () => {
    const outer = vi.fn(),
      inner = vi.fn();
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={outer} title="Outer">
        <ModalDialog dismissLabel="Done" isOpen onClose={inner} title="Inner">
          Nested
        </ModalDialog>
      </ModalDialog>,
    );
    fireEvent(
      screen.getByRole("dialog", { name: "Inner" }),
      new Event("cancel", { cancelable: true, bubbles: false }),
    );
    expect(inner).toHaveBeenCalledOnce();
    expect(outer).not.toHaveBeenCalled();
  });
  it("lets an editor consume Escape without closing the dialog", () => {
    const close = vi.fn();
    const shortcut = vi.fn();
    window.addEventListener("keydown", shortcut);
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={close} title="Editor">
        <input
          aria-label="Editor input"
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
            }
          }}
        />
      </ModalDialog>,
    );
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Editor input" }), { key: "Escape" });
    expect(close).not.toHaveBeenCalled();
    expect(shortcut).not.toHaveBeenCalled();
    window.removeEventListener("keydown", shortcut);
  });
  it("consumes an unhandled Escape before it reaches global PC shortcuts", () => {
    const shortcut = vi.fn();
    window.addEventListener("keydown", shortcut);
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={vi.fn()} title="Information">
        Details
      </ModalDialog>,
    );
    fireEvent.keyDown(screen.getByRole("dialog", { name: "Information" }), { key: "Escape" });
    expect(shortcut).not.toHaveBeenCalled();
    window.removeEventListener("keydown", shortcut);
  });
  it("restores invoking focus when an open modal is removed by its owner", () => {
    const trigger = document.createElement("button");
    document.body.append(trigger);
    trigger.focus();
    const view = render(
      <ModalDialog dismissLabel="Done" isOpen onClose={vi.fn()} title="Information">
        Details
      </ModalDialog>,
    );
    view.unmount();
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });
  it("prevents native form navigation even when the feature only returns validation", () => {
    render(<ManualEntryDialog />);
    const form = screen.getByRole("textbox", { name: "Host" }).closest("form")!;
    expect(fireEvent.submit(form)).toBe(false);
  });

  it("does not mistake keyboard clicks on child controls for backdrop clicks", () => {
    const close = vi.fn();
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={close} title="Information">
        <button type="button">Keep open</button>
      </ModalDialog>,
    );
    const dialog = screen.getByRole("dialog", { name: "Information" });
    vi.spyOn(dialog, "getBoundingClientRect").mockReturnValue({
      left: 100,
      right: 500,
      top: 100,
      bottom: 500,
    } as DOMRect);
    fireEvent.click(screen.getByRole("button", { name: "Keep open" }), {
      detail: 0,
      clientX: 0,
      clientY: 0,
    });
    expect(close).not.toHaveBeenCalled();
    expect(dialog.hasAttribute("open")).toBe(true);
    fireEvent.click(dialog, { clientX: 20, clientY: 20 });
    expect(close).toHaveBeenCalledOnce();
  });
  it("focuses the dialog surface by default without visually focusing its close control", () => {
    render(
      <ModalDialog dismissLabel="Done" isOpen onClose={() => undefined} title="Information">
        <p>Details</p>
      </ModalDialog>,
    );

    expect(document.activeElement).toBe(screen.getByRole("dialog", { name: "Information" }));
    expect(screen.getByRole("button", { name: "Close Information" })).not.toBe(
      document.activeElement,
    );
  });

  it("keeps the focused control reachable when the visual viewport shrinks", async () => {
    const visualViewport = Object.assign(new EventTarget(), {
      height: 500,
      offsetLeft: 0,
      offsetTop: 0,
      width: 390,
    });
    const scrollIntoView = vi.fn();
    Object.defineProperty(window, "visualViewport", {
      configurable: true,
      value: visualViewport as unknown as VisualViewport,
    });
    Object.defineProperty(HTMLElement.prototype, "scrollIntoView", {
      configurable: true,
      value: scrollIntoView,
    });

    render(<ManualEntryDialog />);
    expect(
      screen
        .getByRole("dialog", { name: "Enter host manually" })
        .classList.contains("modal-dialog-landscape-wide"),
    ).toBe(true);
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
    expect(dialog.style.getPropertyValue("--modal-visual-viewport-bottom-offset")).toBe(
      `${window.innerHeight - 190}px`,
    );

    visualViewport.height = window.innerHeight - 20;
    visualViewport.dispatchEvent(new Event("resize"));

    await waitFor(() => {
      expect(dialog.hasAttribute("data-visual-viewport-bottom-constrained")).toBe(false);
    });
  });
});
