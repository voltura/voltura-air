import { afterEach, describe, expect, it, vi } from "vitest";
import { readLocalStorage, removeLocalStorage, writeLocalStorage } from "./browserStorage";

afterEach(() => vi.unstubAllGlobals());

describe("browserStorage", () => {
  it("retains a volatile write when reads work but persistent writes are quota-blocked", () => {
    vi.stubGlobal(
      "localStorage",
      storageWith({
        getItem: () => null,
        setItem: () => {
          throw new DOMException("Full", "QuotaExceededError");
        },
      }),
    );

    expect(writeLocalStorage("quota-write", "saved in memory")).toBe(false);

    expect(readLocalStorage("quota-write")).toBe("saved in memory");
  });

  it("retains a volatile removal when persistent removal is blocked", () => {
    vi.stubGlobal(
      "localStorage",
      storageWith({
        getItem: () => "stale",
        removeItem: () => {
          throw new DOMException("Blocked", "SecurityError");
        },
      }),
    );

    expect(removeLocalStorage("blocked-remove")).toBe(false);

    expect(readLocalStorage("blocked-remove")).toBeNull();
  });
});

function storageWith(overrides: Partial<Storage>): Storage {
  return {
    get length() {
      return 0;
    },
    clear: () => undefined,
    getItem: () => null,
    key: () => null,
    removeItem: () => undefined,
    setItem: () => undefined,
    ...overrides,
  } satisfies Storage;
}
