import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getAutoRefreshSessionKey } from "../settings/appStorage";
import { refreshWithFreshAppUrl, type FreshAppRefreshResult } from "./freshAppRefresh";
import { shouldRefreshWebClient, usePwaLifecycle } from "./usePwaLifecycle";

vi.mock("./freshAppRefresh", () => ({
  refreshWithFreshAppUrl: vi.fn()
}));

const activePc = { customName: false, id: "https://pc.local", name: "PC", url: "https://pc.local" };
const successfulRefresh: FreshAppRefreshResult = { navigationStarted: true, navigationMethod: "replace", warnings: [] };
const failedRefresh: FreshAppRefreshResult = { navigationStarted: false, navigationMethod: null, warnings: ["failed"] };

function createOptions(hostStatus: { webClientBuildId?: string } | null = { webClientBuildId: "build-b" }) {
  return {
    activePc,
    autoRefresh: true,
    clientId: "client-a",
    hostStatus,
    state: "paired" as const
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

beforeEach(() => {
  vi.stubGlobal("__WEB_BUILD_ID__", "build-a");
  vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
  vi.mocked(refreshWithFreshAppUrl).mockReset();
  sessionStorage.clear();
});

describe("PWA web build refresh", () => {
  it("refreshes only when the host serves a different compiled web build", () => {
    expect(shouldRefreshWebClient(undefined)).toBe(false);
    expect(shouldRefreshWebClient("build-a")).toBe(false);
    expect(shouldRefreshWebClient("build-b")).toBe(true);
  });

  it("commits the automatic guard at navigation and suppresses a later duplicate", async () => {
    const refreshKey = getAutoRefreshSessionKey("client-a", activePc.id, "build-b");
    vi.mocked(refreshWithFreshAppUrl).mockImplementation((_environment, beforeNavigate) => {
      expect(sessionStorage.getItem(refreshKey)).toBeNull();
      beforeNavigate?.();
      return Promise.resolve(successfulRefresh);
    });
    const { rerender } = renderHook(({ options }) => usePwaLifecycle(options), {
      initialProps: { options: createOptions() }
    });

    await waitFor(() => { expect(sessionStorage.getItem(refreshKey)).toBe("true"); });
    rerender({ options: createOptions({ webClientBuildId: "build-b" }) });

    expect(refreshWithFreshAppUrl).toHaveBeenCalledOnce();
  });

  it("deduplicates concurrent automatic messages before navigation starts", async () => {
    const pending = deferred<FreshAppRefreshResult>();
    let beforeNavigate: (() => void) | undefined;
    vi.mocked(refreshWithFreshAppUrl).mockImplementation((_environment, before) => {
      beforeNavigate = before;
      return pending.promise;
    });
    const { rerender } = renderHook(({ options }) => usePwaLifecycle(options), {
      initialProps: { options: createOptions() }
    });
    await waitFor(() => { expect(refreshWithFreshAppUrl).toHaveBeenCalledOnce(); });

    rerender({ options: createOptions({ webClientBuildId: "build-b" }) });
    expect(refreshWithFreshAppUrl).toHaveBeenCalledOnce();
    act(() => {
      beforeNavigate?.();
      pending.resolve(successfulRefresh);
    });
    await pending.promise;
  });

  it("removes a failed guard and retries on a later status event for the same build", async () => {
    vi.mocked(refreshWithFreshAppUrl)
      .mockImplementationOnce((_environment, beforeNavigate) => {
        beforeNavigate?.();
        return Promise.resolve(failedRefresh);
      })
      .mockImplementationOnce((_environment, beforeNavigate) => {
        beforeNavigate?.();
        return Promise.resolve(successfulRefresh);
      });
    const refreshKey = getAutoRefreshSessionKey("client-a", activePc.id, "build-b");
    const { result, rerender } = renderHook(({ options }) => usePwaLifecycle(options), {
      initialProps: { options: createOptions() }
    });

    await waitFor(() => { expect(result.current.refreshMessage).toContain("Could not refresh"); });
    expect(sessionStorage.getItem(refreshKey)).toBeNull();
    rerender({ options: createOptions({ webClientBuildId: "build-b" }) });

    await waitFor(() => { expect(refreshWithFreshAppUrl).toHaveBeenCalledTimes(2); });
    expect(sessionStorage.getItem(refreshKey)).toBe("true");
  });

  it("handles a rejected manual refresh and permits another attempt", async () => {
    vi.mocked(refreshWithFreshAppUrl).mockRejectedValueOnce(new Error("boundary"));
    const { result } = renderHook(() => usePwaLifecycle(createOptions(null)));

    await expect(act(async () => result.current.refreshInstalledApp())).resolves.toBeUndefined();
    expect(result.current.refreshMessage).toContain("Could not refresh");
    vi.mocked(refreshWithFreshAppUrl).mockResolvedValueOnce(successfulRefresh);
    await act(async () => result.current.refreshInstalledApp());

    expect(refreshWithFreshAppUrl).toHaveBeenCalledTimes(2);
  });

  it("shares one active refresh between automatic and manual callers", async () => {
    const pending = deferred<FreshAppRefreshResult>();
    vi.mocked(refreshWithFreshAppUrl).mockReturnValue(pending.promise);
    const { result } = renderHook(() => usePwaLifecycle(createOptions()));
    await waitFor(() => { expect(refreshWithFreshAppUrl).toHaveBeenCalledOnce(); });

    let manualRefresh!: Promise<void>;
    act(() => { manualRefresh = result.current.refreshInstalledApp(); });
    expect(refreshWithFreshAppUrl).toHaveBeenCalledOnce();
    act(() => { pending.resolve(successfulRefresh); });
    await manualRefresh;
  });
});
