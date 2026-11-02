import "./introduction.css";
import { useIntroductionPlayback } from "./useIntroductionPlayback";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ConfirmationDialog } from "../../ui/overlays/ConfirmationDialog";
import { SpotlightCard } from "../../ui/guidance/SpotlightCard";
import {
  collectDrawerSteps,
  getIntroductionCopy,
  getVisibleModeNavigation,
  getStepCopy,
  type IntroductionCapabilities,
  type IntroductionStep,
} from "./introductionSteps";
import { saveIntroduction } from "./introductionState";

export interface IntroductionProps {
  capabilities: IntroductionCapabilities;
  eligible: boolean;
  connected: boolean;
  connectionId: string;
  busy: boolean;
  drawerOpen: boolean;
  setDrawerOpen: (open: boolean) => void;
  replay: number;
}

export function Introduction(props: IntroductionProps) {
  const [suppressed, setSuppressed] = useState(!props.eligible);
  const [welcome, setWelcome] = useState(false);
  const [tour, setTour] = useState<IntroductionStep[] | null>(null);
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const callbacks = useRef(props);
  const returnFocus = useRef<HTMLElement | null>(null);
  const replaySeen = useRef(0);
  useEffect(() => {
    callbacks.current = props;
  });

  const suppress = () => {
    setSuppressed(true);
    saveIntroduction("suppressed");
  };
  const start = () => {
    suppress();
    setWelcome(false);
    returnFocus.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const menu = document.querySelector<HTMLElement>('[data-introduction-target="menu"]');
    const drawer = document.querySelector<HTMLElement>(".settings-drawer-scroll-region");
    const navigation = getVisibleModeNavigation();
    const steps = [
      ...(menu ? [{ targets: [menu], copy: "Find all your tools here." }] : []),
      ...(drawer ? collectDrawerSteps(drawer, getIntroductionCopy(props.capabilities)) : []),
      ...(navigation
        ? [{ targets: [navigation], copy: "Switch controls here whenever you need them." }]
        : []),
    ];
    callbacks.current.setDrawerOpen(false);
    setIndex(0);
    setPaused(window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false);
    setTour(steps.length ? steps : null);
  };
  const finish = () => {
    if (!tour && !welcome) {
      return;
    }
    setTour(null);
    setWelcome(false);
    callbacks.current.setDrawerOpen(false);
    const focus = returnFocus.current;
    // Drawer close also restores focus; defer ours until that close has completed.
    queueMicrotask(() => {
      if (focus?.isConnected && !focus.closest("dialog:not([open])")) {
        focus.focus({ preventScroll: true });
      } else {
        document
          .querySelector<HTMLElement>('[data-introduction-target="menu"]')
          ?.focus({ preventScroll: true });
      }
    });
  };
  const actions = useRef({ start, finish });
  useEffect(() => {
    actions.current = { start, finish };
  });

  useEffect(() => {
    if (props.replay !== replaySeen.current && props.connected) {
      replaySeen.current = props.replay;
      actions.current.start();
    }
  }, [props.replay, props.connected]);

  useEffect(() => {
    if (suppressed || !props.connected || props.busy || props.drawerOpen || tour) {
      return;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    let down = false;
    const schedule = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        const active = document.activeElement;
        if (
          !down &&
          document.visibilityState !== "hidden" &&
          !document.querySelector(":active:not(html):not(body)") &&
          !document.querySelector("dialog[open], [role=dialog], [data-introduction-controls]") &&
          !(
            active instanceof HTMLElement && active.matches("input,textarea,[contenteditable=true]")
          )
        ) {
          setWelcome(true);
        }
      }, 800);
    };
    const pointerDown = () => {
      down = true;
      clearTimeout(timer);
    };
    const pointerUp = () => {
      down = false;
      schedule();
    };
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ["open"],
    });
    document.addEventListener("pointerdown", pointerDown, true);
    document.addEventListener("pointerup", pointerUp, true);
    document.addEventListener("pointercancel", pointerUp, true);
    document.addEventListener("keydown", schedule, true);
    document.addEventListener("visibilitychange", schedule);
    schedule();
    return () => {
      clearTimeout(timer);
      observer.disconnect();
      document.removeEventListener("pointerdown", pointerDown, true);
      document.removeEventListener("pointerup", pointerUp, true);
      document.removeEventListener("pointercancel", pointerUp, true);
      document.removeEventListener("keydown", schedule, true);
      document.removeEventListener("visibilitychange", schedule);
    };
  }, [suppressed, props.connected, props.busy, props.drawerOpen, tour]);

  useEffect(() => {
    if (!props.connected || props.busy) {
      actions.current.finish();
    }
    return () => {
      actions.current.finish();
    };
  }, [props.connected, props.connectionId, props.busy]);

  const { step, drawerStage } = useIntroductionPlayback({
    tour,
    setTour,
    index,
    setIndex,
    paused,
    setPaused,
    drawerOpen: props.drawerOpen,
    setDrawerOpen: props.setDrawerOpen,
    onFinish: finish,
  });

  const slot = drawerStage ? document.querySelector("[data-introduction-slot]") : null;
  return (
    <>
      <ConfirmationDialog
        title="Welcome to Voltura Air"
        description="Connected! Discover what you can do."
        isOpen={welcome && props.connected && !props.busy}
        confirmLabel="Show me"
        cancelLabel="Skip"
        destructive={false}
        onConfirm={start}
        onCancel={() => {
          suppress();
          setWelcome(false);
        }}
      />
      {step &&
        tour &&
        createPortal(
          <div className={drawerStage ? undefined : "introduction-shell-card"}>
            <SpotlightCard
              copy={getStepCopy(step, props.capabilities)}
              index={index}
              count={tour.length}
              paused={paused}
              onBack={() => {
                setIndex((value) => Math.max(0, value - 1));
              }}
              onNext={() => {
                if (index === tour.length - 1) {
                  finish();
                } else {
                  setIndex((value) => Math.min(tour.length - 1, value + 1));
                }
              }}
              onPause={() => setPaused((value) => !value)}
              onSkip={finish}
            />
          </div>,
          slot ?? document.body,
        )}
    </>
  );
}
