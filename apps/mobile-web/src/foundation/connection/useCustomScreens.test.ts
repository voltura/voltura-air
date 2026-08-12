import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  incompatibleCustomScreenResponseCode,
  incompatibleCustomScreenResponseMessage,
  useCustomScreens
} from "./useCustomScreens";

describe("useCustomScreens", () => {
  it("completes a correlated rejected definition instead of leaving it pending", () => {
    const send = vi.fn();
    const { result } = renderHook(() =>
      useCustomScreens("paired", 4, "catalog.current", send));

    act(() => { result.current.requestCustomScreen("screen.gyro"); });
    const request = send.mock.calls[0]?.[0] as { operationId: string };

    act(() => {
      expect(result.current.rejectCustomScreenGet(request.operationId)).toBe(true);
    });

    expect(result.current.customScreenDefinition).toBeNull();
    expect(result.current.customScreenGetResult).toEqual({
      type: "custom.screen.get.result",
      operationId: request.operationId,
      succeeded: false,
      code: incompatibleCustomScreenResponseCode,
      message: incompatibleCustomScreenResponseMessage
    });
    expect(result.current.rejectCustomScreenGet("other-operation")).toBe(false);
  });
});
