import { useEffect } from "react";

export function supportsScreenWakeLock(): boolean {
  return typeof navigator.wakeLock?.request === "function";
}

// Each consumer owns its sentinel, so ending Gyro cannot release the app's lock.
export function useScreenWakeLock(enabled: boolean): void {
  useEffect(() => {
    if (!enabled || !supportsScreenWakeLock()) {
      return;
    }
    let disposed = false;
    let generation = 0;
    let visible = false;
    let sentinel: WakeLockSentinel | null = null;

    const release = () => {
      generation += 1;
      const owned = sentinel;
      sentinel = null;
      if (owned) {
        void owned.release().catch(() => {
          // The browser may already have released the lock.
        });
      }
    };
    const acquire = async () => {
      const attempt = ++generation;
      try {
        const lock = await navigator.wakeLock.request("screen");
        if (disposed || attempt !== generation || document.visibilityState !== "visible") {
          await lock.release();
        } else {
          sentinel = lock;
        }
      } catch {
        // Best effort: retry only after another visible session or explicit enable.
      }
    };
    const updateVisibility = () => {
      const nextVisible = document.visibilityState === "visible";
      if (visible === nextVisible) {
        return;
      }
      visible = nextVisible;
      if (visible) {
        void acquire();
      } else {
        release();
      }
    };
    const onPageHide = () => {
      visible = false;
      release();
    };
    updateVisibility();
    document.addEventListener("visibilitychange", updateVisibility);
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener("pageshow", updateVisibility);
    return () => {
      disposed = true;
      document.removeEventListener("visibilitychange", updateVisibility);
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener("pageshow", updateVisibility);
      release();
    };
  }, [enabled]);
}
