import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ComponentProps } from "react";
import type { ClientMessage, CustomScreenDefinition } from "../../foundation/protocol/messages";
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
      trackpadGyroControl: false,
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
          enabled: true,
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
          enabled: true,
        },
      ],
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
      trackpadGyroControl: false,
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
          unavailableReason: "Application launch is disabled.",
        },
      ],
    },
  ],
};

describe("CustomScreenWorkspace", () => {
  beforeEach(() => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 390 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 844 });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it.each(["disconnect", "revision", "disabled", "orientation", "collapse", "pagehide"])(
    "retires a held custom action on %s",
    (reason) => {
      vi.useFakeTimers();
      const invoke = vi.fn();
      const initial = structuredClone(definition);
      initial.sections[0]!.collapsible = reason === "collapse";
      initial.sections[0]!.landscape = { order: 0, visible: false };
      const view = render(workspace(invoke, { definition: initial }));
      fireEvent.pointerDown(screen.getByRole("button", { name: "Volume up" }), {
        button: 0,
        pointerId: 8,
      });
      expect(invoke).toHaveBeenCalledOnce();
      if (reason === "disconnect") {
        view.rerender(workspace(invoke, { definition: initial, state: "connecting" }));
      } else if (reason === "pagehide") {
        fireEvent(window, new Event("pagehide"));
      } else if (reason === "collapse") {
        fireEvent.click(screen.getByRole("button", { name: "Transport" }));
      } else if (reason === "orientation") {
        Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
        Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
        fireEvent(window, new Event("resize"));
      } else {
        const next = structuredClone(initial);
        if (reason === "revision") {
          next.revision = "revision.two";
        } else {
          next.sections[0]!.buttons[1]!.enabled = false;
        }
        view.rerender(workspace(invoke, { definition: next }));
      }
      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(invoke).toHaveBeenCalledOnce();
    },
  );

  it.each(["revoked", "collapse", "pagehide"])(
    "releases a custom trackpad button on %s",
    (reason) => {
      const send = vi.fn();
      const initial = gyroTrackpadDefinition({ collapsible: reason === "collapse" });
      initial.sections[0]!.trackpadGyroControl = false;
      initial.sections[0]!.trackpadLeftClick = true;
      const view = render(workspace(vi.fn(), { definition: initial, send }));
      fireEvent.pointerDown(screen.getByRole("button", { name: "Left click" }), {
        button: 0,
        pointerId: 8,
      });
      if (reason === "collapse") {
        fireEvent.click(screen.getByRole("button", { name: "Gyro pointer" }));
      } else if (reason === "pagehide") {
        fireEvent(window, new Event("pagehide"));
      } else {
        const next = structuredClone(initial);
        next.sections[0]!.trackpadEnabled = false;
        view.rerender(workspace(vi.fn(), { definition: next, send }));
      }
      expect(send).toHaveBeenCalledWith(
        expect.objectContaining({ type: "pointer.button", button: "left", action: "up" }),
      );
    },
  );

  it("preserves a repeating custom action when another pointer presses it", () => {
    vi.useFakeTimers();
    const invoke = vi.fn();
    renderWorkspace(invoke);
    const button = screen.getByRole("button", { name: "Volume up" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerDown(button, { button: 0, pointerId: 2 });
    fireEvent.pointerUp(button, { pointerId: 2 });
    expect(invoke).toHaveBeenCalledOnce();
    act(() => {
      vi.advanceTimersByTime(450);
    });
    expect(invoke).toHaveBeenCalledTimes(2);
    fireEvent.pointerUp(button, { pointerId: 1 });
  });
  it("ignores unrelated release while a custom action repeats", () => {
    vi.useFakeTimers();
    const invoke = vi.fn();
    renderWorkspace(invoke);
    const button = screen.getByRole("button", { name: "Volume up" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(button, { pointerId: 2 });
    act(() => {
      vi.advanceTimersByTime(450);
    });
    expect(invoke).toHaveBeenCalledTimes(2);
  });
  it("keeps the owning trackpad button held when a different pointer releases", () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const initial = gyroTrackpadDefinition();
    initial.sections[0]!.trackpadGyroControl = false;
    initial.sections[0]!.trackpadLeftClick = true;
    render(workspace(vi.fn(), { definition: initial, send }));
    const button = screen.getByRole("button", { name: "Left click" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });
    fireEvent.pointerUp(button, { pointerId: 2 });
    expect(
      send.mock.calls.some(
        ([message]) => message.type === "pointer.button" && message.action === "up",
      ),
    ).toBe(false);
    fireEvent.pointerUp(button, { pointerId: 1 });
    expect(send).toHaveBeenCalledWith(
      expect.objectContaining({ type: "pointer.button", action: "up" }),
    );
  });

  it("does not carry fullscreen trackpad state into a replacement definition", () => {
    const initial = gyroTrackpadDefinition();
    const view = render(workspace(vi.fn(), { definition: initial }));
    fireEvent.click(screen.getByRole("button", { name: "Expand Gyro pointer" }));
    expect(view.container.querySelector(".is-fullscreen")).not.toBeNull();
    view.rerender(workspace(vi.fn(), { definition: { ...initial, revision: "replacement" } }));
    expect(view.container.querySelector(".is-fullscreen")).toBeNull();
  });

  it("does not turn off the laser when only callback identity changes", () => {
    const initial = withLaserButtons();
    const first = vi.fn();
    const latest = vi.fn();
    const view = render(workspace(first, { definition: initial }));
    view.rerender(workspace(latest, { definition: initial }));
    expect(first).not.toHaveBeenCalled();
    expect(latest).not.toHaveBeenCalled();
    view.unmount();
    expect(latest).toHaveBeenCalledWith(
      initial.id,
      initial.revision,
      "button.default-laser",
      false,
      true,
    );
  });

  it.each(["changed", "disabled", "disconnect", "hidden"])(
    "dismisses a pending action confirmation when its target is %s",
    (reason) => {
      const initial = structuredClone(definition);
      initial.sections = [initial.sections[0]!];
      const button = initial.sections[0]!.buttons[0]!;
      button.confirmation = "confirm";
      const invoke = vi.fn();
      const view = render(workspace(invoke, { definition: initial }));
      fireEvent.click(screen.getByRole("button", { name: "Play or pause" }));
      expect(screen.getByRole("dialog")).toBeTruthy();
      if (reason === "disconnect") {
        view.rerender(workspace(invoke, { definition: initial, state: "connecting" }));
      } else if (reason === "hidden") {
        button.landscape = { order: 0, visible: false };
        Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
        Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
        fireEvent(window, new Event("resize"));
      } else {
        const next = structuredClone(initial);
        if (reason === "changed") {
          next.sections[0]!.buttons[0]!.name = "Different action";
        } else {
          next.sections[0]!.buttons[0]!.enabled = false;
        }
        view.rerender(workspace(invoke, { definition: next }));
      }
      expect(screen.queryByRole("dialog")).toBeNull();
      if (reason === "disconnect") {
        view.rerender(workspace(invoke, { definition: initial }));
        expect(screen.queryByRole("dialog")).toBeNull();
      }
      expect(invoke).not.toHaveBeenCalled();
    },
  );

  it("applies orientation order and visibility while preserving accessible button names", () => {
    const view = renderWorkspace();

    expect(screen.getAllByRole("heading", { level: 2 }).map((item) => item.textContent)).toEqual([
      "Portrait only",
      "Transport",
    ]);
    expect(screen.getByRole("button", { name: "Play or pause" }).textContent).toBe("");
    expect(
      screen
        .getByRole("button", { name: "Play or pause" })
        .getAttribute("data-custom-screen-button-id"),
    ).toBe("button.play");
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

    expect(invoke).toHaveBeenCalledExactlyOnceWith("screen.media", "revision.one", "button.play");
  });

  it("renders authoritative laser colors and blocks pending recolor while keeping off available", () => {
    const invoke = vi.fn();
    const laserDefinition = withLaserButtons();
    const view = render(
      <CustomScreenWorkspace
        definition={laserDefinition}
        invoke={invoke}
        onBack={vi.fn()}
        pendingButtonIds={new Set(["button.green-laser"])}
        presentationCapability={{
          canControl: true,
          canSaveReports: true,
          laserPointerActive: true,
          laserPointerColor: "red",
          laserPointerDefaultColor: "red",
        }}
        requestedName="Lasers"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    const defaultLaser = screen.getByRole<HTMLButtonElement>("button", { name: "Default laser" });
    const redLaser = screen.getByRole<HTMLButtonElement>("button", { name: "Red laser" });
    const greenLaser = screen.getByRole<HTMLButtonElement>("button", { name: "Green laser" });
    expect(defaultLaser.getAttribute("aria-pressed")).toBe("true");
    expect(redLaser.getAttribute("aria-pressed")).toBe("true");
    expect(defaultLaser.disabled).toBe(false);
    expect(greenLaser.disabled).toBe(true);

    fireEvent.click(defaultLaser);
    expect(invoke).toHaveBeenLastCalledWith(
      "screen.lasers",
      "revision.lasers",
      "button.default-laser",
      false,
    );

    view.unmount();
    expect(invoke).toHaveBeenLastCalledWith(
      "screen.lasers",
      "revision.lasers",
      "button.default-laser",
      false,
      true,
    );
  });

  it("does not optimistically press a laser recolor", () => {
    const invoke = vi.fn();
    render(
      <CustomScreenWorkspace
        definition={withLaserButtons()}
        invoke={invoke}
        onBack={vi.fn()}
        pendingButtonIds={new Set()}
        presentationCapability={{
          canControl: true,
          canSaveReports: true,
          laserPointerActive: true,
          laserPointerColor: "red",
          laserPointerDefaultColor: "red",
        }}
        requestedName="Lasers"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    const greenLaser = screen.getByRole("button", { name: "Green laser" });
    expect(greenLaser.getAttribute("aria-pressed")).toBe("false");
    fireEvent.click(greenLaser);
    expect(greenLaser.getAttribute("aria-pressed")).toBe("false");
    expect(invoke).toHaveBeenCalledWith("screen.lasers", "revision.lasers", "button.green-laser");
  });

  it("requires confirmation before invoking protected host actions", () => {
    const invoke = vi.fn();
    const protectedDefinition: CustomScreenDefinition = {
      ...definition,
      sections: [
        {
          ...definition.sections[0]!,
          buttons: [
            {
              ...definition.sections[0]!.buttons[0]!,
              name: "Sleep PC",
              confirmation: "confirm",
              confirmationMessage: "Voltura Air will disconnect.",
            },
          ],
        },
      ],
    };
    render(<CustomScreenWorkspace {...workspace(invoke).props} definition={protectedDefinition} />);

    fireEvent.click(screen.getAllByRole("button", { name: "Sleep PC" }).at(-1)!);
    expect(invoke).not.toHaveBeenCalled();
    expect(screen.getByText("Voltura Air will disconnect.")).toBeTruthy();
    fireEvent.click(screen.getAllByRole("button", { name: "Sleep PC" }).at(-1)!);
    expect(invoke).toHaveBeenCalledExactlyOnceWith("screen.media", "revision.one", "button.play");
  });

  it("requires an uninterrupted hold before invoking destructive host actions", () => {
    vi.useFakeTimers();
    const invoke = vi.fn();
    const protectedDefinition: CustomScreenDefinition = {
      ...definition,
      sections: [
        {
          ...definition.sections[0]!,
          buttons: [
            {
              ...definition.sections[0]!.buttons[0]!,
              name: "Shut down PC",
              confirmation: "hold",
              confirmationMessage: "Unsaved work may be lost.",
            },
          ],
        },
      ],
    };
    render(<CustomScreenWorkspace {...workspace(invoke).props} definition={protectedDefinition} />);

    fireEvent.click(screen.getByRole("button", { name: "Shut down PC" }));
    const hold = screen.getByRole("button", { name: "Hold to shut down pc" });
    expect(document.activeElement).toBe(hold);
    fireEvent.pointerDown(hold, { button: 0, pointerId: 1 });
    vi.advanceTimersByTime(1599);
    expect(invoke).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(invoke).toHaveBeenCalledExactlyOnceWith("screen.media", "revision.one", "button.play");
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
      />,
    );

    expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(
      view.container.querySelector(".custom-screen-workspace")?.classList.contains("header-hidden"),
    ).toBe(true);
  });

  it("places buttons in their selected rows", () => {
    const view = renderWorkspace();

    expect(screen.getByRole("button", { name: "Play or pause" }).parentElement?.dataset.row).toBe(
      "2",
    );
    const compact = screen.getByRole("button", { name: "Volume up" });
    expect(compact.parentElement?.dataset.row).toBe("1");
    expect(compact.classList.contains("size-compact")).toBe(true);
    expect(compact.parentElement?.dataset.buttonAlignment).toBe("start");

    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));
    view.rerender(workspace());

    expect(screen.getByRole("button", { name: "Play or pause" }).parentElement?.dataset.row).toBe(
      "1",
    );
    expect(screen.getByRole("button", { name: "Volume up" }).parentElement?.dataset.row).toBe("2");
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

    expect(invoke).toHaveBeenNthCalledWith(2, "screen.media", "revision.one", "button.play");
  });

  it("does not duplicate a canceled repeat press when its pointer click follows", () => {
    const invoke = vi.fn();
    renderWorkspace(invoke);
    const repeating = screen.getByRole("button", { name: "Volume up" });

    fireEvent.pointerDown(repeating, { button: 0, pointerId: 4 });
    fireEvent.pointerCancel(repeating, { pointerId: 4 });
    fireEvent.click(repeating, { detail: 1 });

    expect(invoke).toHaveBeenCalledExactlyOnceWith("screen.media", "revision.one", "button.volume");
  });

  it("renders a standalone volume slider and reuses the audio protocol", () => {
    const send = vi.fn();
    const volumeDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [
        {
          ...definition.sections[0]!,
          id: "section.volume",
          name: "Volume slider",
          showHeader: false,
          widthColumns: 6,
          heightMode: "content",
          rowLimit: 0,
          kind: "volume",
          buttons: [],
          volumeEnabled: true,
        },
      ],
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
      />,
    );

    expect(send).toHaveBeenCalledWith({ type: "audio.get" });
    expect(view.container.querySelector<HTMLElement>(".kind-volume")?.style.gridColumn).toBe(
      "span 6",
    );
    const slider = screen.getByRole<HTMLInputElement>("slider", { name: "PC volume" });
    expect(slider.value).toBe("42");
    fireEvent.change(slider, { target: { value: "67" } });
    expect(send).toHaveBeenCalledWith({
      type: "audio.volume.set",
      inputContext: "custom-screens",
      volume: 67,
    });
    fireEvent.click(screen.getByRole("button", { name: "Mute PC" }));
    expect(send).toHaveBeenCalledWith({
      type: "audio.mute.toggle",
      inputContext: "custom-screens",
    });
  });

  it("expands and collapses collapsible sections with a required header", () => {
    const collapsibleDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [
        {
          ...definition.sections[0]!,
          id: "section.collapsible",
          name: "Advanced controls",
          kind: "buttons",
          collapsible: true,
          initiallyExpanded: false,
          showHeader: true,
          heightMode: "fill",
          fillWeight: 2,
          widthColumns: 6,
        },
      ],
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
      />,
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
      sections: [
        {
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
          trackpadGyroControl: true,
          trackpadEnabled: true,
          volumeEnabled: true,
          buttons: [],
        },
      ],
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
      />,
    );

    const layout = screen.getByRole("application", { name: "Pointer" }).parentElement!;
    expect(layout.classList.contains("buttons-left")).toBe(true);
    expect(screen.getByRole("group", { name: "Trackpad movement" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Touch" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "Gyro" })).toBeTruthy();
    expect(
      screen
        .getAllByRole("button", { name: /click/i })
        .map((button) => button.getAttribute("aria-label")),
    ).toEqual(["Right click", "Left click"]);

    const leftClick = screen.getByRole("button", { name: "Left click" });
    fireEvent.pointerDown(leftClick, { button: 0, pointerId: 8 });
    fireEvent.pointerUp(leftClick, { button: 0, pointerId: 8 });
    expect(send).toHaveBeenNthCalledWith(1, {
      type: "pointer.button",
      inputContext: "custom-screens",
      button: "left",
      action: "down",
    });
    expect(send).toHaveBeenNthCalledWith(2, {
      type: "pointer.button",
      inputContext: "custom-screens",
      button: "left",
      action: "up",
    });

    fireEvent.click(screen.getByRole("button", { name: "Expand Pointer" }));
    expect(layout.classList.contains("is-fullscreen")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Restore Pointer" }));
    expect(layout.classList.contains("is-fullscreen")).toBe(false);
  });

  it("uses the custom trackpad as a Gyro clutch and releases it cleanly", async () => {
    const send = vi.fn();
    const onGyroSelectedChange = vi.fn();
    const trackpadDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [
        {
          id: "section.gyro",
          name: "Gyro pointer",
          showHeader: true,
          widthColumns: 12,
          heightMode: "fill",
          fillWeight: 1,
          rowLimit: 0,
          buttonAlignment: "start",
          kind: "trackpad",
          collapsible: false,
          initiallyExpanded: true,
          trackpadLeftClick: false,
          trackpadRightClick: true,
          trackpadButtonSide: "right",
          trackpadFullscreenControl: true,
          trackpadGyroControl: true,
          trackpadEnabled: true,
          volumeEnabled: true,
          buttons: [],
        },
      ],
    };
    const view = render(
      <CustomScreenWorkspace
        connectionEpoch={7}
        definition={trackpadDefinition}
        gyroActivationRequest={{ id: 1, permission: Promise.resolve(true) }}
        invoke={vi.fn()}
        onBack={vi.fn()}
        onGyroSelectedChange={onGyroSelectedChange}
        pendingButtonIds={new Set()}
        requestedName="Gyro pointer"
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Gyro" }).getAttribute("aria-pressed")).toBe(
        "true",
      );
    });
    const surface = screen.getByRole("application", { name: "Gyro pointer" });

    fireEvent.pointerDown(surface, { button: 0, isPrimary: true, pointerId: 31 });
    fireEvent.pointerUp(surface, { button: 0, isPrimary: true, pointerId: 31 });
    expect(send).toHaveBeenCalledWith({
      type: "pointer.button",
      inputContext: "custom-screens",
      button: "left",
      action: "click",
    });
    send.mockClear();

    fireEvent.pointerDown(surface, { button: 0, isPrimary: true, pointerId: 32 });
    const rightClick = screen.getByRole("button", { name: "Right click" });
    fireEvent.pointerDown(rightClick, { button: 0, pointerId: 33 });
    fireEvent.pointerUp(rightClick, { button: 0, pointerId: 33 });
    fireEvent.pointerUp(surface, { button: 0, isPrimary: true, pointerId: 32 });
    expect(send.mock.calls).toEqual([
      [{ type: "pointer.button", inputContext: "custom-screens", button: "right", action: "down" }],
      [{ type: "pointer.button", inputContext: "custom-screens", button: "right", action: "up" }],
    ]);

    vi.useFakeTimers();
    fireEvent.pointerDown(surface, { button: 0, isPrimary: true, pointerId: 34 });
    fireEvent(window, new Event("blur"));
    act(() => {
      vi.advanceTimersByTime(301);
    });
    expect(surface.classList.contains("gyro-engaged")).toBe(false);

    fireEvent.pointerDown(surface, { button: 0, isPrimary: true, pointerId: 35 });
    act(() => {
      vi.advanceTimersByTime(301);
    });
    expect(surface.classList.contains("gyro-engaged")).toBe(true);
    fireEvent(window, new Event("blur"));
    expect(surface.classList.contains("gyro-engaged")).toBe(false);

    view.unmount();
    expect(onGyroSelectedChange).toHaveBeenLastCalledWith(false);
  });

  it("uses the existing custom-trackpad gesture for two-finger Gyro scrolling", async () => {
    const send = vi.fn();
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        frames.push(callback);
        return frames.length;
      }),
    );
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    render(
      <CustomScreenWorkspace
        connectionEpoch={8}
        definition={gyroTrackpadDefinition()}
        gyroActivationRequest={{ id: 4, permission: Promise.resolve(true) }}
        invoke={vi.fn()}
        onBack={vi.fn()}
        onGyroSelectedChange={vi.fn()}
        pendingButtonIds={new Set()}
        requestedName="Gyro pointer"
        send={send}
        state="paired"
        trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
      />,
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Gyro" }).getAttribute("aria-pressed")).toBe(
        "true",
      );
    });
    const surface = screen.getByRole("application", { name: "Gyro pointer" });
    expect(screen.getByText("Trackpad").classList.contains("custom-screen-trackpad-label")).toBe(
      true,
    );
    const start = [
      { identifier: 41, clientX: 100, clientY: 100 },
      { identifier: 42, clientX: 140, clientY: 100 },
    ];
    fireEvent.touchStart(surface, { touches: start, targetTouches: start });
    fireEvent.touchMove(surface, {
      touches: start.map((touch) => ({ ...touch, clientY: touch.clientY + 20 })),
      targetTouches: start.map((touch) => ({ ...touch, clientY: touch.clientY + 20 })),
    });
    act(() => {
      frames.at(-1)?.(0);
    });
    fireEvent.touchEnd(surface, {
      touches: [start[1]],
      targetTouches: [start[1]],
      changedTouches: [start[0]],
    });
    fireEvent.touchEnd(surface, {
      touches: [],
      targetTouches: [],
      changedTouches: [start[1]],
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "pointer.wheel",
      inputContext: "custom-screens",
      dx: 0,
      dy: -22,
    });
  });

  it("stops Gyro when its collapsible trackpad is folded", async () => {
    const onGyroSelectedChange = vi.fn();
    render(
      <CustomScreenWorkspace
        definition={gyroTrackpadDefinition({ collapsible: true })}
        gyroActivationRequest={{ id: 2, permission: Promise.resolve(true) }}
        invoke={vi.fn()}
        onBack={vi.fn()}
        onGyroSelectedChange={onGyroSelectedChange}
        pendingButtonIds={new Set()}
        requestedName="Gyro pointer"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Gyro" }).getAttribute("aria-pressed")).toBe(
        "true",
      );
    });
    fireEvent.click(screen.getByRole("button", { name: "Gyro pointer" }));

    await waitFor(() => {
      expect(onGyroSelectedChange).toHaveBeenLastCalledWith(false);
    });
    expect(screen.queryByRole("group", { name: "Trackpad movement" })).toBeNull();
  });

  it("stops Gyro when orientation hides its trackpad", async () => {
    const onGyroSelectedChange = vi.fn();
    render(
      <CustomScreenWorkspace
        definition={gyroTrackpadDefinition({ orientationLayoutsEnabled: true })}
        gyroActivationRequest={{ id: 3, permission: Promise.resolve(true) }}
        invoke={vi.fn()}
        onBack={vi.fn()}
        onGyroSelectedChange={onGyroSelectedChange}
        pendingButtonIds={new Set()}
        requestedName="Gyro pointer"
        send={vi.fn()}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />,
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Gyro" }).getAttribute("aria-pressed")).toBe(
        "true",
      );
    });
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));

    await waitFor(() => {
      expect(onGyroSelectedChange).toHaveBeenLastCalledWith(false);
    });
    expect(screen.queryByRole("application", { name: "Gyro pointer" })).toBeNull();
  });

  it("folds a collapsible trackpad and restores it to its responsive row", () => {
    const trackpadDefinition: CustomScreenDefinition = {
      ...definition,
      orientationLayoutsEnabled: false,
      sections: [
        {
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
          trackpadGyroControl: false,
          trackpadLeftClick: true,
          trackpadRightClick: true,
          trackpadButtonSide: "right",
          trackpadEnabled: true,
          volumeEnabled: true,
          buttons: [],
        },
      ],
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
      />,
    );

    expect(screen.queryByRole("application", { name: "Pointer tools" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Pointer tools" }));
    expect(screen.getByRole("application", { name: "Pointer tools" })).toBeTruthy();
    expect(screen.queryByRole("group", { name: "Trackpad movement" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Expand Pointer tools" }));
    expect(
      screen
        .getByRole("application", { name: "Pointer tools" })
        .parentElement?.classList.contains("is-fullscreen"),
    ).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Restore Pointer tools" }));
    expect(
      screen
        .getByRole("application", { name: "Pointer tools" })
        .parentElement?.classList.contains("is-fullscreen"),
    ).toBe(false);
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
          landscape: { order: 0, visible: true, widthColumns: 6 },
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
          trackpadGyroControl: false,
          portrait: { order: 1, visible: true, widthColumns: 12 },
          landscape: { order: 1, visible: true, widthColumns: 6 },
          trackpadEnabled: true,
          volumeEnabled: true,
          buttons: [],
        },
      ],
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
      />,
    );

    expect(view.container.querySelectorAll(".custom-screen-row")).toHaveLength(2);
    expect(
      Array.from(view.container.querySelectorAll<HTMLElement>(".custom-screen-row")).map(
        (row) => row.style.flexGrow,
      ),
    ).toEqual(["1", "2"]);

    Object.defineProperty(window, "innerWidth", { configurable: true, value: 900 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    fireEvent(window, new Event("resize"));

    expect(view.container.querySelectorAll(".custom-screen-row")).toHaveLength(1);
    expect(view.container.querySelector<HTMLElement>(".custom-screen-row")?.style.flexGrow).toBe(
      "2",
    );
  });
});

function renderWorkspace(invoke = vi.fn()) {
  return render(workspace(invoke));
}

function workspace(
  invoke = vi.fn(),
  overrides: Partial<ComponentProps<typeof CustomScreenWorkspace>> = {},
) {
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
      {...overrides}
    />
  );
}

function withLaserButtons(): CustomScreenDefinition {
  return {
    ...definition,
    id: "screen.lasers",
    name: "Lasers",
    revision: "revision.lasers",
    orientationLayoutsEnabled: false,
    sections: [
      {
        ...definition.sections[0]!,
        portrait: null,
        landscape: null,
        buttons: [
          laserButton("button.default-laser", "Default laser", "default"),
          laserButton("button.red-laser", "Red laser", "red"),
          laserButton("button.green-laser", "Green laser", "green"),
        ],
      },
    ],
  };
}

function gyroTrackpadDefinition({
  collapsible = false,
  orientationLayoutsEnabled = false,
}: {
  collapsible?: boolean;
  orientationLayoutsEnabled?: boolean;
} = {}): CustomScreenDefinition {
  return {
    ...definition,
    orientationLayoutsEnabled,
    sections: [
      {
        ...definition.sections[0]!,
        id: "section.gyro",
        name: "Gyro pointer",
        rowLimit: 0,
        kind: "trackpad",
        collapsible,
        initiallyExpanded: true,
        trackpadLeftClick: false,
        trackpadRightClick: false,
        trackpadFullscreenControl: true,
        trackpadGyroControl: true,
        portrait: { order: 0, visible: true, widthColumns: 12 },
        landscape: { order: 0, visible: false, widthColumns: 12 },
        buttons: [],
      },
    ],
  };
}

function laserButton(id: string, name: string, color: "default" | "red" | "green") {
  return {
    id,
    name,
    label: name,
    icon: "mouse-pointer-2",
    presentation: "iconLabel" as const,
    size: "standard" as const,
    repeat: false,
    row: 0,
    enabled: true,
    laserPointerColor: color,
  };
}
