import { useCallback, useEffect, useRef, useState } from "react";

type ScreenOrientationSnapshot = OrientationType | "landscape" | "portrait";

export function useScreenViewFullscreen() {
  const [immersive, setImmersive] = useState(false);
  const workspaceRef = useRef<HTMLElement | null>(null);
  const ownedWorkspaceRef = useRef<HTMLElement | null>(null);
  const immersiveRef = useRef(false);
  const nativeFullscreenRef = useRef(false);
  const nativeOrientationRef = useRef<ScreenOrientationSnapshot | null>(null);
  const fullscreenExitTimerRef = useRef<number | undefined>(undefined);
  const orientationSettleTimerRef = useRef<number | undefined>(undefined);

  const recordNativeEntry = useCallback(() => {
    window.clearTimeout(fullscreenExitTimerRef.current);
    fullscreenExitTimerRef.current = undefined;
    if (!nativeFullscreenRef.current) {
      nativeFullscreenRef.current = true;
      nativeOrientationRef.current = currentOrientation();
    }
  }, []);

  async function enterImmersive() {
    const workspace = workspaceRef.current;
    if (!workspace || immersiveRef.current) {
      return;
    }
    ownedWorkspaceRef.current = workspace;
    immersiveRef.current = true;
    setImmersive(true);
    if (!workspace.requestFullscreen) {
      return;
    }
    try {
      await workspace.requestFullscreen({ navigationUI: "hide" });
      if (document.fullscreenElement !== workspace) {
        return;
      }
      if (immersiveRef.current && ownedWorkspaceRef.current === workspace) {
        recordNativeEntry();
      } else {
        await document.exitFullscreen?.();
      }
    } catch {
      /* The in-app fullscreen view remains available. */
    }
  }

  async function exitImmersive() {
    const workspace = ownedWorkspaceRef.current;
    ownedWorkspaceRef.current = null;
    immersiveRef.current = false;
    nativeFullscreenRef.current = false;
    nativeOrientationRef.current = null;
    window.clearTimeout(fullscreenExitTimerRef.current);
    window.clearTimeout(orientationSettleTimerRef.current);
    fullscreenExitTimerRef.current = undefined;
    orientationSettleTimerRef.current = undefined;
    setImmersive(false);
    if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
      try {
        await document.exitFullscreen();
      } catch {
        /* The in-app fallback still exits. */
      }
    }
  }

  useEffect(() => {
    const onFullscreenChange = () => {
      const workspace = ownedWorkspaceRef.current;
      if (!workspace || !immersiveRef.current) {
        return;
      }
      if (document.fullscreenElement === workspace) {
        recordNativeEntry();
      } else if (nativeFullscreenRef.current) {
        nativeFullscreenRef.current = false;
        window.clearTimeout(fullscreenExitTimerRef.current);
        fullscreenExitTimerRef.current = window.setTimeout(() => {
          fullscreenExitTimerRef.current = undefined;
          const exitedAcrossOrientation =
            nativeOrientationRef.current !== null &&
            nativeOrientationRef.current !== currentOrientation();
          nativeOrientationRef.current = null;
          if (!exitedAcrossOrientation) {
            ownedWorkspaceRef.current = null;
            immersiveRef.current = false;
            setImmersive(false);
          }
        }, 200);
      }
    };
    const settleNativeOrientation = () => {
      window.clearTimeout(orientationSettleTimerRef.current);
      orientationSettleTimerRef.current = window.setTimeout(() => {
        orientationSettleTimerRef.current = undefined;
        if (
          nativeFullscreenRef.current &&
          document.fullscreenElement === ownedWorkspaceRef.current
        ) {
          nativeOrientationRef.current = currentOrientation();
        }
      }, 250);
    };
    document.addEventListener("fullscreenchange", onFullscreenChange);
    window.addEventListener("orientationchange", settleNativeOrientation);
    screen.orientation?.addEventListener("change", settleNativeOrientation);
    return () => {
      document.removeEventListener("fullscreenchange", onFullscreenChange);
      window.removeEventListener("orientationchange", settleNativeOrientation);
      screen.orientation?.removeEventListener("change", settleNativeOrientation);
      window.clearTimeout(fullscreenExitTimerRef.current);
      window.clearTimeout(orientationSettleTimerRef.current);
    };
  }, [recordNativeEntry]);

  useEffect(
    () => () => {
      immersiveRef.current = false;
      nativeFullscreenRef.current = false;
      nativeOrientationRef.current = null;
      const workspace = ownedWorkspaceRef.current;
      ownedWorkspaceRef.current = null;
      if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
        void document.exitFullscreen().catch(() => undefined);
      }
    },
    [],
  );

  return { workspaceRef, immersive, enterImmersive, exitImmersive };
}

function currentOrientation(): ScreenOrientationSnapshot {
  return screen.orientation?.type ??
    (window.innerWidth > window.innerHeight ? "landscape" : "portrait");
}
