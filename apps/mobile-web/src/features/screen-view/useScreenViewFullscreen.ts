import { useEffect, useEffectEvent, useRef, useState } from "react";

export function useScreenViewFullscreen() {
  const [immersive, setImmersive] = useState(false);
  const workspaceRef = useRef<HTMLElement | null>(null);
  const nativeFullscreenRef = useRef(false);

  async function enterImmersive() {
    const workspace = workspaceRef.current;
    if (!workspace || immersive) {return;}
    setImmersive(true);
    if (!workspace.requestFullscreen) {return;}
    try {
      await workspace.requestFullscreen({ navigationUI: "hide" });
      nativeFullscreenRef.current = document.fullscreenElement === workspace;
    } catch {
      nativeFullscreenRef.current = false;
    }
  }

  async function exitImmersive() {
    const workspace = workspaceRef.current;
    nativeFullscreenRef.current = false;
    setImmersive(false);
    if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
      try {await document.exitFullscreen();} catch { /* The in-app fallback still exits. */ }
    }
  }

  const exitImmersiveForEvent = useEffectEvent(() => {void exitImmersive();});

  useEffect(() => {
    const onFullscreenChange = () => {
      if (nativeFullscreenRef.current && document.fullscreenElement !== workspaceRef.current) {
        nativeFullscreenRef.current = false;
        setImmersive(false);
      }
    };
    document.addEventListener("fullscreenchange", onFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", onFullscreenChange);
  }, []);

  useEffect(() => {
    if (!immersive) {return;}
    const startedInLandscape = window.innerWidth > window.innerHeight;
    const exitAfterResizeAcrossOrientation = () => {
      if ((window.innerWidth > window.innerHeight) !== startedInLandscape) {exitImmersiveForEvent();}
    };
    const exitAfterOrientationChange = () => exitImmersiveForEvent();
    const orientation = screen.orientation;
    window.addEventListener("resize", exitAfterResizeAcrossOrientation);
    window.addEventListener("orientationchange", exitAfterOrientationChange);
    orientation?.addEventListener("change", exitAfterOrientationChange);
    return () => {
      window.removeEventListener("resize", exitAfterResizeAcrossOrientation);
      window.removeEventListener("orientationchange", exitAfterOrientationChange);
      orientation?.removeEventListener("change", exitAfterOrientationChange);
    };
  }, [immersive]);

  useEffect(() => () => {
    nativeFullscreenRef.current = false;
    const workspace = workspaceRef.current;
    if (workspace && document.fullscreenElement === workspace && document.exitFullscreen) {
      void document.exitFullscreen().catch(() => undefined);
    }
  }, []);

  return { workspaceRef, immersive, enterImmersive, exitImmersive };
}
