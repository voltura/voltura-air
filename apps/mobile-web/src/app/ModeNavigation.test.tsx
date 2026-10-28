import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ModeSelector } from "./ModeNavigation";
import { primaryModeDefinitions } from "./appModeTabs";
const props = {
  modeTabs: primaryModeDefinitions,
  remoteMode: "standard" as const,
  tab: "keyboard" as const,
  onClose: vi.fn(),
  onSelect: vi.fn(),
  onSelectRemoteMode: vi.fn(),
};
describe("Mode selector keyboard access", () => {
  it("focuses the selected mode when opened", () => {
    render(<ModeSelector {...props} />);
    expect(document.activeElement).toBe(screen.getByRole("menuitemradio", { name: "Keyboard" }));
  });
  it("dismisses with Escape and restores the invoking button", () => {
    const button = document.createElement("button");
    document.body.append(button);
    button.focus();
    const onClose = vi.fn();
    render(<ModeSelector {...props} onClose={onClose} />);
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    expect(onClose).toHaveBeenCalledOnce();
    expect(document.activeElement).toBe(button);
    button.remove();
  });
  it("dismisses on Tab without blocking normal focus traversal", () => {
    const onClose = vi.fn();
    const globalShortcut = vi.fn();
    window.addEventListener("keydown", globalShortcut);
    render(<ModeSelector {...props} onClose={onClose} />);
    const event = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    fireEvent(screen.getByRole("menu"), event);
    expect(onClose).toHaveBeenCalledOnce();
    expect(event.defaultPrevented).toBe(false);
    expect(globalShortcut).not.toHaveBeenCalled();
    window.removeEventListener("keydown", globalShortcut);
  });
  it("navigates menu choices with arrows and boundary keys", () => {
    render(<ModeSelector {...props} />);
    const menu = screen.getByRole("menu");
    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(screen.getByRole("menuitemradio", { name: "Remote" }));
    fireEvent.keyDown(menu, { key: "End" });
    expect(document.activeElement).toBe(screen.getByRole("menuitemradio", { name: "Kodi remote" }));
    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(screen.getByRole("menuitemradio", { name: "Trackpad" }));
    fireEvent.keyDown(menu, { key: "Home" });
    fireEvent.keyDown(menu, { key: "ArrowUp" });
    expect(document.activeElement).toBe(screen.getByRole("menuitemradio", { name: "Kodi remote" }));
  });
});
