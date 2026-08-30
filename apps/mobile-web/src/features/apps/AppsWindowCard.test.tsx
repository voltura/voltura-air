import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppsWindowCard } from "./AppsWindowCard";

const appWindow = {
  windowId: "11111111111111111111111111111111",
  title: "Draft",
  applicationName: "Notepad",
  active: false,
  minimized: true,
  maximizeSupported: true,
  previewSupported: false,
};

describe("Apps window card", () => {
  it("shows minimized state and keeps close available as a semantic action", () => {
    renderCard();

    expect(screen.getByText("Minimized")).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Close Draft" }).disabled).toBe(
      false,
    );
  });

  it("does not mount a close action on a passive card while the deck is busy", () => {
    render(
      <AppsWindowCard
        busy
        onActivate={vi.fn()}
        onClose={vi.fn()}
        onSelect={vi.fn()}
        previewState="unavailable"
        selected={false}
        window={appWindow}
      />,
    );

    expect(screen.queryByRole("button", { name: "Close Draft" })).toBeNull();
  });

  it("distinguishes an in-flight preview from an unavailable preview", () => {
    const view = renderCard(undefined, undefined, "loading");
    expect(screen.getByText("Loading preview…")).toBeTruthy();

    view.rerender(card("unavailable"));
    expect(screen.getByText("Preview unavailable")).toBeTruthy();
  });

  it("keeps the current preview painted until its replacement is ready", () => {
    const view = render(card("unavailable", vi.fn(), vi.fn(), "blob:current"));

    view.rerender(card("unavailable", vi.fn(), vi.fn(), "blob:replacement"));
    const images = screen.getByRole("button", { name: "Activate Draft" }).querySelectorAll("img");
    expect(images).toHaveLength(2);
    expect(images[0]?.src).toBe("blob:current");
    expect(images[1]?.src).toBe("blob:replacement");
    expect(images[1]?.classList.contains("is-ready")).toBe(false);

    fireEvent.load(images[1]!);
    expect(images[1]?.classList.contains("is-ready")).toBe(true);
  });

  it("reveals close feedback but sends nothing below the upward threshold", () => {
    const onClose = vi.fn();
    renderCard(onClose);
    const card = screen.getByRole("article");

    fireEvent.pointerDown(card, { pointerId: 3, clientX: 150, clientY: 240 });
    fireEvent.pointerMove(card, { pointerId: 3, clientX: 148, clientY: 200 });
    expect(card.classList.contains("is-close-dragging")).toBe(true);
    expect(card.style.getPropertyValue("--apps-close-progress")).not.toBe("0");
    fireEvent.pointerUp(card, { pointerId: 3, clientX: 148, clientY: 200 });

    expect(onClose).not.toHaveBeenCalled();
    expect(card.classList.contains("is-close-dragging")).toBe(false);
  });

  it("fully hides close feedback when an upward swipe reverses", () => {
    renderCard();
    const card = screen.getByRole("article");

    fireEvent.pointerDown(card, { pointerId: 7, clientX: 150, clientY: 240 });
    fireEvent.pointerMove(card, { pointerId: 7, clientX: 148, clientY: 190 });
    expect(card.classList.contains("is-close-dragging")).toBe(true);
    expect(card.style.getPropertyValue("--apps-close-offset")).toBe("-50px");

    fireEvent.pointerMove(card, { pointerId: 7, clientX: 148, clientY: 250 });
    expect(card.classList.contains("is-close-dragging")).toBe(false);
    expect(card.style.getPropertyValue("--apps-close-progress")).toBe("0");
    expect(card.style.getPropertyValue("--apps-close-offset")).toBe("0px");
  });

  it("sends exactly one close after a locked upward swipe crosses the threshold", () => {
    const onClose = vi.fn();
    renderCard(onClose);
    const card = screen.getByRole("article");
    vi.spyOn(card, "getBoundingClientRect").mockReturnValue(new DOMRect(0, 0, 300, 420));

    fireEvent.pointerDown(card, { pointerId: 4, clientX: 150, clientY: 250 });
    fireEvent.pointerMove(card, { pointerId: 4, clientX: 146, clientY: 150 });
    expect(card.style.getPropertyValue("--apps-close-offset")).toBe("-100px");
    fireEvent.pointerUp(card, { pointerId: 4, clientX: 146, clientY: 150 });

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(card.classList.contains("is-close-committed")).toBe(true);
    expect(Number.parseFloat(card.style.getPropertyValue("--apps-close-offset"))).toBeLessThan(
      -420,
    );
  });

  it("does not close on a horizontal gesture and accepts the next deliberate tap", () => {
    const onClose = vi.fn();
    const onActivate = vi.fn();
    renderCard(onClose, onActivate);
    const card = screen.getByRole("article");
    const activate = screen.getByRole("button", { name: "Activate Draft" });

    fireEvent.pointerDown(card, { pointerId: 5, clientX: 220, clientY: 200 });
    fireEvent.pointerMove(card, { pointerId: 5, clientX: 120, clientY: 196 });
    fireEvent.pointerUp(card, { pointerId: 5, clientX: 120, clientY: 196 });
    fireEvent.click(activate);
    expect(onClose).not.toHaveBeenCalled();
    expect(onActivate).not.toHaveBeenCalled();

    fireEvent.pointerDown(card, { pointerId: 6, clientX: 150, clientY: 200 });
    fireEvent.pointerUp(card, { pointerId: 6, clientX: 150, clientY: 200 });
    fireEvent.click(activate);
    expect(onActivate).toHaveBeenCalledTimes(1);
  });
});

function card(
  previewState: "loading" | "unavailable" = "unavailable",
  onClose = vi.fn(),
  onActivate = vi.fn(),
  previewUrl?: string,
) {
  return (
    <AppsWindowCard
      busy={false}
      onActivate={onActivate}
      onClose={onClose}
      onSelect={vi.fn()}
      previewState={previewState}
      previewUrl={previewUrl}
      selected
      window={appWindow}
    />
  );
}

function renderCard(
  onClose = vi.fn(),
  onActivate = vi.fn(),
  previewState: "loading" | "unavailable" = "unavailable",
) {
  return render(card(previewState, onClose, onActivate));
}
