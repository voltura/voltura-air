import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ComponentProps } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishAppsResult } from "../../foundation/connection/appsResultBus";
import type { AppsListResultMessage, ClientMessage } from "../../foundation/protocol/messages";
import { AppsWorkspace } from "./AppsWorkspace";

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));
vi.mock("../../foundation/webrtc/iceGathering", () => ({
  hasOnlyRelayCandidates: () => true,
  waitForIceGathering: () => Promise.resolve(),
}));
vi.mock("../../foundation/webrtc/sessionCrypto", () => ({
  hashSessionDescription: (value: string) => `hash:${value}`,
  verifyHostSessionSignature: () => true,
}));

const revision = "0123456789abcdef0123456789abcdef";
const firstId = "11111111111111111111111111111111";
const secondId = "22222222222222222222222222222222";
let previewPeer: FakePreviewPeer | null = null;
let resizeObserverCallback: ResizeObserverCallback | null = null;

describe("Apps workspace", () => {
  beforeEach(() => {
    resizeObserverCallback = null;
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: ResizeObserverCallback) {
          resizeObserverCallback = callback;
        }

        observe = vi.fn();
        disconnect = vi.fn();
      },
    );
    HTMLElement.prototype.scrollTo = vi.fn();
    previewPeer = null;
    vi.stubGlobal("RTCPeerConnection", StubRTCPeerConnection);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("holds the active-card space while discovering open applications", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    expect(screen.getByRole("status", { name: "Loading open applications" })).toBeTruthy();
    expect(screen.getByText("Loading…")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Open application launcher" })).toBeNull();

    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    expect(screen.queryByText("Loading…")).toBeNull();
    expect(await screen.findByRole("button", { name: /activate browser/i })).toBeTruthy();
    expect(screen.getByRole("button", { name: /select notes/i })).toBeTruthy();
  });

  it("reveals the active card when its initial preview does not settle", async () => {
    vi.useFakeTimers();
    try {
      const send = vi.fn<(message: ClientMessage) => void>();
      renderWorkspace(send, {
        capability: {
          enabled: true,
          permissionGranted: true,
          canUse: true,
          previewAvailable: true,
        },
      });

      await act(async () => vi.runOnlyPendingTimersAsync());
      const list = send.mock.calls[0]?.[0];
      if (list?.type !== "apps.list") {
        throw new TypeError("Expected Apps list request.");
      }
      const result = listResult(list.operationId);
      act(() =>
        publishAppsResult({
          ...result,
          windows: result.windows.map((window) => ({ ...window, previewSupported: true })),
        }),
      );

      expect(screen.getByRole("status", { name: "Loading open applications" })).toBeTruthy();
      await act(async () => vi.advanceTimersByTimeAsync(2_500));
      expect(screen.getByLabelText("Open applications")).toBeTruthy();
      expect(screen.getAllByText("Loading preview…").length).toBeGreaterThan(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it("opens Trackpad from the header without sending another Apps message", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onOpenTrackpad = vi.fn();
    renderWorkspace(send, { onOpenTrackpad });

    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const header = screen.getByRole("heading", { name: "Apps" }).closest("header");
    expect(
      [...(header?.querySelectorAll("button") ?? [])].map((button) =>
        button.getAttribute("aria-label"),
      ),
    ).toEqual(["Back", "Open Trackpad", "Refresh applications"]);

    fireEvent.click(screen.getByRole("button", { name: "Open Trackpad" }));

    expect(onOpenTrackpad).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("lists once on entry and only activates the centered card", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    expect(list.type).toBe("apps.list");
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }

    act(() => publishAppsResult(listResult(list.operationId)));

    fireEvent.click(await screen.findByRole("button", { name: /select notes/i }));
    expect(send).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole("button", { name: /activate browser/i }));
    expect(send.mock.calls.at(-1)?.[0]).toMatchObject({
      type: "apps.activate",
      revision,
      windowId: secondId,
    });
  });

  it("refreshes the window list once when the app returns from the background", async () => {
    let visibilityState: DocumentVisibilityState = "visible";
    vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visibilityState);
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);

    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    visibilityState = "hidden";
    fireEvent(document, new Event("visibilitychange"));
    visibilityState = "visible";
    fireEvent(document, new Event("visibilitychange"));

    await waitFor(() =>
      expect(send.mock.calls.filter(([message]) => message.type === "apps.list")).toHaveLength(2),
    );
  });

  it("shows the active badge as soon as Windows accepts activation", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    const initial = listResult(list.operationId);
    act(() =>
      publishAppsResult({
        ...initial,
        windows: initial.windows.map((window) => ({ ...window, active: false })),
      }),
    );

    const notesCard = await screen.findByRole("button", { name: /activate notes/i });
    expect(notesCard.closest("article")?.querySelector(".apps-active-badge")).toBeNull();
    fireEvent.click(notesCard);
    const activate = send.mock.calls.at(-1)?.[0];
    if (activate?.type !== "apps.activate") {
      throw new TypeError("Expected Apps activate request.");
    }

    act(() =>
      publishAppsResult({
        type: "apps.activate.result",
        operationId: activate.operationId,
        windowId: firstId,
        succeeded: true,
        code: "accepted",
        message: "Application activated.",
      }),
    );

    expect(notesCard.closest("article")?.querySelector(".apps-active-badge")?.textContent).toBe(
      "Active",
    );
    expect(send.mock.calls.filter(([message]) => message.type === "apps.list")).toHaveLength(2);
  });

  it("offers an explicit close control and refreshes after an accepted close", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onFeedback = vi.fn();
    renderWorkspace(send, { onFeedback });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    fireEvent.click(await screen.findByRole("button", { name: /close browser/i }));
    const close = send.mock.calls.at(-1)?.[0];
    expect(close).toMatchObject({ type: "apps.close", windowId: secondId });
    if (close?.type !== "apps.close") {
      throw new TypeError("Expected Apps close request.");
    }

    act(() =>
      publishAppsResult({
        type: "apps.close.result",
        operationId: close.operationId,
        windowId: secondId,
        succeeded: true,
        code: "accepted",
        message: "Close requested.",
      }),
    );
    await waitFor(() =>
      expect(send.mock.calls.filter(([message]) => message.type === "apps.list")).toHaveLength(2),
    );
    const refresh = send.mock.calls.filter(([message]) => message.type === "apps.list").at(-1)?.[0];
    if (refresh?.type !== "apps.list") {
      throw new TypeError("Expected Apps refresh request.");
    }
    act(() => publishAppsResult(listResult(refresh.operationId)));
    expect(await screen.findByText("Check the PC for a save or confirmation prompt.")).toBeTruthy();
    expect(onFeedback).not.toHaveBeenCalled();
  });

  it("opens a scrollable application launcher and returns to the deck", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onAppLaunch = vi.fn();
    renderWorkspace(send, {
      appLaunchActions: [{ id: "custom.notes", label: "Notes", kind: "custom" }],
      onAppLaunch,
      supportsRemoteLaunch: true,
    });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult({ ...listResult(list.operationId), windows: [] }));

    fireEvent.click(await screen.findByRole("button", { name: "Open application launcher" }));
    fireEvent.click(await screen.findByRole("button", { name: "Notes" }));
    expect(onAppLaunch).toHaveBeenCalledWith("custom.notes");

    fireEvent.click(screen.getByRole("button", { name: "Close application launcher" }));
    expect(await screen.findByRole("button", { name: "Open application launcher" })).toBeTruthy();
  });

  it("refreshes once after Windows rejects a stale card", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onFeedback = vi.fn();
    renderWorkspace(send, { onFeedback });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    const initial = listResult(list.operationId);
    act(() =>
      publishAppsResult({
        ...initial,
        windows: [initial.windows[1]!, initial.windows[0]!],
      }),
    );

    fireEvent.click(await screen.findByRole("button", { name: /activate browser/i }));
    const activate = send.mock.calls.at(-1)?.[0];
    if (activate?.type !== "apps.activate") {
      throw new TypeError("Expected Apps activate request.");
    }
    act(() =>
      publishAppsResult({
        type: "apps.activate.result",
        operationId: activate.operationId,
        windowId: secondId,
        succeeded: false,
        code: "stale-window",
        message: "The application window is no longer available.",
      }),
    );

    expect(screen.queryByRole("button", { name: /activate browser/i })).toBeNull();
    expect(screen.queryByText("The application window is no longer available.")).toBeNull();
    expect(onFeedback).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(send.mock.calls.filter(([message]) => message.type === "apps.list")).toHaveLength(2),
    );

    const nextCard = screen.getByRole<HTMLButtonElement>("button", { name: /activate notes/i });
    expect(nextCard.disabled).toBe(true);
    fireEvent.click(nextCard);
    expect(send.mock.calls.filter(([message]) => message.type === "apps.activate")).toHaveLength(1);

    const refresh = send.mock.calls.filter(([message]) => message.type === "apps.list").at(-1)?.[0];
    if (refresh?.type !== "apps.list") {
      throw new TypeError("Expected Apps refresh request.");
    }
    const refreshedId = "33333333333333333333333333333333";
    act(() =>
      publishAppsResult({
        type: "apps.list.result",
        operationId: refresh.operationId,
        succeeded: true,
        code: "accepted",
        message: "Refreshed.",
        revision: "fedcba9876543210fedcba9876543210",
        windows: [{ ...initial.windows[0]!, windowId: refreshedId }],
      }),
    );

    const refreshedCard = await screen.findByRole<HTMLButtonElement>("button", {
      name: /activate notes/i,
    });
    expect(refreshedCard.disabled).toBe(false);
    fireEvent.click(refreshedCard);
    expect(send.mock.calls.at(-1)?.[0]).toMatchObject({
      type: "apps.activate",
      revision: "fedcba9876543210fedcba9876543210",
      windowId: refreshedId,
    });
  });

  it("clears a pending window action when a manual refresh succeeds", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const browserCard = await screen.findByRole<HTMLButtonElement>("button", {
      name: /activate browser/i,
    });
    fireEvent.click(browserCard);
    expect(browserCard.disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: /refresh applications/i }));
    const refresh = send.mock.calls.filter(([message]) => message.type === "apps.list").at(-1)?.[0];
    if (refresh?.type !== "apps.list") {
      throw new TypeError("Expected Apps refresh request.");
    }
    const refreshedId = "33333333333333333333333333333333";
    const refreshed = listResult(refresh.operationId);
    act(() =>
      publishAppsResult({
        ...refreshed,
        revision: "fedcba9876543210fedcba9876543210",
        windows: [refreshed.windows[0]!, { ...refreshed.windows[1]!, windowId: refreshedId }],
      }),
    );

    const recoveredCard = await screen.findByRole<HTMLButtonElement>("button", {
      name: /activate browser/i,
    });
    expect(recoveredCard.disabled).toBe(false);
    fireEvent.click(recoveredCard);
    expect(send.mock.calls.at(-1)?.[0]).toMatchObject({
      type: "apps.activate",
      revision: "fedcba9876543210fedcba9876543210",
      windowId: refreshedId,
    });
  });

  it("uses toast feedback only when a window action fails", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onFeedback = vi.fn();
    renderWorkspace(send, { onFeedback });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    fireEvent.click(await screen.findByRole("button", { name: /activate browser/i }));
    const activate = send.mock.calls.at(-1)?.[0];
    if (activate?.type !== "apps.activate") {
      throw new TypeError("Expected Apps activate request.");
    }
    act(() =>
      publishAppsResult({
        type: "apps.activate.result",
        operationId: activate.operationId,
        windowId: secondId,
        succeeded: false,
        code: "activation-rejected",
        message: "Windows did not allow the application to take focus.",
      }),
    );

    expect(onFeedback).toHaveBeenCalledExactlyOnceWith(
      "Windows did not allow the application to take focus.",
      "error",
    );
  });

  it("leaves horizontal touch movement to native overflow scrolling", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const carousel = await screen.findByLabelText("Open applications");
    carousel.scrollLeft = 610;
    fireEvent.touchStart(carousel, {
      touches: [{ identifier: 7, clientX: 250, clientY: 180 }],
    });
    fireEvent.touchMove(carousel, {
      cancelable: true,
      touches: [{ identifier: 7, clientX: 170, clientY: 181 }],
    });
    fireEvent.touchEnd(carousel, {
      changedTouches: [{ identifier: 7, clientX: 170, clientY: 181 }],
      touches: [],
    });
    expect(carousel.scrollLeft).toBe(610);
  });

  it("does not create loop clones for a single running app", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    const result = listResult(list.operationId);
    act(() => publishAppsResult({ ...result, windows: result.windows.slice(0, 1) }));

    const carousel = await screen.findByLabelText("Open applications");
    expect(carousel.querySelectorAll("[data-app-loop-clone=true]")).toHaveLength(0);
    expect(carousel.querySelectorAll("[data-app-canonical=true]")).toHaveLength(2);
  });

  it("continues from the final launcher card to the first app without exposing an end", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const carousel = await screen.findByLabelText("Open applications");
    const cards = carousel.querySelectorAll<HTMLElement>("[data-app-index]");
    expect(cards).toHaveLength(9);
    expect([...cards].map((card) => card.dataset.appIndex)).toEqual([
      "0",
      "1",
      "2",
      "0",
      "1",
      "2",
      "0",
      "1",
      "2",
    ]);
    expect(cards[0]?.dataset.appLoopClone).toBe("true");
    expect(cards[3]?.dataset.appCanonical).toBe("true");
    expect(cards[6]?.dataset.appLoopClone).toBe("true");
    expect(carousel.querySelectorAll(".apps-window-card.is-selected")).toHaveLength(1);
    expect(carousel.querySelectorAll('[aria-label="Close Browser"]')).toHaveLength(1);

    Object.defineProperty(carousel, "clientWidth", { configurable: true, value: 300 });
    cards.forEach((card, index) => {
      Object.defineProperty(card, "offsetLeft", { configurable: true, value: index * 220 });
      Object.defineProperty(card, "clientWidth", { configurable: true, value: 200 });
    });
    await act(() => new Promise((resolve) => window.setTimeout(resolve, 50)));
    const scrollToCallsBeforeWrap = vi.mocked(HTMLElement.prototype.scrollTo).mock.calls.length;
    carousel.scrollLeft = 1_270;
    fireEvent.scroll(carousel);
    await waitFor(() => expect(carousel.scrollLeft).toBe(610));

    expect(vi.mocked(HTMLElement.prototype.scrollTo).mock.calls).toHaveLength(
      scrollToCallsBeforeWrap,
    );
    expect(cards[3]?.style.getPropertyValue("--apps-card-focus")).toBe("1.000");
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("rebases an outer loop before the next swipe so repeated scrolling has no end", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const carousel = await screen.findByLabelText("Open applications");
    const cards = carousel.querySelectorAll<HTMLElement>("[data-app-index]");
    Object.defineProperty(carousel, "clientWidth", { configurable: true, value: 300 });
    cards.forEach((card, index) => {
      Object.defineProperty(card, "offsetLeft", { configurable: true, value: index * 220 });
      Object.defineProperty(card, "clientWidth", { configurable: true, value: 200 });
    });
    await act(() => new Promise((resolve) => window.setTimeout(resolve, 50)));

    carousel.scrollLeft = 1_270;
    fireEvent.touchStart(carousel, {
      touches: [{ identifier: 8, clientX: 240, clientY: 180 }],
    });
    expect(carousel.scrollLeft).toBe(610);

    carousel.scrollLeft = 1_270;
    fireEvent.touchStart(carousel, {
      touches: [{ identifier: 9, clientX: 240, clientY: 180 }],
    });
    expect(carousel.scrollLeft).toBe(610);
  });

  it("cannot remain stopped between two cards", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const carousel = await screen.findByLabelText("Open applications");
    const cards = carousel.querySelectorAll<HTMLElement>("[data-app-index]");
    Object.defineProperty(carousel, "clientWidth", { configurable: true, value: 300 });
    cards.forEach((card, index) => {
      Object.defineProperty(card, "offsetLeft", { configurable: true, value: index * 220 });
      Object.defineProperty(card, "clientWidth", { configurable: true, value: 200 });
    });
    await act(() => new Promise((resolve) => window.setTimeout(resolve, 50)));
    vi.mocked(HTMLElement.prototype.scrollTo).mockClear();

    carousel.scrollLeft = 720;
    fireEvent.scroll(carousel);
    await waitFor(() =>
      expect(HTMLElement.prototype.scrollTo).toHaveBeenLastCalledWith({
        behavior: "smooth",
        left: 610,
      }),
    );
  });

  it("recenters the selected card without changing selection during a viewport resize", async () => {
    let nextFrame = 0;
    const frames = new Map<number, FrameRequestCallback>();
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((callback: FrameRequestCallback) => {
        const frame = ++nextFrame;
        frames.set(frame, callback);
        return frame;
      }),
    );
    vi.stubGlobal(
      "cancelAnimationFrame",
      vi.fn((frame: number) => {
        frames.delete(frame);
      }),
    );
    const flushFrame = () => {
      const pending = [...frames.values()];
      frames.clear();
      pending.forEach((callback) => callback(0));
    };

    const send = vi.fn<(message: ClientMessage) => void>();
    renderWorkspace(send);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    act(() => publishAppsResult(listResult(list.operationId)));

    const carousel = await screen.findByLabelText("Open applications");
    const cards = carousel.querySelectorAll<HTMLElement>("[data-app-index]");
    Object.defineProperty(carousel, "clientWidth", { configurable: true, value: 300 });
    cards.forEach((card, index) => {
      Object.defineProperty(card, "offsetLeft", { configurable: true, value: index * 220 });
      Object.defineProperty(card, "clientWidth", { configurable: true, value: 200 });
    });
    act(flushFrame);
    act(flushFrame);

    carousel.scrollLeft = 390;
    act(() => resizeObserverCallback?.([], {} as ResizeObserver));
    act(flushFrame);
    expect(HTMLElement.prototype.scrollTo).toHaveBeenLastCalledWith({
      behavior: "auto",
      left: 830,
    });
    expect(cards[2]?.style.getPropertyValue("--apps-card-focus")).toBe("1.000");

    carousel.scrollLeft = 610;
    fireEvent.scroll(carousel);
    expect(screen.getByRole("button", { name: /activate browser/i })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /select browser/i })).toBeNull();
  });

  it("refreshes an activated preview without removing the painted image", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const onFeedback = vi.fn();
    renderWorkspace(send, {
      capability: {
        enabled: true,
        permissionGranted: true,
        canUse: true,
        previewAvailable: true,
      },
      onFeedback,
    });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(1));
    const list = send.mock.calls[0]![0];
    if (list.type !== "apps.list") {
      throw new TypeError("Expected Apps list request.");
    }
    const previewList = listResult(list.operationId);
    act(() =>
      publishAppsResult({
        ...previewList,
        windows: previewList.windows.map((window) => ({ ...window, previewSupported: true })),
      }),
    );
    act(() =>
      publishAppsResult({
        type: "apps.preview.offer",
        operationId: list.operationId,
        previewId: "33333333333333333333333333333333",
        offerSdp: "offer-sdp",
        hostSignature: "host-signature",
      }),
    );

    await waitFor(() =>
      expect(send.mock.calls.some(([message]) => message.type === "apps.preview.answer")).toBe(
        true,
      ),
    );
    const channel = new FakePreviewChannel();
    act(() => previewPeer?.emitDataChannel(channel));
    expect(channel.binaryType).toBe("arraybuffer");
    channel.readyState = "open";
    await act(() => channel.dispatchEvent(new Event("open")));

    await waitFor(() => expect(channel.send).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("status", { name: "Loading open applications" })).toBeTruthy();
    expect(screen.queryByLabelText("Open applications")).toBeNull();
    const request = new Uint8Array(channel.send.mock.calls[0]![0] as ArrayBuffer);
    expect(request[0]).toBe(0x11);
    expect(request[33]).toBe(2);

    for (const record of completePreviewRecords(secondId)) {
      await act(() => channel.dispatchEvent(new MessageEvent("message", { data: record })));
    }
    const canonicalCards = (await screen.findByLabelText("Open applications")).querySelectorAll(
      "[data-app-canonical=true]",
    );
    expect(
      [...canonicalCards].filter((card) => card.textContent?.includes("Loading preview…")),
    ).toHaveLength(1);

    await act(() =>
      channel.dispatchEvent(
        new MessageEvent("message", { data: unavailablePreviewRecord(firstId) }),
      ),
    );
    expect(
      [...canonicalCards].filter((card) => card.textContent?.includes("Preview unavailable")),
    ).toHaveLength(1);
    expect(
      [...canonicalCards].filter((card) => card.textContent?.includes("Loading preview…")),
    ).toHaveLength(0);

    for (const record of completePreviewRecords(firstId)) {
      await act(() => channel.dispatchEvent(new MessageEvent("message", { data: record })));
    }
    const notesPreview = screen
      .getByRole("button", { name: /select notes/i })
      .querySelector<HTMLImageElement>(".apps-preview-stage img")?.src;
    const browserPreview = screen
      .getByRole("button", { name: /activate browser/i })
      .querySelector<HTMLImageElement>(".apps-preview-stage img")?.src;
    expect(notesPreview).toMatch(/^blob:/u);
    expect(browserPreview).toMatch(/^blob:/u);

    fireEvent.click(screen.getByRole("button", { name: /activate browser/i }));
    const activate = send.mock.calls.at(-1)?.[0];
    if (activate?.type !== "apps.activate") {
      throw new TypeError("Expected Apps activate request.");
    }
    act(() =>
      publishAppsResult({
        type: "apps.activate.result",
        operationId: activate.operationId,
        windowId: secondId,
        succeeded: true,
        code: "accepted",
        message: "Application activated.",
      }),
    );
    expect(onFeedback).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(send.mock.calls.filter(([message]) => message.type === "apps.list")).toHaveLength(2),
    );
    const refresh = send.mock.calls.filter(([message]) => message.type === "apps.list").at(-1)?.[0];
    if (refresh?.type !== "apps.list") {
      throw new TypeError("Expected Apps refresh request.");
    }
    act(() =>
      publishAppsResult({
        ...previewList,
        operationId: refresh.operationId,
        revision: "fedcba9876543210fedcba9876543210",
        windows: [...previewList.windows]
          .reverse()
          .map((window) => ({ ...window, previewSupported: true })),
      }),
    );

    await waitFor(() => {
      expect(
        screen
          .getByRole("button", { name: /select notes/i })
          .querySelector<HTMLImageElement>(".apps-preview-stage img")?.src,
      ).toBe(notesPreview);
      expect(
        screen
          .getByRole("button", { name: /activate browser/i })
          .querySelector<HTMLImageElement>(".apps-preview-stage img")?.src,
      ).toBe(browserPreview);
    });
    await waitFor(() => expect(channel.send).toHaveBeenCalledTimes(2));
    const refreshedPreviewRequest = new Uint8Array(channel.send.mock.calls[1]![0] as ArrayBuffer);
    expect(refreshedPreviewRequest[33]).toBe(1);
    expect(new TextDecoder().decode(refreshedPreviewRequest.slice(34))).toBe(secondId);
    expect(
      [
        ...screen.getByLabelText("Open applications").querySelectorAll("[data-app-canonical=true]"),
      ].map((card) => card.querySelector("button")?.getAttribute("aria-label")),
    ).toEqual(["Select Notes, Notepad", "Activate Browser", "Open application launcher"]);
  });
});

