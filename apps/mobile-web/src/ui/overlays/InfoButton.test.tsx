import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { InfoButton } from "./InfoButton";

const props = {
  title: "Live typing",
  description: "Sends each character as you type."
};

describe("InfoButton", () => {
  it("opens an accessible modal and closes it with OK", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.click(trigger);

    const dialog = screen.getByRole("dialog", { name: "Live typing" });
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    expect(screen.getByText(props.description)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "OK" }));

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(trigger.getAttribute("data-dialog-focus-returned")).toBeNull();
  });

  it("closes with Escape and returns focus to the trigger", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.click(trigger);
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("does not restore pointer focus after a touch-style dismissal", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.pointerDown(trigger);
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("button", { name: "OK" }));

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).not.toBe(trigger);
  });

  it("closes when the dialog backdrop is tapped", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("dialog"), { clientX: -1, clientY: -1 });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("uses the detailed content-width layout when requested", () => {
    render(<InfoButton {...props} size="detailed" />);

    fireEvent.click(screen.getByRole("button", { name: "About Live typing" }));

    expect(screen.getByRole("dialog", { name: "Live typing" }).classList.contains("info-dialog-detailed")).toBe(true);
  });

  it("tracks the visible viewport through keyboard and orientation changes", async () => {
    const originalVisualViewport = window.visualViewport;
    const visualViewport = Object.assign(new EventTarget(), {
      height: 500,
      offsetLeft: 0,
      offsetTop: 20,
      width: 390
    });
    Object.defineProperty(window, "visualViewport", {
      configurable: true,
      value: visualViewport as unknown as VisualViewport
    });

    try {
      render(<InfoButton {...props} />);
      fireEvent.click(screen.getByRole("button", { name: "About Live typing" }));
      const dialog = screen.getByRole("dialog", { name: "Live typing" });

      expect(dialog.style.getPropertyValue("--modal-visual-viewport-width")).toBe("390px");
      expect(dialog.style.getPropertyValue("--modal-visual-viewport-height")).toBe("500px");
      expect(dialog.style.getPropertyValue("--modal-visual-viewport-center-y")).toBe("270px");

      visualViewport.width = 844;
      visualViewport.height = 240;
      visualViewport.offsetLeft = 12;
      visualViewport.offsetTop = 80;
      visualViewport.dispatchEvent(new Event("resize"));

      await waitFor(() => {
        expect(dialog.style.getPropertyValue("--modal-visual-viewport-width")).toBe("844px");
        expect(dialog.style.getPropertyValue("--modal-visual-viewport-height")).toBe("240px");
        expect(dialog.style.getPropertyValue("--modal-visual-viewport-center-x")).toBe("434px");
        expect(dialog.style.getPropertyValue("--modal-visual-viewport-center-y")).toBe("200px");
      });
    } finally {
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        value: originalVisualViewport
      });
    }
  });
});
