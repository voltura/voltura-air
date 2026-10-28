import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
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
  afterEach(() => {
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  });

  it.each(["Activate Draft", "Close Draft"])(
    "keeps native %s taps captured by their originating button",
    (name) => {
      renderCard();
      const card = screen.getByRole("article");
      const cardCapture = vi.fn();
      const buttonCapture = vi.fn();
      const button = screen.getByRole("button", { name });
      Object.assign(card, { setPointerCapture: cardCapture });
      Object.assign(button, { setPointerCapture: buttonCapture });
      fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 100, clientY: 100 });
      expect(cardCapture).not.toHaveBeenCalled();
      expect(buttonCapture).toHaveBeenCalledExactlyOnceWith(1);
      fireEvent.pointerMove(card, { pointerId: 1, clientX: 100, clientY: 80 });
      expect(cardCapture).not.toHaveBeenCalled();
    },
  );

  it.each(["blur", "pagehide", "hidden"])("abandons a window-close swipe after %s", (reason) => {
    const close = vi.fn();
    const activate = vi.fn();
    renderCard(close, activate);
    const card = screen.getByRole("article");
    fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 220 });
    fireEvent.pointerMove(card, { pointerId: 1, clientX: 100, clientY: 100 });
    if (reason === "hidden") {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
      fireEvent(document, new Event("visibilitychange"));
    } else {
      fireEvent(window, new Event(reason));
    }
    fireEvent.pointerUp(card, { pointerId: 1, clientX: 100, clientY: 100 });
    fireEvent.click(screen.getByRole("button", { name: "Activate Draft" }), { detail: 1 });
    expect(close).not.toHaveBeenCalled();
    expect(activate).not.toHaveBeenCalled();
    expect(card.classList.contains("is-close-dragging")).toBe(false);
  });

  it("ignores a secondary mouse button swipe", () => {
    const close = vi.fn();
    renderCard(close);
    const card = screen.getByRole("article");
    fireEvent.pointerDown(card, { button: 2, pointerId: 2, clientX: 100, clientY: 200 });
    fireEvent.pointerMove(card, { pointerId: 2, clientX: 100, clientY: 80 });
    fireEvent.pointerUp(card, { pointerId: 2, clientX: 100, clientY: 80 });
    expect(close).not.toHaveBeenCalled();
  });
  it("keeps the first pointer when another pointer starts", () => {
    const close = vi.fn();
    renderCard(close);
    const card = screen.getByRole("article");
    fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 200 });
    fireEvent.pointerDown(card, { pointerId: 2, clientX: 100, clientY: 200 });
    fireEvent.pointerMove(card, { pointerId: 1, clientX: 100, clientY: 80 });
    fireEvent.pointerUp(card, { pointerId: 1, clientX: 100, clientY: 80 });
    expect(close).toHaveBeenCalledOnce();
  });
  it.each(["pointerUp", "pointerCancel", "lostPointerCapture"] as const)(
    "does not let an unrelated %s retire the owning pointer",
    (event) => {
      const close = vi.fn();
      renderCard(close);
      const card = screen.getByRole("article");
      fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 200 });
      fireEvent.pointerMove(card, { pointerId: 1, clientX: 100, clientY: 80 });
      fireEvent[event](card, { pointerId: 2, clientX: 100, clientY: 80 });
      fireEvent.pointerUp(card, { pointerId: 1, clientX: 100, clientY: 80 });
      expect(close).toHaveBeenCalledOnce();
    },
  );
  it("recognizes a swipe whose final position arrives only on release", () => {
    const close = vi.fn();
    renderCard(close);
    const card = screen.getByRole("article");
    fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 200 });
    fireEvent.pointerUp(card, { pointerId: 1, clientX: 100, clientY: 80 });
    expect(close).toHaveBeenCalledOnce();
  });
  it.each(["pointerCancel", "lostPointerCapture"] as const)(
    "suppresses a pointer click after %s cancels its gesture",
    (event) => {
      const activate = vi.fn();
      renderCard(vi.fn(), activate);
      const card = screen.getByRole("article");
      fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 200 });
      fireEvent.pointerMove(card, { pointerId: 1, clientX: 40, clientY: 200 });
      fireEvent[event](card, { pointerId: 1 });
      fireEvent.click(screen.getByRole("button", { name: "Activate Draft" }), { detail: 1 });
      expect(activate).not.toHaveBeenCalled();
    },
  );
  it("allows keyboard activation after a swipe without a synthetic pointer click", () => {
    const activate = vi.fn();
    renderCard(vi.fn(), activate);
    const card = screen.getByRole("article");
    fireEvent.pointerDown(card, { pointerId: 1, clientX: 100, clientY: 200 });
    fireEvent.pointerMove(card, { pointerId: 1, clientX: 40, clientY: 200 });
    fireEvent.pointerUp(card, { pointerId: 1, clientX: 40, clientY: 200 });
    fireEvent.click(screen.getByRole("button", { name: "Activate Draft" }), { detail: 0 });
    expect(activate).toHaveBeenCalledOnce();
  });
  it("keeps a ready incoming preview when the old image loads later", () => {
    vi.useFakeTimers();
    const view = render(card("loading", vi.fn(), vi.fn(), "blob:old"));
    view.rerender(card("loading", vi.fn(), vi.fn(), "blob:new"));
    const images = screen.getByRole("button", { name: "Activate Draft" }).querySelectorAll("img");
    fireEvent.load(images[1]!);
    fireEvent.load(images[0]!);
    act(() => {
      vi.advanceTimersByTime(180);
    });
    expect(
      screen.getByRole("button", { name: "Activate Draft" }).querySelectorAll("img"),
    ).toHaveLength(1);
    vi.useRealTimers();
  });

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
    vi.useFakeTimers();
    const view = render(card("unavailable", vi.fn(), vi.fn(), "blob:current"));

    const currentImage = screen
      .getByRole("button", { name: "Activate Draft" })
      .querySelector<HTMLImageElement>("img");
    expect(currentImage?.classList.contains("is-ready")).toBe(false);
    fireEvent.load(currentImage!);
    expect(currentImage?.classList.contains("is-ready")).toBe(true);

    view.rerender(card("unavailable", vi.fn(), vi.fn(), "blob:replacement"));
    const images = screen.getByRole("button", { name: "Activate Draft" }).querySelectorAll("img");
    expect(images).toHaveLength(2);
    expect(images[0]?.src).toBe("blob:current");
    expect(images[1]?.src).toBe("blob:replacement");
    expect(images[1]?.classList.contains("is-ready")).toBe(false);

    fireEvent.load(images[1]!);
    expect(images[1]?.classList.contains("is-ready")).toBe(true);

    act(() => {
      vi.advanceTimersByTime(180);
    });
    const replacedImages = screen
      .getByRole("button", { name: "Activate Draft" })
      .querySelectorAll("img");
    expect(replacedImages).toHaveLength(1);
    expect(replacedImages[0]?.src).toBe("blob:replacement");
    vi.useRealTimers();
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
    fireEvent.click(activate, { detail: 1 });
    expect(onClose).not.toHaveBeenCalled();
    expect(onActivate).not.toHaveBeenCalled();

    fireEvent.pointerDown(card, { pointerId: 6, clientX: 150, clientY: 200 });
    fireEvent.pointerUp(card, { pointerId: 6, clientX: 150, clientY: 200 });
    fireEvent.click(activate, { detail: 1 });
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
