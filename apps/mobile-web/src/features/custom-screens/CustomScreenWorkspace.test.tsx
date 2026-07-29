import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CustomScreenDefinition } from "../../foundation/protocol/messages";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import { CustomScreenWorkspace } from "./CustomScreenWorkspace";

const definition: CustomScreenDefinition = {
  id: "screen.media",
  name: "Media controls",
  revision: "revision.one",
  orientationLayoutsEnabled: true,
  showNavigationHeader: true,
  sections: [
    {
      id: "section.transport",
      name: "Transport",
      showHeader: true,
      widthColumns: 12,
      heightMode: "content",
      fillWeight: 1,
      rowLimit: 2,
      buttonAlignment: "start",
      kind: "buttons",
      collapsible: false,
      initiallyExpanded: true,
      trackpadLeftClick: true,
      trackpadRightClick: true,
      trackpadButtonSide: "right",
      trackpadFullscreenControl: false,
      trackpadEnabled: true,
      volumeEnabled: true,
      portrait: { order: 1, visible: true, widthColumns: 12 },
      landscape: { order: 0, visible: true, widthColumns: 6 },
      buttons: [
        {
          id: "button.play",
          name: "Play or pause",
          label: "Play",
          icon: "play",
          presentation: "icon",
          size: "standard",
          repeat: false,
          row: 2,
          portrait: { order: 0, visible: true, row: 2 },
          landscape: { order: 1, visible: true, size: "wide", row: 1 },
          enabled: true
        },
        {
          id: "button.volume",
          name: "Volume up",
          label: "Volume",
          icon: "volume-2",
          presentation: "iconLabel",
          size: "compact",
          repeat: true,
          row: 1,
          portrait: { order: 1, visible: true, row: 1 },
          landscape: { order: 0, visible: true, row: 2 },
          enabled: true
        }
      ]
    },
    {
      id: "section.hidden",
      name: "Portrait only",
      showHeader: true,
      widthColumns: 6,
      heightMode: "fill",
      fillWeight: 1,
      rowLimit: 0,
      buttonAlignment: "start",
      kind: "buttons",
      collapsible: false,
      initiallyExpanded: true,
      trackpadLeftClick: true,
      trackpadRightClick: true,
      trackpadButtonSide: "right",
      trackpadFullscreenControl: false,
      trackpadEnabled: true,
      volumeEnabled: true,
      portrait: { order: 0, visible: true, widthColumns: 6 },
      landscape: { order: 1, visible: false },
      buttons: [
        {
          id: "button.disabled",
          name: "Unavailable app",
          label: "Unavailable",
          icon: "app-window",
          presentation: "label",
          size: "fill",
          repeat: false,
          enabled: false,
          unavailableReason: "Application launch is disabled."
        }
      ]
    }
  ]
};

