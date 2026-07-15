import { fireEvent, render, screen } from "@testing-library/react";
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
  });

  it("closes with Escape and returns focus to the trigger", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.click(trigger);
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("closes when the dialog backdrop is tapped", () => {
    render(<InfoButton {...props} />);

    const trigger = screen.getByRole("button", { name: "About Live typing" });
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("dialog"));

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("uses the taller dialog layout when requested", () => {
    render(<InfoButton {...props} size="detailed" />);

    fireEvent.click(screen.getByRole("button", { name: "About Live typing" }));

    expect(screen.getByRole("dialog", { name: "Live typing" }).classList.contains("info-dialog-detailed")).toBe(true);
  });
});
