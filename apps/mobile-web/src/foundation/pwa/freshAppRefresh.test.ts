import { describe, expect, it, vi } from "vitest";
import { refreshWithFreshAppUrl, type FreshAppRefreshEnvironment } from "./freshAppRefresh";

function createEnvironment(overrides: Partial<FreshAppRefreshEnvironment> = {}): FreshAppRefreshEnvironment {
  return {
    createFreshUrl: vi.fn(() => "https://phone.local/?refresh=1"),
    reload: vi.fn(),
    replace: vi.fn(),
    ...overrides
  };
}

describe("refreshWithFreshAppUrl", () => {
  it.each([
    ["service-worker lookup", { getRegistrations: vi.fn(() => Promise.reject(new Error("lookup"))) }],
    ["service-worker unregister", { getRegistrations: vi.fn(() => Promise.resolve([{ unregister: () => { throw new Error("unregister"); } }])) }],
    ["cache lookup", { getCacheNames: vi.fn(() => Promise.reject(new Error("keys"))), deleteCache: vi.fn() }],
    ["cache deletion", { getCacheNames: vi.fn(() => Promise.resolve(["cache-a"])), deleteCache: () => { throw new Error("delete"); } }]
  ])("continues to navigation after a %s failure", async (_name, overrides) => {
    const environment = createEnvironment(overrides);

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationStarted).toBe(true);
    expect(result.navigationMethod).toBe("replace");
    expect(result.warnings).toHaveLength(1);
    expect(environment.replace).toHaveBeenCalledExactlyOnceWith("https://phone.local/?refresh=1");
  });

  it("falls back to reload when fresh URL creation fails", async () => {
    const environment = createEnvironment({ createFreshUrl: () => { throw new Error("URL"); } });

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationMethod).toBe("reload");
    expect(environment.replace).not.toHaveBeenCalled();
    expect(environment.reload).toHaveBeenCalledOnce();
  });

  it("falls back to reload when replacement throws", async () => {
    const environment = createEnvironment({ replace: () => { throw new Error("replace"); } });

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationMethod).toBe("reload");
    expect(environment.reload).toHaveBeenCalledOnce();
  });

  it("returns a handled failure when neither navigation method can start", async () => {
    const environment = createEnvironment({
      reload: () => { throw new Error("reload"); },
      replace: () => { throw new Error("replace"); }
    });

    await expect(refreshWithFreshAppUrl(environment)).resolves.toMatchObject({
      navigationStarted: false,
      navigationMethod: null
    });
  });

  it("commits the caller's guard immediately before navigation", async () => {
    const order: string[] = [];
    const environment = createEnvironment({ replace: () => { order.push("navigate"); } });

    await refreshWithFreshAppUrl(environment, () => { order.push("guard"); });

    expect(order).toEqual(["guard", "navigate"]);
  });
});
