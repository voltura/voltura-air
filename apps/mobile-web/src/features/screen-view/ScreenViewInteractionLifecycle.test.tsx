import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { publishScreenViewResult } from "../../foundation/connection/screenViewResultBus";
import type { ClientMessage } from "../../foundation/protocol/messages";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import ScreenViewWorkspace from "./ScreenViewWorkspace";

beforeEach(() => {
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({
      matches: true,
      media: "",
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
});
function setup() {
  const send = vi.fn<(message: ClientMessage) => void>();
  const view = render(
    <ScreenViewWorkspace
      activePc={{ customName: false, id: "preview", name: "PC", url: "http://127.0.0.1" }}
      browserPreviewState="active"
      capability={{
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        systemAudio: { codec: "opus", sampleRate: 48000, channels: 2 },
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30,
        directPointer: { permissionGranted: true },
      }}
      clientId="client"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />,
  );
  const request = send.mock.calls.find(([m]) => m.type === "screen.view.sources.get")?.[0];
  if (request?.type !== "screen.view.sources.get") {
    throw new Error("Missing sources request");
  }
  act(() =>
    publishScreenViewResult({
      type: "screen.view.sources.result",
      operationId: request.operationId,
      succeeded: true,
      message: "Ready",
      sources: [{ id: "display-1", label: "Main", width: 1920, height: 1080, isPrimary: true }],
    }),
  );
  const surface = view.container.querySelector<HTMLElement>(".screen-view-direct-pointer")!;
  vi.spyOn(surface, "getBoundingClientRect").mockReturnValue({
    left: 0,
    top: 0,
    width: 100,
    height: 100,
  } as DOMRect);
  return { ...view, send, surface };
}
it.each(["blur", "pagehide"])("releases held direct mouse buttons on %s", (event) => {
  const { surface, send } = setup();
  fireEvent.mouseDown(surface, { button: 0, clientX: 25, clientY: 25 });
  fireEvent(window, new Event(event));
  expect(send).toHaveBeenCalledWith(
    expect.objectContaining({ type: "screen.pointer.button", button: "left", action: "up" }),
  );
  expect(
    screen.getByRole("button", { name: "Mouse and keyboard control" }).getAttribute("aria-pressed"),
  ).toBe("false");
});
it("cancels direct movement queued before hiding", () => {
  const { surface, send } = setup();
  vi.useFakeTimers();
  fireEvent.mouseMove(surface, { clientX: 25, clientY: 25 });
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
  fireEvent(document, new Event("visibilitychange"));
  act(() => {
    vi.advanceTimersByTime(25);
  });
  expect(send.mock.calls.some(([m]) => m.type === "screen.pointer.move")).toBe(false);
});
it("sends pending direct movement before the next mouse button event", () => {
  const { surface, send } = setup();
  vi.useFakeTimers();
  send.mockClear();
  fireEvent.mouseDown(surface, { button: 0, clientX: 20, clientY: 20 });
  fireEvent.mouseMove(surface, { clientX: 40, clientY: 40 });
  fireEvent.mouseUp(surface, { button: 0, clientX: 60, clientY: 60 });
  act(() => {
    vi.advanceTimersByTime(25);
  });
  expect(send.mock.calls.map(([message]) => message)).toEqual([
    expect.objectContaining({ type: "screen.pointer.button", action: "down", x: 0.2 }),
    expect.objectContaining({ type: "screen.pointer.move", x: 0.4 }),
    expect.objectContaining({ type: "screen.pointer.button", action: "up", x: 0.6 }),
  ]);
});

it("does not release a remote button whose press never left this device", () => {
  const { surface, send } = setup();
  send.mockImplementation((message) =>
    message.type !== "screen.pointer.button" || message.action !== "down",
  );
  fireEvent.mouseDown(surface, { button: 0, clientX: 20, clientY: 20 });
  fireEvent.mouseUp(surface, { button: 0, clientX: 20, clientY: 20 });
  fireEvent.contextMenu(surface, { button: 0, clientX: 20, clientY: 20 });
  expect(send.mock.calls.map(([message]) => message).filter(
    (message) => message.type === "screen.pointer.button",
  )).toEqual([
    expect.objectContaining({ button: "left", action: "down" }),
    expect.objectContaining({ button: "right", action: "down" }),
  ]);
});
it("leaves keyboard activation of the mouse control button with the browser", () => {
  const { send } = setup();
  const button = screen.getByRole("button", { name: "Mouse and keyboard control" });
  button.focus();
  expect(fireEvent.keyDown(button, { key: "Enter", code: "Enter" })).toBe(true);
  expect(fireEvent.keyDown(button, { key: " ", code: "Space" })).toBe(true);
  expect(
    send.mock.calls.some(
      ([message]) => message.type === "keyboard.text" || message.type === "keyboard.special",
    ),
  ).toBe(false);
});
it("restores remote typing when the mirrored display is clicked after a toolbar control", () => {
  const { surface, send } = setup();
  const modeButton = screen.getByRole("button", { name: /two-finger mode/i });
  modeButton.focus();
  fireEvent.click(modeButton);
  expect(document.activeElement).toBe(modeButton);

  fireEvent.mouseDown(surface, { button: 0, clientX: 25, clientY: 25 });
  fireEvent.mouseUp(surface, { button: 0, clientX: 25, clientY: 25 });
  expect(document.activeElement).toBe(surface.closest(".screen-view-stage"));
  fireEvent.keyDown(document.activeElement!, { key: "a", code: "KeyA" });
  expect(send).toHaveBeenCalledWith({
    type: "keyboard.text",
    inputContext: "screen-view",
    text: "a",
  });
});
it("does not carry fractional direct scrolling into a new control session", () => {
  const { surface, send } = setup();
  fireEvent.wheel(surface, { deltaY: 6, clientX: 25, clientY: 25 });
  fireEvent.click(screen.getByRole("button", { name: "Mouse and keyboard control" }));
  fireEvent.click(screen.getByRole("button", { name: "Mouse and keyboard control" }));
  fireEvent.wheel(surface, { deltaY: 6, clientX: 25, clientY: 25 });
  expect(send.mock.calls.some(([m]) => m.type === "screen.pointer.wheel")).toBe(false);
});
it("consumes wheel events only on the active PC display", () => {
  const { surface, send } = setup();
  expect(fireEvent.wheel(surface, { deltaY: 120, clientX: 25, clientY: 25 })).toBe(false);
  expect(fireEvent.wheel(surface, { deltaY: 120, clientX: 125, clientY: 25 })).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Mouse and keyboard control" }));
  expect(fireEvent.wheel(surface, { deltaY: 120, clientX: 25, clientY: 25 })).toBe(true);
  expect(
    send.mock.calls.filter(([message]) => message.type === "screen.pointer.wheel"),
  ).toHaveLength(1);
});
it("keeps browser zoom gestures local over the active PC display", () => {
  const { surface, send } = setup();
  expect(fireEvent.wheel(surface, { ctrlKey: true, deltaY: 120, clientX: 25, clientY: 25 })).toBe(
    true,
  );
  expect(send.mock.calls.some(([message]) => message.type === "screen.pointer.wheel")).toBe(false);
});
it("keeps a large wheel event within the Screen View input limit", () => {
  const { surface, send } = setup();
  fireEvent.wheel(surface, { deltaX: 60_012, deltaY: -60_012, clientX: 25, clientY: 25 });
  expect(send).toHaveBeenCalledWith(
    expect.objectContaining({ type: "screen.pointer.wheel", dx: 5_000, dy: -5_000 }),
  );
});
it.each(["input", "select", "textarea", "contenteditable", "dialog"])(
  "leaves local %s keystrokes with the local UI",
  (kind) => {
    const { send } = setup();
    const element = document.createElement(
      kind === "contenteditable" || kind === "dialog" ? "div" : kind,
    );
    if (kind === "contenteditable") {
      element.setAttribute("contenteditable", "true");
    }
    if (kind === "dialog") {
      element.setAttribute("role", "dialog");
    }
    document.body.append(element);
    fireEvent.keyDown(element, { key: "a", code: "KeyA" });
    element.remove();
    expect(
      send.mock.calls.some(([m]) => m.type === "keyboard.text" || m.type === "keyboard.special"),
    ).toBe(false);
  },
);
it("does not jump local screen zoom when a pinch finger is replaced", () => {
  const { container, send } = setup();
  const stage = container.querySelector<HTMLElement>(".screen-view-stage")!;
  Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
  Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
  const content = container.querySelector<HTMLElement>(".screen-view-content")!;
  const original = content.style.transform;
  fireEvent.touchStart(stage, {
    targetTouches: [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 },
    ],
  });
  fireEvent.touchMove(stage, {
    targetTouches: [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 3, clientX: 300, clientY: 100 },
    ],
  });
  fireEvent.touchEnd(stage, { targetTouches: [] });
  expect(content.style.transform).toBe(original);
  expect(
    send.mock.calls.some(([m]) => m.type === "pointer.button" || m.type === "pointer.wheel"),
  ).toBe(false);
});
