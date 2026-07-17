import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PowerControlEntry } from "./PowerControlEntry";

describe("PowerControlEntry", () => {
  it("opens one compact power sheet entry and forwards allowed actions", () => {
    const onAction = vi.fn();
    const onOpen = vi.fn();
    render(
      <PowerControlEntry
        capabilities={{ lock: true, blackoutDisplay: true, displayOff: false, screenSaver: false, screenSaverAvailable: false, signOut: false, restart: false, shutdown: false }}
        onAction={onAction}
        onOpen={onOpen}
        pendingAction={null}
        result={null}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Power" }));
    expect(onOpen).toHaveBeenCalledOnce();
    expect(screen.getByRole("dialog", { name: "Power & session" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /Lock PC/ }));
    expect(onAction).toHaveBeenCalledExactlyOnceWith("lock");
  });

  it("does not render against hosts without power capabilities", () => {
    const { container } = render(<PowerControlEntry capabilities={null} onAction={vi.fn()} onOpen={vi.fn()} pendingAction={null} result={null} />);
    expect(container.childElementCount).toBe(0);
  });
});
