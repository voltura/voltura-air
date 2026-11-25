import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useScreenViewFullscreen } from "./useScreenViewFullscreen";

const originalFullscreen = Object.getOwnPropertyDescriptor(document, "fullscreenElement");
const originalExit = Object.getOwnPropertyDescriptor(document, "exitFullscreen");
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  for (const [name, descriptor] of [
    ["fullscreenElement", originalFullscreen],
    ["exitFullscreen", originalExit],
  ] as const) {
    if (descriptor) {
      Object.defineProperty(document, name, descriptor);
    } else {
      Reflect.deleteProperty(document, name);
    }
  }
});

function setup() {
  let finish!: () => void;
  const workspace = document.createElement("section");
  const request = vi.fn(
    () =>
      new Promise<void>((resolve) => {
        finish = resolve;
      }),
  );
  workspace.requestFullscreen = request;
  let fullscreen: Element | null = null;
  Object.defineProperty(document, "fullscreenElement", {
    configurable: true,
    get: () => fullscreen,
  });
  const exit = vi.fn(() => {
    fullscreen = null;
    return Promise.resolve();
  });
  Object.defineProperty(document, "exitFullscreen", { configurable: true, value: exit });
  const hook = renderHook(() => useScreenViewFullscreen());
  hook.result.current.workspaceRef.current = workspace;
  return {
    ...hook,
    request,
    exit,
    workspace,
    setFullscreen: (element: Element | null) => {
      fullscreen = element;
      document.dispatchEvent(new Event("fullscreenchange"));
    },
    settleRequest: () => finish(),
    complete: () => {
      fullscreen = workspace;
      finish();
    },
  };
}

describe("Screen View fullscreen ownership", () => {
  it("deduplicates entry before React can render the immersive state", async () => {
    const view = setup();
    let first!: Promise<void>;
    act(() => {
      first = view.result.current.enterImmersive();
      void view.result.current.enterImmersive();
    });
    expect(view.request).toHaveBeenCalledOnce();
    await act(async () => {
      view.complete();
      await first;
    });
    expect(view.result.current.immersive).toBe(true);
  });

  it("exits a late native entry when the user has already exited", async () => {
    const view = setup();
    let entry!: Promise<void>;
    act(() => {
      entry = view.result.current.enterImmersive();
    });
    await act(() => view.result.current.exitImmersive());
    await act(async () => {
      view.complete();
      await entry;
    });
    expect(view.exit).toHaveBeenCalledOnce();
    expect(view.result.current.immersive).toBe(false);
  });

  it("releases native fullscreen after React detaches the workspace ref", async () => {
    const view = setup();
    let entry!: Promise<void>;
    act(() => {
      entry = view.result.current.enterImmersive();
    });
    await act(async () => {
      view.complete();
      await entry;
    });
    view.result.current.workspaceRef.current = null;
    view.unmount();
    expect(view.exit).toHaveBeenCalledOnce();
  });

  it("releases a pending native entry that finishes after unmount", async () => {
    const view = setup();
    let entry!: Promise<void>;
    act(() => {
      entry = view.result.current.enterImmersive();
    });
    view.result.current.workspaceRef.current = null;
    view.unmount();
    view.complete();
    await entry;
    expect(view.exit).toHaveBeenCalledOnce();
  });

  it("releases a former workspace's late native entry during a replacement's fallback", async () => {
    const view = setup();
    let first!: Promise<void>;
    act(() => { first = view.result.current.enterImmersive(); });
    await act(() => view.result.current.exitImmersive());
    view.result.current.workspaceRef.current = document.createElement("section");
    await act(() => view.result.current.enterImmersive());

    await act(async () => {
      view.complete();
      await first;
    });

    expect(view.exit).toHaveBeenCalledOnce();
    expect(view.result.current.immersive).toBe(true);
  });

  it("acknowledges browser entry and exit even before the native promise settles", async () => {
    vi.useFakeTimers();
    const view = setup();
    let entry!: Promise<void>;
    act(() => { entry = view.result.current.enterImmersive(); });
    act(() => {
      view.setFullscreen(view.workspace);
      view.setFullscreen(null);
      vi.advanceTimersByTime(200);
    });
    await act(async () => {
      view.settleRequest();
      await entry;
    });

    expect(view.result.current.immersive).toBe(false);
  });

  it("ignores a delayed exit when native fullscreen already returned", async () => {
    vi.useFakeTimers();
    const view = setup();
    let entry!: Promise<void>;
    act(() => { entry = view.result.current.enterImmersive(); });
    await act(async () => {
      view.complete();
      await entry;
    });

    act(() => {
      view.setFullscreen(null);
      vi.advanceTimersByTime(100);
      view.setFullscreen(view.workspace);
      vi.advanceTimersByTime(200);
    });

    expect(view.result.current.immersive).toBe(true);
  });

  it("leaves fullscreen alone when Screen View owns no entry", async () => {
    const view = setup();
    act(() => view.setFullscreen(view.workspace));
    await act(() => view.result.current.exitImmersive());
    expect(view.exit).not.toHaveBeenCalled();
  });
});
