import { beforeEach, describe, expect, it, vi } from "vitest";
import { shouldRefreshWebClient } from "./usePwaLifecycle";

beforeEach(() => {
  vi.stubGlobal("__WEB_BUILD_ID__", "build-a");
});

describe("PWA web build refresh", () => {
  it("refreshes only when the host serves a different compiled web build", () => {
    expect(shouldRefreshWebClient(undefined)).toBe(false);
    expect(shouldRefreshWebClient("build-a")).toBe(false);
    expect(shouldRefreshWebClient("build-b")).toBe(true);
  });
});