class FakePreviewChannel extends EventTarget {
  readonly label = "voltura-apps-preview";
  binaryType: BinaryType = "blob";
  readyState: RTCDataChannelState = "connecting";
  readonly send = vi.fn<(data: ArrayBuffer) => void>();
  readonly close = vi.fn();
}

class FakePreviewPeer extends EventTarget {
  localDescription: RTCSessionDescriptionInit | null = null;
  readonly close = vi.fn();
  readonly createAnswer = vi.fn(() =>
    Promise.resolve({ type: "answer" as const, sdp: "answer-sdp" }),
  );
  readonly setRemoteDescription = vi.fn(() => Promise.resolve());
  readonly setLocalDescription = vi.fn((description: RTCSessionDescriptionInit) => {
    this.localDescription = description;
    return Promise.resolve();
  });

  emitDataChannel(channel: FakePreviewChannel) {
    const event = new Event("datachannel");
    Object.defineProperty(event, "channel", { value: channel });
    this.dispatchEvent(event);
  }
}

function StubRTCPeerConnection() {
  const peer = new FakePreviewPeer();
  previewPeer = peer;
  return peer;
}

function renderWorkspace(
  send: (message: ClientMessage) => void,
  overrides: Partial<ComponentProps<typeof AppsWorkspace>> = {},
) {
  return render(
    <AppsWorkspace
      activePc={{
        customName: false,
        id: "pc-a",
        name: "PC",
        url: "https://pc.local",
        hostIdentityPublicKey: "host-key",
      }}
      appLaunchActions={[]}
      appLaunchResult={null}
      capability={{ enabled: true, permissionGranted: true, canUse: true, previewAvailable: false }}
      clientId="client-a"
      onAppLaunch={vi.fn()}
      onBack={vi.fn()}
      onFeedback={vi.fn()}
      onOpenTrackpad={vi.fn()}
      pendingAppLaunchId={null}
      send={send}
      state="paired"
      supportsRemoteLaunch={false}
      {...overrides}
    />,
  );
}

