import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useDeveloperRefreshLongPress } from "./useDeveloperRefreshLongPress";

function Harness({ enabled = true, refresh }: { enabled?: boolean; refresh: () => void }) {
  return (
    <div data-testid="target" {...useDeveloperRefreshLongPress(enabled, refresh)}>
      Brand
    </div>
  );
}
afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe("developer refresh cancellation", () => {
  it.each(["movement", "blur", "disabled", "unmount"])(
    "releases pointer capture on %s",
    (boundary) => {
      vi.useFakeTimers();
      const refresh = vi.fn();
      const view = render(<Harness refresh={refresh} />);
      const target = screen.getByTestId("target");
      const release = vi.fn();
      Object.assign(target, {
        setPointerCapture: vi.fn(),
        hasPointerCapture: () => true,
        releasePointerCapture: release,
      });
      fireEvent.pointerDown(target, { button: 0, pointerId: 1, clientX: 0, clientY: 0 });

      if (boundary === "movement") {
        fireEvent.pointerMove(target, { pointerId: 1, clientX: 20, clientY: 0 });
      } else if (boundary === "disabled") {
        view.rerender(<Harness enabled={false} refresh={refresh} />);
      } else if (boundary === "unmount") {
        view.unmount();
      } else {
        fireEvent(window, new Event("blur"));
      }
      act(() => {
        vi.advanceTimersByTime(1_000);
      });

      expect(release).toHaveBeenCalledExactlyOnceWith(1);
      expect(refresh).not.toHaveBeenCalled();
    },
  );

  it("suppresses the native touch context menu before the refresh hold completes", () => {
    vi.useFakeTimers();
    const refresh = vi.fn();
    render(<Harness refresh={refresh} />);
    const target = screen.getByTestId("target");
    expect(fireEvent.contextMenu(target)).toBe(true);
    fireEvent.pointerDown(target, { button: 0, pointerId: 1, pointerType: "touch" });
    act(() => {
      vi.advanceTimersByTime(500);
    });

    expect(fireEvent.contextMenu(target)).toBe(false);
    expect(refresh).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(refresh).toHaveBeenCalledOnce();

    fireEvent.pointerUp(target, { pointerId: 1 });
    expect(fireEvent.contextMenu(target)).toBe(true);
  });

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
