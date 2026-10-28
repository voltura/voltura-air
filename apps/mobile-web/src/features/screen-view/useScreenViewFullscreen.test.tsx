import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useScreenViewFullscreen } from "./useScreenViewFullscreen";

const originalFullscreen = Object.getOwnPropertyDescriptor(document, "fullscreenElement");
const originalExit = Object.getOwnPropertyDescriptor(document, "exitFullscreen");
afterEach(() => {
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
});