function listResult(operationId: string): AppsListResultMessage {
  return {
    type: "apps.list.result",
    operationId,
    succeeded: true,
    code: "accepted",
    message: "Refreshed.",
    revision,
    windows: [
      {
        windowId: firstId,
        title: "Notes",
        applicationName: "Notepad",
        active: false,
        minimized: false,
        maximizeSupported: true,
        previewSupported: false,
      },
      {
        windowId: secondId,
        title: "Browser",
        applicationName: "Browser",
        active: true,
        minimized: false,
        maximizeSupported: true,
        previewSupported: false,
      },
    ],
  };
}

function unavailablePreviewRecord(windowId: string): ArrayBuffer {
  const record = new Uint8Array(43);
  record[0] = 0x12;
  record.set(new TextEncoder().encode(windowId), 1);
  record[42] = 1;
  return record.buffer;
}

function completePreviewRecords(windowId: string): [ArrayBuffer, ArrayBuffer] {
  const content = new Uint8Array([0xff, 0xd8, 0xff, 0xd9]);
  const header = new Uint8Array(43);
  header[0] = 0x12;
  header.set(new TextEncoder().encode(windowId), 1);
  header[33] = 1;
  new DataView(header.buffer).setUint16(34, 2);
  new DataView(header.buffer).setUint16(36, 2);
  new DataView(header.buffer).setUint32(38, content.length);
  header[42] = 1;
  const data = new Uint8Array(37 + content.length);
  data[0] = 0x13;
  data.set(new TextEncoder().encode(windowId), 1);
  data.set(content, 37);
  return [header.buffer, data.buffer];
}
