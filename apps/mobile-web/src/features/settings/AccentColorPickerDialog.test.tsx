import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AccentColorPickerDialog } from "./AccentColorPickerDialog";
afterEach(() => vi.restoreAllMocks());
function setup(width = 100, height = 100) {
  render(
    <AccentColorPickerDialog initialColor="#12A894" isOpen onApply={vi.fn()} onCancel={vi.fn()} />,
  );
  const surface = screen.getByRole("slider", { name: "Saturation and brightness" });
  vi.spyOn(surface, "getBoundingClientRect").mockReturnValue({
    left: 0,
    top: 0,
    width,
    height,
  } as DOMRect);
  const capture = vi.fn();
  const release = vi.fn();
  Object.assign(surface, {
    setPointerCapture: capture,
    hasPointerCapture: () => false,
    releasePointerCapture: release,
  });
  return {
    surface,
    capture,
    release,
    input: screen.getByRole<HTMLInputElement>("textbox", { name: "Hex color" }),
  };
}
it("does not change a color on a secondary-button press", () => {
  const { surface, input } = setup();
  fireEvent.pointerDown(surface, {
    button: 2,
    pointerId: 1,
    isPrimary: true,
    clientX: 90,
    clientY: 90,
  });
  expect(input.value).toBe("#12A894");
});
it("ignores a color pick when the surface has no measurable area", () => {
  const { surface, input } = setup(0, 0);
  fireEvent.pointerDown(surface, {
    button: 0,
    pointerId: 1,
    isPrimary: true,
    clientX: 0,
    clientY: 0,
  });
  expect(input.value).toBe("#12A894");
});
it("still applies the initial pick when optional pointer capture fails", () => {
  const { surface, capture, input } = setup();
  capture.mockImplementation(() => {
    throw new DOMException("Pointer retired", "NotFoundError");
  });
  fireEvent.pointerDown(surface, {
    button: 0,
    pointerId: 1,
    isPrimary: true,
    clientX: 0,
    clientY: 0,
  });
  expect(input.value).toBe("#FFFFFF");
});
it("does not release a pointer that the browser has already released", () => {
  const { surface, release } = setup();
  release.mockImplementation(() => {
    throw new DOMException("Pointer retired", "NotFoundError");
  });
  fireEvent.pointerUp(surface, { pointerId: 1 });
  expect(release).not.toHaveBeenCalled();
});
