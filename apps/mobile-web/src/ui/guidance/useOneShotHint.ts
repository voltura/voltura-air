import { useCallback, useEffect, useState } from "react";

interface UseOneShotHintOptions {
  autoHideMs?: number | undefined;
}

export function useOneShotHint({ autoHideMs }: UseOneShotHintOptions = {}) {
  const [state, setState] = useState<0 | 1 | 2>(0);
  const open = state === 1;

  const dismiss = useCallback(() => {
    setState((current) => current === 1 ? 2 : current);
  }, []);

  const showOnce = useCallback(() => {
    setState((current) => current === 0 ? 1 : current);
  }, []);

  useEffect(() => {
    if (!open || autoHideMs === undefined) {
      return;
    }

    const timeout = window.setTimeout(() => { setState(2); }, autoHideMs);
    return () => { window.clearTimeout(timeout); };
  }, [autoHideMs, open]);

  return {
    dismiss,
    hasShown: state !== 0,
    open,
    showOnce
  };
}
