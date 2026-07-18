import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { defaultTrackpadSettings } from "../../../foundation/input/gestures";
import { GestureDebugMode } from "./GestureDebugMode";

describe("GestureDebugMode", () => {
  it("renders active settings", () => {
    render(<GestureDebugMode trackpadSettings={{ ...defaultTrackpadSettings, pointerSpeed: 65, pointerSmoothing: true }} />);

    expect(screen.getByText("65%")).toBeTruthy();
    expect(screen.getByText("Smoothing")).toBeTruthy();
    expect(screen.getByText("On")).toBeTruthy();
  });

  it("logs gesture output without sending to the PC", () => {
    render(<GestureDebugMode trackpadSettings={defaultTrackpadSettings} />);

    const surface = screen.getByText("Touch here");
    fireEvent.touchStart(surface, {
      targetTouches: [{ identifier: 1, clientX: 100, clientY: 100 }]
    });
    fireEvent.touchMove(surface, {
      targetTouches: [{ identifier: 1, clientX: 110, clientY: 96 }]
    });

    expect(screen.getByText("13.5")).toBeTruthy();
    expect(screen.getByText("-5.4")).toBeTruthy();
    expect(screen.getByText(/pointer\.move/)).toBeTruthy();
  });

  it("clears the log", () => {
    render(<GestureDebugMode trackpadSettings={defaultTrackpadSettings} />);

    const surface = screen.getByText("Touch here");
    fireEvent.touchStart(surface, {
      targetTouches: [{ identifier: 1, clientX: 100, clientY: 100 }]
    });
    fireEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(screen.queryByText("start")).toBeNull();
  });

  it("cancels active touches when the viewport changes", () => {
    render(<GestureDebugMode trackpadSettings={defaultTrackpadSettings} />);

    const surface = screen.getByText("Touch here");
    fireEvent.touchStart(surface, {
      targetTouches: [{ identifier: 1, clientX: 100, clientY: 100 }]
    });

    expect(screen.getByText("Touches").nextSibling?.textContent).toBe("1");

    fireEvent(window, new Event("resize"));

    expect(screen.getByText("Touches").nextSibling?.textContent).toBe("0");
  });
});
