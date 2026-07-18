import { useState } from "react";
import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ConnectionState } from "./connectionTypes";
import { useManualReconnectFeedback } from "./useManualReconnectFeedback";

describe("useManualReconnectFeedback", () => {
  it("keeps reconnect progress visible while activating a disconnected saved PC", () => {
    const { result } = renderHook(() => {
      const [activePcId, setActivePcId] = useState<string | null>(null);
      const [state, setState] = useState<ConnectionState>("disconnected");
      const reconnectFeedback = useManualReconnectFeedback(activePcId, state, setActivePcId);
      return { activePcId, reconnectFeedback, setState };
    });

    act(() => { result.current.reconnectFeedback.reconnect("pc-a"); });

    expect(result.current.activePcId).toBe("pc-a");
    expect(result.current.reconnectFeedback.progress).toBe("reconnecting");

    act(() => { result.current.setState("connecting"); });
    expect(result.current.reconnectFeedback.progress).toBe("reconnecting");

    act(() => { result.current.setState("paired"); });
    expect(result.current.reconnectFeedback.progress).toBe("connected");
  });

  it("ends saved reconnect progress when the reconnect credential is rejected", () => {
    const { result } = renderHook(() => {
      const [activePcId, setActivePcId] = useState<string | null>(null);
      const [state, setState] = useState<ConnectionState>("disconnected");
      const reconnectFeedback = useManualReconnectFeedback(activePcId, state, setActivePcId);
      return { reconnectFeedback, setState };
    });

    act(() => { result.current.reconnectFeedback.reconnect("pc-a"); });
    act(() => { result.current.setState("connecting"); });
    act(() => { result.current.setState("needs-pairing"); });

    expect(result.current.reconnectFeedback.progress).toBeUndefined();
  });
});
