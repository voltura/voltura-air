// @vitest-environment-options {"url":"https://voltura.se/"}

import { renderHook } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { useVolturaAirConnection } from "./useVolturaAirConnection";

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

it("gives name confirmation the hosted pairing after removing its fragment on a fresh app URL", () => {
  const route = "r".repeat(22);
  const pairToken = "a".repeat(32);
  const socket = vi.fn();
  vi.stubGlobal("WebSocket", socket);
  window.history.replaceState(
    null,
    "",
    `/air/app/?m=s&r=${route}&v=1.5.7&refresh=123#${pairToken}`,
  );

  const { result } = renderHook(() => useVolturaAirConnection());

  expect(result.current.initialPairing).toEqual({
    pairToken,
    pcUrl: `https://voltura.se/s/${route}`,
  });
  expect(window.location.hash).toBe("");
  expect(result.current.pairedPcs).toEqual([]);
  expect(socket).not.toHaveBeenCalled();
});
