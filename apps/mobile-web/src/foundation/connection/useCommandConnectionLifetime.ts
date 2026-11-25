import { useEffectEvent, useLayoutEffect, useRef } from "react";
import type { ConnectionState } from "./connectionTypes";

export function useCommandConnectionLifetime(
  state: ConnectionState,
  connectionEpoch: number,
  retire: () => void,
): void {
  const lastEpochRef = useRef(connectionEpoch);
  const retireCommands = useEffectEvent(retire);

  useLayoutEffect(() => {
    const previousEpoch = lastEpochRef.current;
    lastEpochRef.current = connectionEpoch;
    if (state !== "paired" || previousEpoch !== connectionEpoch) {
      retireCommands();
    }
  }, [connectionEpoch, state]);
}
