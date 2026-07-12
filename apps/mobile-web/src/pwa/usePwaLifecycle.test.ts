import { describe, expect, it } from "vitest";
import { shouldRefreshWebClient } from "./usePwaLifecycle";

describe("PWA web build refresh", () => {
  it("refreshes only when the host serves a different compiled web build", () => {
    expect(shouldRefreshWebClient(undefined)).toBe(false);
    expect(shouldRefreshWebClient(__WEB_BUILD_ID__)).toBe(false);
    expect(shouldRefreshWebClient(`${__WEB_BUILD_ID__}-new`)).toBe(true);
  });
});