describe("CustomScreenWorkspace", () => {
  beforeEach(() => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 390 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 844 });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("applies orientation order and visibility while preserving accessible button names", () => {
    const view = renderWorkspace();

    expect(screen.getAllByRole("heading", { level: 2 }).map(item => item.textContent))
      .toEqual(["Portrait only", "Transport"]);
    expect(screen.getByRole("button", { name: "Play or pause" }).textContent).toBe("");
    const unavailable = screen.getByRole<HTMLButtonElement>("button", { name: "Unavailable app" });
    expect(unavailable.disabled).toBe(true);
    expect(unavailable.title).toBe("Application launch is disabled.");

    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));
    view.rerender(workspace());

    expect(screen.queryByRole("heading", { name: "Portrait only" })).toBeNull();
    expect(screen.getByRole("heading", { name: "Transport" })).toBeTruthy();
  });

  it("invokes ordinary buttons once and reports exact screen identities", () => {
    const invoke = vi.fn();
    renderWorkspace(invoke);

    fireEvent.click(screen.getByRole("button", { name: "Play or pause" }));

    expect(invoke).toHaveBeenCalledExactlyOnceWith(
      "screen.media",
      "revision.one",
      "button.play");
  });

  it("removes the navigation row when the saved layout disables it", () => {
    const view = render(
      <CustomScreenWorkspace
        definition={{ ...definition, showNavigationHeader: false }}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Media controls"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(view.container.querySelector(".custom-screen-workspace")
      ?.classList.contains("header-hidden")).toBe(true);
  });

  it("places buttons in their selected rows", () => {
    const view = renderWorkspace();

    expect(screen.getByRole("button", { name: "Play or pause" })
      .parentElement?.dataset.row).toBe("2");
    const compact = screen.getByRole("button", { name: "Volume up" });
    expect(compact.parentElement?.dataset.row).toBe("1");
    expect(compact.classList.contains("size-compact")).toBe(true);
    expect(compact.parentElement?.dataset.buttonAlignment).toBe("start");

    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));
    view.rerender(workspace());

    expect(screen.getByRole("button", { name: "Play or pause" })
      .parentElement?.dataset.row).toBe("1");
    expect(screen.getByRole("button", { name: "Volume up" })
      .parentElement?.dataset.row).toBe("2");
  });

  it("stops hold repeat on pointer release", () => {
    vi.useFakeTimers();
    const invoke = vi.fn();
    renderWorkspace(invoke);
    const button = screen.getByRole("button", { name: "Volume up" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 4 });
    expect(invoke).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(510);
    expect(invoke.mock.calls.length).toBeGreaterThan(2);

    fireEvent.pointerUp(button, { pointerId: 4 });
    const completed = invoke.mock.calls.length;
    vi.advanceTimersByTime(500);
    expect(invoke).toHaveBeenCalledTimes(completed);
  });

  it("does not swallow the next action after a repeat press is canceled", () => {
    const invoke = vi.fn();
    renderWorkspace(invoke);
    const repeating = screen.getByRole("button", { name: "Volume up" });

    fireEvent.pointerDown(repeating, { button: 0, pointerId: 4 });
    fireEvent.pointerCancel(repeating, { pointerId: 4 });
    fireEvent.click(screen.getByRole("button", { name: "Play or pause" }));

    expect(invoke).toHaveBeenNthCalledWith(
      2,
      "screen.media",
      "revision.one",
      "button.play");
  });

  it("renders a standalone volume slider and reuses the audio protocol", () => {
    const send = vi.fn();
    const volumeDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [{
        ...definition.sections[0]!,
        id: "section.volume",
        name: "Volume slider",
        showHeader: false,
        widthColumns: 6,
        heightMode: "content",
        rowLimit: 0,
        kind: "volume",
        buttons: [],
        volumeEnabled: true
      }]
    };
    const view = render(
      <CustomScreenWorkspace
        audioState={{ type: "audio.state", volume: 42, muted: false }}
        definition={volumeDefinition}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Volume"
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    expect(send).toHaveBeenCalledWith({ type: "audio.get" });
    expect(view.container.querySelector<HTMLElement>(".kind-volume")?.style.gridColumn)
      .toBe("span 6");
    const slider = screen.getByRole<HTMLInputElement>("slider", { name: "PC volume" });
    expect(slider.value).toBe("42");
    fireEvent.change(slider, { target: { value: "67" } });
    expect(send).toHaveBeenCalledWith({ type: "audio.volume.set", volume: 67 });
    fireEvent.click(screen.getByRole("button", { name: "Mute PC" }));
    expect(send).toHaveBeenCalledWith({ type: "audio.mute.toggle" });
  });

  it("expands and collapses collapsible sections with a required header", () => {
    const collapsibleDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [{
        ...definition.sections[0]!,
        id: "section.collapsible",
        name: "Advanced controls",
        kind: "buttons",
        collapsible: true,
        initiallyExpanded: false,
        showHeader: true,
        heightMode: "fill",
        fillWeight: 2,
        widthColumns: 6
      }]
    };

    render(
      <CustomScreenWorkspace
        definition={collapsibleDefinition}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Media controls"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    const toggle = screen.getByRole("button", { name: "Advanced controls" });
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(screen.queryByRole("button", { name: "Play or pause" })).toBeNull();

    fireEvent.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByRole("button", { name: "Play or pause" })).toBeTruthy();

    fireEvent.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(screen.queryByRole("button", { name: "Play or pause" })).toBeNull();
  });

  it("renders a sized trackpad with bottom click buttons in the selected order", () => {
    const send = vi.fn();
    const trackpadDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [{
        id: "section.trackpad",
        name: "Pointer",
        showHeader: true,
        widthColumns: 12,
        heightMode: "fill",
        fillWeight: 1,
        rowLimit: 0,
        buttonAlignment: "start",
        kind: "trackpad",
        collapsible: false,
        initiallyExpanded: true,
        trackpadLeftClick: true,
        trackpadRightClick: true,
        trackpadButtonSide: "left",
        trackpadFullscreenControl: true,
        trackpadEnabled: true,
        volumeEnabled: true,
        buttons: []
      }]
    };

    render(
      <CustomScreenWorkspace
        definition={trackpadDefinition}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Pointer"
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    const layout = screen.getByRole("application", { name: "Pointer" }).parentElement!;
    expect(layout.classList.contains("buttons-left")).toBe(true);
    expect(screen.getAllByRole("button", { name: /click/i })
      .map((button) => button.getAttribute("aria-label")))
      .toEqual(["Right click", "Left click"]);

    const leftClick = screen.getByRole("button", { name: "Left click" });
    fireEvent.pointerDown(leftClick, { button: 0, pointerId: 8 });
    fireEvent.pointerUp(leftClick, { button: 0, pointerId: 8 });
    expect(send).toHaveBeenNthCalledWith(
      1,
      { type: "pointer.button", button: "left", action: "down" });
    expect(send).toHaveBeenNthCalledWith(
      2,
      { type: "pointer.button", button: "left", action: "up" });

    fireEvent.click(screen.getByRole("button", { name: "Expand Pointer" }));
    expect(layout.classList.contains("is-fullscreen")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Restore Pointer" }));
    expect(layout.classList.contains("is-fullscreen")).toBe(false);
  });

  it("folds a collapsible trackpad and restores it to its responsive row", () => {
    const trackpadDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [{
        id: "section.trackpad",
        name: "Pointer tools",
        showHeader: true,
        widthColumns: 6,
        heightMode: "fill",
        fillWeight: 2,
        rowLimit: 0,
        buttonAlignment: "start",
        kind: "trackpad",
        collapsible: true,
        initiallyExpanded: false,
        trackpadFullscreenControl: true,
        trackpadLeftClick: true,
        trackpadRightClick: true,
        trackpadButtonSide: "right",
        trackpadEnabled: true,
        volumeEnabled: true,
        buttons: []
      }]
    };

    render(
      <CustomScreenWorkspace
        definition={trackpadDefinition}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Pointer"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    expect(screen.queryByRole("application", { name: "Pointer tools" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Pointer tools" }));
    expect(screen.getByRole("application", { name: "Pointer tools" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Expand Pointer tools" }));
    expect(screen.getByRole("application", { name: "Pointer tools" })
      .parentElement?.classList.contains("is-fullscreen")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Restore Pointer tools" }));
    expect(screen.getByRole("application", { name: "Pointer tools" })
      .parentElement?.classList.contains("is-fullscreen")).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Pointer tools" }));
    expect(screen.queryByRole("application", { name: "Pointer tools" })).toBeNull();
  });

  it("groups orientation widths into responsive rows and weights fill rows", () => {
    const weighted: CustomScreenDefinition = {
      ...definition,
      sections: [
        {
          ...definition.sections[0]!,
          id: "section.controls",
          heightMode: "fill",
          fillWeight: 1,
          portrait: { order: 0, visible: true, widthColumns: 12 },
          landscape: { order: 0, visible: true, widthColumns: 6 }
        },
        {
          id: "section.trackpad",
          name: "Pointer",
          showHeader: true,
          widthColumns: 6,
          heightMode: "fill",
          fillWeight: 2,
          rowLimit: 0,
          buttonAlignment: "start",
          kind: "trackpad",
          collapsible: false,
          initiallyExpanded: true,
          trackpadLeftClick: true,
          trackpadRightClick: true,
          trackpadButtonSide: "right",
          trackpadFullscreenControl: false,
          portrait: { order: 1, visible: true, widthColumns: 12 },
          landscape: { order: 1, visible: true, widthColumns: 6 },
          trackpadEnabled: true,
          volumeEnabled: true,
          buttons: []
        }
      ]
    };
    const view = render(
      <CustomScreenWorkspace
        definition={weighted}
        invoke={vi.fn()}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Pointer"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
    );

    expect(view.container.querySelectorAll(".custom-screen-row")).toHaveLength(2);
    expect(Array.from(view.container.querySelectorAll<HTMLElement>(".custom-screen-row"))
      .map(row => row.style.flexGrow)).toEqual(["1", "2"]);

    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));

    expect(view.container.querySelectorAll(".custom-screen-row")).toHaveLength(1);
    expect(view.container.querySelector<HTMLElement>(".custom-screen-row")?.style.flexGrow)
      .toBe("2");
  });
});

function renderWorkspace(invoke = vi.fn()) {
  return render(workspace(invoke));
}

function workspace(invoke = vi.fn()) {
  return (
    <CustomScreenWorkspace
      definition={definition}
      invoke={invoke}
      onBack={vi.fn()}
      pendingButtonIds={new Set()}
      requestedName="Media controls"
      send={vi.fn()}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />
  );
}
