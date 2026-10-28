import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useDeveloperRefreshLongPress } from "./useDeveloperRefreshLongPress";

function Harness({ refresh }: { refresh: () => void }) {
  return (
    <div data-testid="target" {...useDeveloperRefreshLongPress(true, refresh)}>
      Brand
    </div>
  );
}
afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe("developer refresh cancellation", () => {
  it.each(["capture", "blur", "hidden", "pagehide"])(
    "does not reload after %s loss",
    (boundary) => {
      vi.useFakeTimers();
      const refresh = vi.fn();
      render(<Harness refresh={refresh} />);
      const target = screen.getByTestId("target");
      fireEvent.pointerDown(target, { button: 0, pointerId: 1, clientX: 0, clientY: 0 });
      if (boundary === "capture") {
        fireEvent.lostPointerCapture(target, { pointerId: 1 });
      } else if (boundary === "hidden") {
        vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
        fireEvent(document, new Event("visibilitychange"));
      } else {
        fireEvent(window, new Event(boundary));
      }
      act(() => {
        vi.advanceTimersByTime(1_000);
      });
      expect(refresh).not.toHaveBeenCalled();
    },
  );
});
