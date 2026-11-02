import { useEffect, useRef, useState, type Dispatch, type SetStateAction } from "react";
import { getVisibleModeNavigation, type IntroductionStep } from "./introductionSteps";
import { useIntroductionInteraction } from "./useIntroductionInteraction";

interface PlaybackOptions {
  tour: IntroductionStep[] | null;
  setTour: Dispatch<SetStateAction<IntroductionStep[] | null>>;
  index: number;
  setIndex: Dispatch<SetStateAction<number>>;
  paused: boolean;
  setPaused: Dispatch<SetStateAction<boolean>>;
  drawerOpen: boolean;
  setDrawerOpen: (open: boolean) => void;
  onFinish: () => void;
}
export function useIntroductionPlayback({
  tour,
  setTour,
  index,
  setIndex,
  paused,
  setPaused,
  drawerOpen,
  setDrawerOpen,
  onFinish,
}: PlaybackOptions) {
  const [positionedStep, setPositionedStep] = useState<IntroductionStep | null>(null);
  const finish = useRef(onFinish);
  useEffect(() => {
    finish.current = onFinish;
  });
  const step = tour?.[index];
  const drawerStage = Boolean(step?.targets[0]?.closest(".settings-drawer-scroll-region"));
  useEffect(() => {
    if (!step) {
      return;
    }
    setDrawerOpen(drawerStage);
    if (!drawerStage) {
      const scroll = document.querySelector(".settings-drawer-scroll-region");
      if (scroll) {
        scroll.scrollTop = 0;
      }
    }
  }, [step, drawerStage, setDrawerOpen]);

  useEffect(() => {
    if (!step || (drawerStage && !drawerOpen)) {
      return;
    }
    let targets = step.targets.filter((target) => target.isConnected);
    if (!targets.length) {
      const frame = requestAnimationFrame(() => {
        if (index >= (tour?.length ?? 1) - 1) {
          finish.current();
        } else {
          setIndex((value) => value + 1);
        }
      });
      return () => cancelAnimationFrame(frame);
    }
    const scroller = drawerStage
      ? document.querySelector<HTMLElement>(".settings-drawer-scroll-region")
      : null;
    const card = document.querySelector<HTMLElement>("[data-introduction-controls]");
    let expectedScrollTop = scroller?.scrollTop ?? 0;
    const position = () => {
      if (step.targets[0]?.dataset.introductionTarget === "navigation") {
        const navigation = getVisibleModeNavigation();
        if (!navigation) {
          finish.current();
          return;
        }
        targets.forEach((target) => target.removeAttribute("data-introduction-highlight"));
        targets = [navigation];
        navigation.setAttribute("data-introduction-highlight", "");
      }
      const first = targets[0];
      if (!first) {
        return;
      }
      const last = targets.at(-1) ?? first;
      if (scroller) {
        const bounds = scroller.getBoundingClientRect();
        const top = first.getBoundingClientRect().top;
        const bottom = last.getBoundingClientRect().bottom;
        if (targets.length > 1 && bottom - top > bounds.height - 12) {
          setTour(
            (current) =>
              current?.flatMap((entry) =>
                entry === step
                  ? entry.targets.map((target) => ({ targets: [target], copy: "" }))
                  : [entry],
              ) ?? null,
          );
          return;
        }
        scroller.scrollTop +=
          top < bounds.top + 6 ? top - bounds.top - 6 : Math.max(0, bottom - bounds.bottom + 6);
      }
      expectedScrollTop = scroller?.scrollTop ?? 0;
      const shellCard = card?.parentElement;
      const targetRect = first.getBoundingClientRect();
      if (!drawerStage && shellCard && targetRect.top > window.innerHeight / 2) {
        shellCard.style.bottom = `${Math.max(12, window.innerHeight - targetRect.top + 12)}px`;
      }
      setPositionedStep(step);
    };
    targets.forEach((target) => target.setAttribute("data-introduction-highlight", ""));
    const frame = requestAnimationFrame(position);
    const observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(position) : null;
    if (card) {
      observer?.observe(card);
    }
    if (scroller) {
      observer?.observe(scroller);
    }
    const pause = () => setPaused(true);
    const scroll = () => {
      if (scroller && Math.abs(scroller.scrollTop - expectedScrollTop) > 1) {
        pause();
      }
    };
    scroller?.addEventListener("scroll", scroll, { passive: true });
    scroller?.addEventListener("wheel", pause, { passive: true });
    scroller?.addEventListener("touchmove", pause, { passive: true });
    scroller?.addEventListener("pointerdown", pause, { passive: true });
    window.addEventListener("resize", position);
    window.visualViewport?.addEventListener("resize", position);
    return () => {
      cancelAnimationFrame(frame);
      observer?.disconnect();
      targets.forEach((target) => target.removeAttribute("data-introduction-highlight"));
      scroller?.removeEventListener("scroll", scroll);
      scroller?.removeEventListener("wheel", pause);
      scroller?.removeEventListener("touchmove", pause);
      scroller?.removeEventListener("pointerdown", pause);
      window.removeEventListener("resize", position);
      window.visualViewport?.removeEventListener("resize", position);
    };
  }, [step, drawerStage, drawerOpen, tour, index, setIndex, setPaused, setTour]);

  useEffect(() => {
    if (!tour || paused || positionedStep !== step || index >= tour.length - 1) {
      return;
    }
    const timer = setTimeout(() => setIndex((value) => value + 1), 6000);
    return () => clearTimeout(timer);
  }, [tour, paused, positionedStep, step, index, setIndex]);

  useIntroductionInteraction(
    tour !== null,
    drawerStage,
    drawerOpen,
    () => setPaused(true),
    onFinish,
  );
  return { step, drawerStage };
}
