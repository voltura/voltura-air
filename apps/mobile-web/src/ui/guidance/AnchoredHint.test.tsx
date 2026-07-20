import { act, render, screen } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnchoredHint } from "./AnchoredHint";
import type { AnchoredHintPlacement } from "./anchoredHintPosition";

const originalVisualViewport = window.visualViewport;

function restoreVisualViewport() {
  Object.defineProperty(window, "visualViewport", {
    configurable: true,
    value: originalVisualViewport
  });
}

function installVisualViewport(width: number, height: number) {
  const visualViewport = Object.assign(new EventTarget(), {
    height,
    offsetLeft: 0,
    offsetTop: 0,
    width
  });
  Object.defineProperty(window, "visualViewport", {
    configurable: true,
    value: visualViewport as unknown as VisualViewport
  });
  return visualViewport;
}

function rect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    bottom: top + height,
    height,
    left,
    right: left + width,
    top,
    width,
    x: left,
    y: top,
    toJSON: () => ({})
  } as DOMRect;
}

function Harness({ open = true, preferredPlacement = "below-center" }: { open?: boolean; preferredPlacement?: AnchoredHintPlacement }) {
  const anchorRef = useRef<HTMLButtonElement | null>(null);
  return (
    <>
      <button ref={anchorRef} type="button">Anchor</button>
      <AnchoredHint anchorRef={anchorRef} open={open} preferredPlacement={preferredPlacement}>
        Switch modes from here.
      </AnchoredHint>
    </>
  );
}

afterEach(() => {
  restoreVisualViewport();
  vi.restoreAllMocks();
});

describe("AnchoredHint", () => {
  it("renders as polite status guidance only while open", () => {
    render(<Harness open={false} />);
    expect(screen.queryByRole("status")).toBeNull();

    render(<Harness />);

    const hint = screen.getByRole("status");
    expect(hint.textContent).toBe("Switch modes from here.");
    expect(hint.getAttribute("aria-live")).toBe("polite");
  });

  it("updates placement when the visible viewport changes", async () => {
    const visualViewport = installVisualViewport(320, 320);
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      if (this.classList.contains("anchored-hint")) {
        return rect(0, 0, 120, 40);
      }

      return rect(140, 180, 40, 44);
    });

    render(<Harness preferredPlacement="below-center" />);
    expect(screen.getByRole("status").getAttribute("data-placement")).toBe("below-center");

    visualViewport.height = 230;
    await act(async () => {
      visualViewport.dispatchEvent(new Event("resize"));
      await new Promise((resolve) => window.requestAnimationFrame(resolve));
    });

    expect(screen.getByRole("status").getAttribute("data-placement")).toBe("above-center");
  });

  it("follows its anchor when a scroll container moves it", async () => {
    let anchorTop = 40;
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      if (this.classList.contains("anchored-hint")) {
        return rect(0, 0, 120, 40);
      }

      return rect(140, anchorTop, 40, 44);
    });

    render(<Harness />);
    expect(screen.getByRole("status").style.top).toBe("92px");

    anchorTop = 100;
    await act(async () => {
      screen.getByRole("button", { name: "Anchor" }).dispatchEvent(new Event("scroll"));
      await new Promise((resolve) => window.requestAnimationFrame(resolve));
    });

    expect(screen.getByRole("status").style.top).toBe("152px");
  });

  it("limits its width to the visible viewport under magnification", () => {
    installVisualViewport(180, 320);
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function getBoundingClientRect(this: HTMLElement) {
      if (this.classList.contains("anchored-hint")) {
        return rect(0, 0, 156, 60);
      }

      return rect(70, 40, 40, 44);
    });

    render(<Harness />);

    expect(screen.getByRole("status").style.maxWidth).toBe("156px");
  });
});
