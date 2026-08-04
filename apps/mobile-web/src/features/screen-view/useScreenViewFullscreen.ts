import { useEffect, useRef, useState } from "react";

export function useScreenViewFullscreen() {
  const [immersive, setImmersive] = useState(false);
  const workspaceRef = useRef<HTMLElement | null>(null);
  const nativeFullscreenRef = useRef(false);
  const nativeOrientationRef = useRef<"landscape" | "portrait" | null>(null);
  const fullscreenExitTimerRef = useRef<number | undefined>(undefined);
  const orientationSettleTimerRef = useRef<number | undefined>(undefined);

  async function enterImmersive() {
    const workspace = workspaceRef.current;
    if (!workspace || immersive) {return;}
    setImmersive(true);
    if (!workspace.requestFullscreen) {return;}
    try {
      await workspace.requestFullscreen({ navigationUI: "hide" });
      nativeFullscreenRef.current = document.fullscreenElement === workspace;
      nativeOrientationRef.current = nativeFullscreenRef.current ? currentOrientation() : null;
    } catch {
      nativeFullscreenRef.current = false;
    }
  }

  async function exitImmersive() {
    const workspace = workspaceRef.current;
    nativeFullscreenRef.current = false;
    nativeOrientationRef.current = null;
    window.clearTimeout(fullscreenExitTimerRef.current);
    fullscreenExitTimerRef.current = undefined;
    setImmersive(false);
    if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
      try {await document.exitFullscreen();} catch { /* The in-app fallback still exits. */ }
    }
  }

  useEffect(() => {
    const onFullscreenChange = () => {
      if (nativeFullscreenRef.current && document.fullscreenElement !== workspaceRef.current) {
        nativeFullscreenRef.current = false;
        window.clearTimeout(fullscreenExitTimerRef.current);
        fullscreenExitTimerRef.current = window.setTimeout(() => {
          fullscreenExitTimerRef.current = undefined;
          const exitedAcrossOrientation = nativeOrientationRef.current !== null &&
            nativeOrientationRef.current !== currentOrientation();
          nativeOrientationRef.current = null;
          if (!exitedAcrossOrientation) {setImmersive(false);}
        }, 200);
      }
    };
    const settleNativeOrientation = () => {
      window.clearTimeout(orientationSettleTimerRef.current);
      orientationSettleTimerRef.current = window.setTimeout(() => {
        orientationSettleTimerRef.current = undefined;
        if (nativeFullscreenRef.current && document.fullscreenElement === workspaceRef.current) {
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
  }, []);

  useEffect(() => () => {
    nativeFullscreenRef.current = false;
    nativeOrientationRef.current = null;
    const workspace = workspaceRef.current;
    if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
      void document.exitFullscreen().catch(() => undefined);
    }
  }, []);

  return { workspaceRef, immersive, enterImmersive, exitImmersive };
}

function currentOrientation(): "landscape" | "portrait" {
  return window.innerWidth > window.innerHeight ? "landscape" : "portrait";
}
