import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useUrlOpen } from "./useUrlOpen";

describe("useUrlOpen", () => {
  afterEach(() => vi.useRealTimers());

  it("sends the reviewed draft and completes only from its matching result", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useUrlOpen("paired", send));

    let operationId: string | null = null;
    act(() => {
      operationId = result.current.requestUrlOpen(" example.com/path ");
    });

    expect(operationId).not.toBeNull();
    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "url.open",
      operationId,
      url: " example.com/path "
    });

    act(() => result.current.completeUrlOpen({
      type: "url.open.result",
      operationId: "unrelated",
      succeeded: false,
      message: "Unrelated"
    }));
    expect(result.current.pendingUrlOpen).toBe(true);

    act(() => result.current.completeUrlOpen({
      type: "url.open.result",
      operationId: operationId!,
      succeeded: true,
      code: "accepted",
      message: "Open request sent.",
      normalizedUrl: "https://example.com/path"
    }));
    expect(result.current.pendingUrlOpen).toBe(false);
    expect(result.current.urlOpenResult?.message).toBe("Open request sent.");
  });

  it("returns retryable timeout feedback", () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useUrlOpen("paired", vi.fn()));

    act(() => result.current.requestUrlOpen("example.com"));
    act(() => vi.advanceTimersByTime(8000));

    expect(result.current.pendingUrlOpen).toBe(false);
    expect(result.current.urlOpenResult?.code).toBe("VAIR-URL-RESPONSE-TIMEOUT");
  });
});
