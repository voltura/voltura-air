import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Introduction } from "./Introduction";
import { Introduction as IntroductionEntry } from "./IntroductionEntry";
import type { IntroductionCapabilities } from "./introductionSteps";

const command = vi.fn();
const capabilities = {
  supportsTextTransfer: true,
  remoteInputPermission: true,
} as IntroductionCapabilities;
function Harness({
  connected = true,
  connectionId = "pc-a:1",
  eligible = true,
  busy = false,
  entry = false,
}: {
  connected?: boolean;
  connectionId?: string;
  eligible?: boolean;
  busy?: boolean;
  entry?: boolean;
}) {
  const Guide = entry ? IntroductionEntry : Introduction;
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [replay, setReplay] = useState(0);
  return (
    <>
      <button data-introduction-target="menu">Menu</button>
      <button onClick={() => setReplay((value) => value + 1)}>Replay</button>
      <dialog className="settings-drawer" open={drawerOpen}>
        <button className="settings-drawer-light-dismiss" onClick={() => setDrawerOpen(false)}>
          Dismiss menu
        </button>
        <div className="settings-drawer-scroll-region">
          <button data-introduction-target="remote" onClick={command}>
            Remote tool
          </button>
          <button data-introduction-target="dictation" onClick={command}>
            Dictation tool
          </button>
        </div>
        <div data-introduction-slot />
      </dialog>
      <nav data-introduction-target="navigation">Modes</nav>
      <Guide
        capabilities={capabilities}
        eligible={eligible}
        connected={connected}
        connectionId={connectionId}
        busy={busy}
        drawerOpen={drawerOpen}
        setDrawerOpen={setDrawerOpen}
        replay={replay}
      />
    </>
  );
}
async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}
async function start() {
  await advance(850);
  fireEvent.click(screen.getByRole("button", { name: "Show me" }));
  await advance(30);
}

beforeEach(() => {
  vi.useFakeTimers();
  command.mockClear();
  localStorage.clear();
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({ matches: false })),
  );
  vi.spyOn(HTMLElement.prototype, "getClientRects").mockReturnValue([
    { width: 300, height: 44 },
  ] as unknown as DOMRectList);
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    top: 50,
    bottom: 250,
    left: 0,
    right: 300,
    width: 300,
    height: 200,
    x: 0,
    y: 50,
    toJSON: () => ({}),
  });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("guided introduction lifecycle", () => {
  it("retains a replay request until authenticated status is ready", async () => {
    const view = render(<Harness connected={false} eligible={false} />);
    fireEvent.click(screen.getByText("Replay"));
    await advance(100);
    expect(screen.queryByRole("group", { name: "Introduction controls" })).toBeNull();
    view.rerender(<Harness connected eligible={false} />);
    await advance(100);
    expect(screen.getByText("Find all your tools here.")).toBeTruthy();
  });

  it("starts the first replay when an existing client's lazy guide mounts", async () => {
    render(<Harness eligible={false} entry />);
    expect(screen.queryByRole("group", { name: "Introduction controls" })).toBeNull();
    await act(async () => {
      fireEvent.click(screen.getByText("Replay"));
      await import("./Introduction");
    });
    await advance(100);
    expect(screen.getByText("Find all your tools here.")).toBeTruthy();
  });

  it("waits for authentication and a quiet surface, supports Skip and replay", async () => {
    const view = render(<Harness connected={false} />);
    await advance(1000);
    expect(screen.queryByText("Welcome to Voltura Air")).toBeNull();
    view.rerender(<Harness busy />);
    await advance(1000);
    expect(screen.queryByText("Welcome to Voltura Air")).toBeNull();
    view.rerender(<Harness />);
    await advance(850);
    fireEvent.click(screen.getByRole("button", { name: "Skip" }));
    await advance(1000);
    expect(screen.queryByText("Welcome to Voltura Air")).toBeNull();
    fireEvent.click(screen.getByText("Replay"));
    await advance(30);
    expect(screen.getByText("Find all your tools here.")).toBeTruthy();
    expect(localStorage.getItem("voltura-air.introduction")).toContain('"state":"suppressed"');
  });
  it("positions each stop before six-second playback and waits for Done", async () => {
    render(<Harness />);
    await start();
    await advance(5900);
    expect(screen.getByText("Find all your tools here.")).toBeTruthy();
    await advance(150);
    expect(screen.getByText("Control media and navigate your PC.")).toBeTruthy();
    expect(screen.getByText("Remote tool").hasAttribute("data-introduction-highlight")).toBe(true);
    expect(
      screen.getByRole("group", { name: "Introduction controls" }).closest("dialog"),
    ).toBeTruthy();
    fireEvent.click(screen.getByText("Remote tool"));
    expect(command).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await advance(30000);
    expect(screen.getByText("Switch controls here whenever you need them.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Done" }));
    await advance(50);
    expect(screen.queryByRole("group", { name: "Introduction controls" })).toBeNull();
    expect(document.activeElement).toBe(screen.getByText("Menu"));
  });
  it("pauses on manual scrolling and keyboard use, resumes explicitly and cancels on Escape", async () => {
    render(<Harness />);
    await start();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await advance(30);
    fireEvent.wheel(document.querySelector(".settings-drawer-scroll-region")!);
    await advance(10000);
    expect(screen.getByText("Control media and navigate your PC.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Resume" }));
    await advance(6100);
    expect(screen.getByText("Turn speech into text on your PC.")).toBeTruthy();
    fireEvent.keyDown(document.activeElement!, { key: "Tab" });
    expect(screen.getByRole("button", { name: "Resume" })).toBeTruthy();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    await advance(10000);
    expect(screen.queryByRole("group", { name: "Introduction controls" })).toBeNull();
    expect(document.querySelector("[data-introduction-highlight]")).toBeNull();
  });
  it.each(["disconnect", "switch", "overlay", "menu", "hidden"])(
    "cancels outstanding playback on %s",
    async (reason) => {
      const view = render(<Harness />);
      await start();
      fireEvent.click(screen.getByRole("button", { name: "Next" }));
      await advance(30);
      if (reason === "disconnect") {
        view.rerender(<Harness connected={false} />);
      }
      if (reason === "switch") {
        view.rerender(<Harness connectionId="pc-b:2" />);
      }
      if (reason === "overlay") {
        view.rerender(<Harness busy />);
      }
      if (reason === "menu") {
        fireEvent.click(screen.getByText("Dismiss menu"));
      }
      if (reason === "hidden") {
        vi.spyOn(document, "hidden", "get").mockReturnValue(true);
        fireEvent(document, new Event("visibilitychange"));
      }
      await advance(10000);
      expect(screen.queryByRole("group", { name: "Introduction controls" })).toBeNull();
      expect(command).not.toHaveBeenCalled();
      expect(document.querySelector("[data-introduction-highlight]")).toBeNull();
    },
  );
  it("starts reduced motion paused and keeps progress through resize and rapid Back/Next", async () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: true })),
    );
    render(<Harness />);
    await start();
    await advance(10000);
    expect(screen.getByRole("button", { name: "Resume" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    fireEvent(window, new Event("resize"));
    await advance(30);
    expect(screen.getByText("Control media and navigate your PC.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Skip tour" }));
    expect(command).not.toHaveBeenCalled();
  });
});
