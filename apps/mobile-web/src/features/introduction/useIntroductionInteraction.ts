import { useEffect, useRef } from "react";

export function useIntroductionInteraction(
  active: boolean,
  drawerStage: boolean,
  drawerOpen: boolean,
  pause: () => void,
  onFinish: () => void,
): void {
  const finish = useRef(onFinish);
  const drawerWasOpen = useRef(false);
  const pauseRef = useRef(pause);
  useEffect(() => {
    finish.current = onFinish;
    pauseRef.current = pause;
  });
  useEffect(() => {
    if (!active) {
      drawerWasOpen.current = false;
      return;
    }
    if (drawerStage && !drawerOpen) {
      if (drawerWasOpen.current) {
        finish.current();
      }
      return;
    }
    drawerWasOpen.current = drawerStage && drawerOpen;
    const hidden = () => {
      if (document.hidden) {
        finish.current();
      }
    };
    const keys = (event: KeyboardEvent) => {
      pauseRef.current();
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopImmediatePropagation();
        finish.current();
      }
      if (event.key === "Tab") {
        const controls = Array.from(
          document.querySelectorAll<HTMLButtonElement>(
            "[data-introduction-controls] button:not(:disabled)",
          ),
        );
        const current = controls.indexOf(document.activeElement as HTMLButtonElement);
        const next =
          controls[(current + (event.shiftKey ? -1 : 1) + controls.length) % controls.length];
        if (next) {
          event.preventDefault();
          next.focus();
        }
      }
    };
    const block = (event: Event) => {
      if (!(event.target instanceof Element)) {
        return;
      }
      if (
        event.target.closest(
          "[data-introduction-controls], .settings-drawer-light-dismiss, .drawer-header",
        )
      ) {
        return;
      }
      if (event.type !== "pointerdown" || !event.target.closest(".settings-drawer-scroll-region")) {
        event.preventDefault();
      }
      event.stopImmediatePropagation();
    };
    const focus = (event: FocusEvent) => {
      if (
        event.target instanceof Element &&
        !event.target.closest("[data-introduction-controls]")
      ) {
        document
          .querySelector<HTMLButtonElement>("[data-guide-next]")
          ?.focus({ preventScroll: true });
      }
    };
    const observer = new MutationObserver(() => {
      const otherDialog = document.querySelector(
        "dialog[open]:not(.settings-drawer), [role=dialog]",
      );
      if (otherDialog || (drawerStage && !document.querySelector(".settings-drawer[open]"))) {
        finish.current();
      }
    });
    observer.observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ["open"],
    });
    document.addEventListener("visibilitychange", hidden);
    document.addEventListener("keydown", keys, true);
    document.addEventListener("click", block, true);
    document.addEventListener("pointerdown", block, true);
    document.addEventListener("focusin", focus);
    return () => {
      observer.disconnect();
      document.removeEventListener("visibilitychange", hidden);
      document.removeEventListener("keydown", keys, true);
      document.removeEventListener("click", block, true);
      document.removeEventListener("pointerdown", block, true);
      document.removeEventListener("focusin", focus);
    };
  }, [active, drawerStage, drawerOpen]);
}
