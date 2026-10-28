import { act, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, expect, it, vi } from "vitest";
import type { SavedTextSnippet } from "../../../foundation/settings/textSnippets";
import { useSnippetReorder } from "./useSnippetReorder";

const initial = [
  { id: "a", name: "Alpha", text: "First" },
  { id: "b", name: "Beta", text: "Second" },
];
function Harness() {
  const [snippets, setSnippets] = useState<SavedTextSnippet[]>(initial);
  const reorder = useSnippetReorder({ clientId: "reorder-regression", snippets, setSnippets });
  return (
    <div className="text-transfer-mode">
      <ul className="saved-snippets">
        {snippets.map((snippet) => (
          <li
            key={snippet.id}
            data-snippet-id={snippet.id}
            onTouchStart={(event) => reorder.startSnippetLongPress(event, snippet)}
            onTouchMove={reorder.moveSnippet}
            onTouchEnd={reorder.finishSnippetDrag}
            onTouchCancel={reorder.cancelSnippetDrag}
            onClickCapture={reorder.suppressSnippetClick}
          >
            <button>{snippet.name}</button>
          </li>
        ))}
      </ul>
      <button
        onClick={() =>
          setSnippets((current) =>
            current.map((snippet) => ({ ...snippet, name: `${snippet.name} updated` })),
          )
        }
      >
        Rename while dragging
      </button>
      <button
        onClick={() =>
          setSnippets((current) => [...current, { id: "c", name: "Gamma", text: "Third" }])
        }
      >
        Add while dragging
      </button>
      <button
        onClick={() => setSnippets((current) => current.filter((snippet) => snippet.id !== "b"))}
      >
        Delete while dragging
      </button>
      <output>{reorder.draggingSnippetId ?? "idle"}</output>
    </div>
  );
}
const touch = (id = 1) => ({ identifier: id, clientX: 20, clientY: 100 });
function setup() {
  vi.useFakeTimers();
  render(<Harness />);
  const card = screen.getByRole("button", { name: "Alpha" }).closest("li")!;
  fireEvent.touchStart(card, { touches: [touch()] });
  return card;
}
afterEach(() => {
  vi.useRealTimers();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  vi.restoreAllMocks();
});
it("cancels a pending long press when a second finger starts", () => {
  const card = setup();
  fireEvent.touchStart(card, { touches: [touch(), touch(2)] });
  act(() => {
    vi.advanceTimersByTime(500);
  });
  expect(screen.getByText("idle")).toBeTruthy();
});
it("does not leave an orphaned timer after a replacement touch start", () => {
  const card = setup();
  fireEvent.touchStart(card, { touches: [touch(2)] });
  fireEvent.touchEnd(card, { changedTouches: [touch()], touches: [] });
  act(() => {
    vi.advanceTimersByTime(500);
  });
  expect(screen.getByText("idle")).toBeTruthy();
});
it.each(["end", "cancel"])("ignores an unrelated finger's %s event", (kind) => {
  const card = setup();
  act(() => {
    vi.advanceTimersByTime(500);
  });
  if (kind === "end") {
    fireEvent.touchEnd(card, { changedTouches: [touch(2)], touches: [touch()] });
  } else {
    fireEvent.touchCancel(card, { changedTouches: [touch(2)], touches: [touch()] });
  }
  expect(screen.getByText("a")).toBeTruthy();
});
it.each(["hidden", "blur", "pagehide"])("cancels an active reorder after %s", (kind) => {
  setup();
  act(() => {
    vi.advanceTimersByTime(500);
  });
  if (kind === "hidden") {
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    fireEvent(document, new Event("visibilitychange"));
  } else {
    fireEvent(window, new Event(kind));
  }
  expect(screen.getByText("idle")).toBeTruthy();
});
it("cancels a drag when its tracked finger disappears from movement", () => {
  const card = setup();
  act(() => {
    vi.advanceTimersByTime(500);
  });
  fireEvent.touchMove(card, { touches: [touch(2)] });
  expect(screen.getByText("idle")).toBeTruthy();
});

it.each(["Rename", "Add", "Delete"])(
  "preserves a concurrent %s when canceling an active reorder",
  (kind) => {
    const card = setup();
    act(() => {
      vi.advanceTimersByTime(500);
    });
    fireEvent.click(screen.getByRole("button", { name: `${kind} while dragging` }));
    fireEvent.touchCancel(card, { changedTouches: [touch()] });
    if (kind === "Rename") {
      expect(screen.getByRole("button", { name: "Alpha updated" })).toBeTruthy();
    }
    if (kind === "Add") {
      expect(screen.getByRole("button", { name: "Gamma" })).toBeTruthy();
    }
    if (kind === "Delete") {
      expect(screen.queryByRole("button", { name: "Beta" })).toBeNull();
    }
    expect(screen.getByText("idle")).toBeTruthy();
  },
);
