import {
  AppWindow,
  ArrowLeft,
  LoaderCircle,
  Maximize2,
  MousePointer2,
  Plus,
  RefreshCw,
  X,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { subscribeAppsResults } from "../../foundation/connection/appsResultBus";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type {
  AppLaunchActionSummary,
  AppLaunchResultMessage,
  AppsCapability,
  AppsListResultMessage,
  AppsWindowSummary,
  ClientMessage,
} from "../../foundation/protocol/messages";
import { AppsWindowCard } from "./AppsWindowCard";
import { useAppsPreviews } from "./useAppsPreviews";
import "./apps.css";

interface Props {
  activePc: PcProfile;
  appLaunchActions: AppLaunchActionSummary[];
  appLaunchResult: AppLaunchResultMessage | null;
  capability: AppsCapability;
  clientId: string;
  onAppLaunch: (actionId: string) => void;
  onBack: () => void;
  onFeedback: (message: string, tone: "success" | "error" | "pending") => void;
  onOpenTrackpad: () => void;
  pendingAppLaunchId: string | null;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
  supportsRemoteLaunch: boolean;
}

const localId = () => crypto.randomUUID().replaceAll("_", "-");
const initialPreviewWaitMs = 2_500;

function findNearestCarouselCard(carousel: HTMLElement) {
  const center = carousel.scrollLeft + carousel.clientWidth / 2;
  let nearestCard: HTMLElement | null = null;
  let nearestDistance = Number.POSITIVE_INFINITY;
  for (const card of carousel.querySelectorAll<HTMLElement>("[data-app-index]")) {
    const distance = Math.abs(card.offsetLeft + card.clientWidth / 2 - center);
    if (distance < nearestDistance) {
      nearestDistance = distance;
      nearestCard = card;
    }
  }
  return nearestCard;
}

function updateCarouselCardFocus(carousel: HTMLElement) {
  const center = carousel.scrollLeft + carousel.clientWidth / 2;
  for (const card of carousel.querySelectorAll<HTMLElement>("[data-app-index]")) {
    const distance = Math.abs(card.offsetLeft + card.clientWidth / 2 - center);
    const focusDistance = Math.max(card.clientWidth * 1.8, 1);
    const focus = Math.max(0, 1 - distance / focusDistance);
    card.style.setProperty("--apps-card-focus", focus.toFixed(3));
  }
}

function normalizeCarouselLoop(carousel: HTMLElement, nearestCard: HTMLElement | null) {
  if (nearestCard?.dataset.appLoopClone !== "true") {
    return nearestCard;
  }
  const logicalIndex = nearestCard.dataset.appIndex;
  const canonicalCard = carousel.querySelector<HTMLElement>(
    `[data-app-index="${logicalIndex}"][data-app-canonical="true"]`,
  );
  if (!canonicalCard) {
    return nearestCard;
  }
  carousel.scrollLeft += canonicalCard.offsetLeft - nearestCard.offsetLeft;
  return canonicalCard;
}

function preserveWindowOrder(
  previousWindows: AppsWindowSummary[],
  nextWindows: AppsWindowSummary[],
) {
  const nextById = new Map(nextWindows.map((window) => [window.windowId, window]));
  const orderedWindows = previousWindows.flatMap((window) => {
    const nextWindow = nextById.get(window.windowId);
    if (!nextWindow) {
      return [];
    }
    nextById.delete(window.windowId);
    return [nextWindow];
  });
  for (const window of nextWindows) {
    if (nextById.delete(window.windowId)) {
      orderedWindows.push(window);
    }
  }
  return orderedWindows;
}

export function AppsWorkspace({
  activePc,
  appLaunchActions,
  appLaunchResult,
  capability,
  clientId,
  onAppLaunch,
  onBack,
  onFeedback,
  onOpenTrackpad,
  pendingAppLaunchId,
  send,
  state,
  supportsRemoteLaunch,
}: Props) {
  const [windows, setWindows] = useState<AppsWindowSummary[]>([]);
  const [revision, setRevision] = useState<string | null>(null);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [hasCompletedInitialList, setHasCompletedInitialList] = useState(false);
  const [hasPresentedInitialDeck, setHasPresentedInitialDeck] = useState(false);
  const [initialPreviewWaitExpired, setInitialPreviewWaitExpired] = useState(false);
  const [closing, setClosing] = useState(false);
  const [launcherExpanded, setLauncherExpanded] = useState(false);
  const [pendingWindowId, setPendingWindowId] = useState<string | null>(null);
  const [message, setMessage] = useState("Choose an open application on your PC.");
  const carouselRef = useRef<HTMLDivElement | null>(null);
  const selectionFrameRef = useRef<number | null>(null);
  const resizeFrameRef = useRef<number | null>(null);
  const resizeReleaseFrameRef = useRef<number | null>(null);
  const scrollSettleTimeoutRef = useRef<number | null>(null);
  const scrollSelectionSuppressedRef = useRef(false);
  const selectedIndexRef = useRef(selectedIndex);
  const hasCompletedInitialListRef = useRef(false);
  const listOperationRef = useRef<string | null>(null);
  const actionOperationRef = useRef<{
    operationId: string;
    type: "activate" | "close";
    target?: AppsWindowSummary;
  } | null>(null);
  const launchedActionRef = useRef<string | null>(null);
  const isListOperationPending = useCallback(
    (operationId: string) => listOperationRef.current === operationId,
    [],
  );
  const {
    acceptPreviewOffer,
    clearPreviewUrls,
    closePreview,
    ownsPreview,
    previewStates,
    previewUrls,
    reconcilePreviewUrls,
    refreshPreview,
  } = useAppsPreviews({
    activePc,
    capability,
    clientId,
    isListOperationPending,
    revision,
    selectedIndex,
    send,
    setMessage,
    state,
    windows,
  });
  const initialSelectedWindow = hasCompletedInitialList ? windows[selectedIndex] : undefined;
  const initialPreviewPending = Boolean(
    revision &&
    capability.previewAvailable &&
    initialSelectedWindow?.previewSupported &&
    !previewUrls.has(initialSelectedWindow.windowId) &&
    previewStates.get(initialSelectedWindow.windowId) !== "unavailable",
  );
  const unavailableMessage =
    state !== "paired"
      ? "Reconnect to the PC to use Apps."
      : !capability.enabled
        ? "Apps is unavailable on this PC."
        : !capability.permissionGranted || !capability.canUse
          ? "Control open applications is blocked for this device."
          : null;
  const initialPresentationReady =
    unavailableMessage === null &&
    hasCompletedInitialList &&
    (!initialPreviewPending || initialPreviewWaitExpired);
  const showInitialLoading = !hasPresentedInitialDeck && !initialPresentationReady;
  const showLoadingCard =
    unavailableMessage === null && (showInitialLoading || closing || refreshing);
  const carouselVisible = unavailableMessage === null && !showLoadingCard && !launcherExpanded;

  useEffect(() => {
    if (!initialPresentationReady || hasPresentedInitialDeck) {
      return;
    }
    const frame = requestAnimationFrame(() => setHasPresentedInitialDeck(true));
    return () => cancelAnimationFrame(frame);
  }, [hasPresentedInitialDeck, initialPresentationReady]);

  useEffect(() => {
    if (hasPresentedInitialDeck || !initialPreviewPending || initialPreviewWaitExpired) {
      return;
    }
    const timeout = window.setTimeout(
      () => setInitialPreviewWaitExpired(true),
      initialPreviewWaitMs,
    );
    return () => window.clearTimeout(timeout);
  }, [hasPresentedInitialDeck, initialPreviewPending, initialPreviewWaitExpired]);

  const requestList = useCallback(
    (quiet = false) => {
      if (state !== "paired" || !capability.canUse) {
        listOperationRef.current = null;
        actionOperationRef.current = null;
        setLoading(false);
        setRefreshing(false);
        setPendingWindowId(null);
        setClosing(false);
        return;
      }
      if (listOperationRef.current) {
        return;
      }
      const operationId = localId();
      const isManualRefresh = !quiet && hasCompletedInitialListRef.current;
      listOperationRef.current = operationId;
      setLoading(true);
      setRefreshing(isManualRefresh);
      if (!quiet && !isManualRefresh) {
        setMessage("Checking open applications…");
      }
      send({ type: "apps.list", operationId });
    },
    [capability.canUse, send, state],
  );

  const handleListResult = useCallback(
    (result: AppsListResultMessage) => {
      if (listOperationRef.current !== result.operationId) {
        return;
      }
      listOperationRef.current = null;
      setLoading(false);
      setRefreshing(false);
      setClosing(false);
      hasCompletedInitialListRef.current = true;
      setHasCompletedInitialList(true);
      if (!result.succeeded || !result.revision) {
        actionOperationRef.current = null;
        setPendingWindowId(null);
        setRevision(null);
        setWindows([]);
        setMessage(result.message);
        clearPreviewUrls();
        return;
      }

      const nextWindows = preserveWindowOrder(windows, result.windows);
      const selectedWindow = windows[selectedIndex];
      const pendingAction = actionOperationRef.current;
      const nextSelectedIndex = selectedWindow
        ? nextWindows.findIndex(
            (window) =>
              window.windowId === selectedWindow.windowId ||
              (window.title === selectedWindow.title &&
                window.applicationName === selectedWindow.applicationName),
          )
        : -1;
      const activeIndex = nextWindows.findIndex((window) => window.active);
      const resolvedSelectedIndex =
        nextSelectedIndex >= 0 ? nextSelectedIndex : Math.max(0, activeIndex);
      setWindows(nextWindows);
      setRevision(result.revision);
      setPendingWindowId(null);
      selectedIndexRef.current = resolvedSelectedIndex;
      setSelectedIndex(resolvedSelectedIndex);
      const previewTarget =
        pendingAction?.type === "activate" && pendingAction.target
          ? nextWindows.find(
              (window) =>
                window.title === pendingAction.target?.title &&
                window.applicationName === pendingAction.target?.applicationName,
            )
          : undefined;
      reconcilePreviewUrls(
        nextWindows.map((window) => window.windowId),
        pendingAction?.target && previewTarget
          ? {
              sourceWindowId: pendingAction.target.windowId,
              targetWindowId: previewTarget.windowId,
            }
          : undefined,
      );
      if (pendingAction?.type === "activate" && previewTarget?.previewSupported) {
        refreshPreview(previewTarget.windowId);
      }
      if (pendingAction?.type === "close" && pendingAction.target) {
        const stillOpen = nextWindows.some(
          (window) =>
            window.title === pendingAction.target?.title &&
            window.applicationName === pendingAction.target?.applicationName,
        );
        setMessage(
          stillOpen
            ? "Check the PC for a save or confirmation prompt."
            : nextWindows.length === 0
              ? "No application windows are open on this PC."
              : "Tap the centered card to activate it. Swipe up or use Close to close it.",
        );
      } else {
        setMessage(
          nextWindows.length === 0
            ? "No application windows are open on this PC."
            : "Tap the centered card to activate it. Swipe up or use Close to close it.",
        );
      }
      actionOperationRef.current = null;
    },
    [clearPreviewUrls, reconcilePreviewUrls, refreshPreview, selectedIndex, windows],
  );

  useEffect(() => {
    const timeout = window.setTimeout(requestList, 0);
    return () => window.clearTimeout(timeout);
  }, [requestList]);

  useEffect(() => {
    const visibilityChange = () => {
      if (document.visibilityState === "hidden") {
        listOperationRef.current = null;
        actionOperationRef.current = null;
        setLoading(false);
        setPendingWindowId(null);
        setClosing(false);
        return;
      }
      requestList(true);
    };
    document.addEventListener("visibilitychange", visibilityChange);
    return () => document.removeEventListener("visibilitychange", visibilityChange);
  }, [requestList]);

  useEffect(() => {
    return subscribeAppsResults((result) => {
      if (result.type === "apps.list.result") {
        handleListResult(result);
      } else if (result.type === "apps.preview.offer") {
        void acceptPreviewOffer(result);
      } else if (result.type === "apps.preview.ended") {
        if (ownsPreview(result.previewId)) {
          closePreview(false);
          setMessage(result.message);
        }
      } else if (result.type === "apps.activate.result" || result.type === "apps.close.result") {
        const pending = actionOperationRef.current;
        if (pending?.operationId !== result.operationId) {
          return;
        }
        setPendingWindowId(null);
        if (!result.succeeded) {
          actionOperationRef.current = null;
          setClosing(false);
          if (result.code === "stale-window") {
            const staleIndex = windows.findIndex(
              (window) => window.windowId === pending.target?.windowId,
            );
            if (staleIndex >= 0) {
              const nextWindows = windows.filter((_, index) => index !== staleIndex);
              const nextIndex = Math.min(staleIndex, nextWindows.length);
              setWindows(nextWindows);
              selectedIndexRef.current = nextIndex;
              setSelectedIndex(nextIndex);
            }
            requestList(true);
            return;
          }
          setMessage(result.message);
          onFeedback(result.message, "error");
          return;
        }
        if (result.type === "apps.activate.result") {
          setWindows((currentWindows) =>
            currentWindows.map((window) =>
              window.windowId === result.windowId
                ? { ...window, active: true, minimized: false }
                : window.active
                  ? { ...window, active: false }
                  : window,
            ),
          );
        }
        listOperationRef.current = null;
        requestList(true);
      } else if (result.type === "apps.preview.answer.result" && !result.succeeded) {
        setMessage(result.message);
        closePreview(false);
      }
    });
  }, [
    acceptPreviewOffer,
    closePreview,
    handleListResult,
    onFeedback,
    ownsPreview,
    requestList,
    windows,
  ]);

  useEffect(() => {
    const launchedAction = launchedActionRef.current;
    if (!launchedAction || appLaunchResult?.actionId !== launchedAction) {
      return;
    }
    launchedActionRef.current = null;
    if (!appLaunchResult.succeeded) {
      return;
    }
    const timeout = window.setTimeout(() => requestList(true), 800);
    return () => window.clearTimeout(timeout);
  }, [appLaunchResult, requestList]);

  const scrollToIndex = useCallback((index: number, smooth = true) => {
    const carousel = carouselRef.current;
    const card = carousel?.querySelector<HTMLElement>(
      `[data-app-index="${index}"][data-app-canonical="true"]`,
    );
    if (!carousel || !card) {
      return;
    }
    const behavior =
      smooth &&
      (typeof window.matchMedia !== "function" ||
        !window.matchMedia("(prefers-reduced-motion: reduce)").matches)
        ? "smooth"
        : "auto";
    carousel.scrollTo({
      left: card.offsetLeft - (carousel.clientWidth - card.clientWidth) / 2,
      behavior,
    });
    if (behavior === "auto") {
      updateCarouselCardFocus(carousel);
    }
  }, []);

  const recenterSelectedCard = useCallback(() => {
    scrollSelectionSuppressedRef.current = true;
    if (selectionFrameRef.current !== null) {
      cancelAnimationFrame(selectionFrameRef.current);
      selectionFrameRef.current = null;
    }
    if (resizeReleaseFrameRef.current !== null) {
      cancelAnimationFrame(resizeReleaseFrameRef.current);
    }
    scrollToIndex(selectedIndexRef.current, false);
    resizeReleaseFrameRef.current = requestAnimationFrame(() => {
      resizeReleaseFrameRef.current = requestAnimationFrame(() => {
        resizeReleaseFrameRef.current = null;
        scrollSelectionSuppressedRef.current = false;
      });
    });
  }, [scrollToIndex]);

  useEffect(() => {
    if (carouselVisible && revision) {
      recenterSelectedCard();
    }
  }, [carouselVisible, recenterSelectedCard, revision, windows.length]);

  useEffect(() => {
    if (!carouselVisible) {
      return;
    }
    const carousel = carouselRef.current;
    if (!carousel || typeof ResizeObserver === "undefined") {
      return;
    }
    const observer = new ResizeObserver(() => {
      if (resizeFrameRef.current !== null) {
        cancelAnimationFrame(resizeFrameRef.current);
      }
      resizeFrameRef.current = requestAnimationFrame(() => {
        resizeFrameRef.current = null;
        recenterSelectedCard();
      });
    });
    observer.observe(carousel);
    return () => observer.disconnect();
  }, [carouselVisible, recenterSelectedCard]);

  useEffect(
    () => () => {
      if (selectionFrameRef.current !== null) {
        cancelAnimationFrame(selectionFrameRef.current);
      }
      if (resizeFrameRef.current !== null) {
        cancelAnimationFrame(resizeFrameRef.current);
      }
      if (resizeReleaseFrameRef.current !== null) {
        cancelAnimationFrame(resizeReleaseFrameRef.current);
      }
      if (scrollSettleTimeoutRef.current !== null) {
        window.clearTimeout(scrollSettleTimeoutRef.current);
      }
      scrollSelectionSuppressedRef.current = false;
    },
    [],
  );

  const onCarouselScroll = () => {
    if (scrollSelectionSuppressedRef.current || selectionFrameRef.current !== null) {
      return;
    }
    if (scrollSettleTimeoutRef.current !== null) {
      window.clearTimeout(scrollSettleTimeoutRef.current);
    }
    scrollSettleTimeoutRef.current = window.setTimeout(() => {
      scrollSettleTimeoutRef.current = null;
      const carousel = carouselRef.current;
      if (carousel) {
        updateCarouselCardFocus(carousel);
      }
      const nearestCard = carousel
        ? normalizeCarouselLoop(carousel, findNearestCarouselCard(carousel))
        : null;
      if (!carousel || !nearestCard) {
        return;
      }
      updateCarouselCardFocus(carousel);
      const nearestIndex = Number(nearestCard.dataset.appIndex ?? 0);
      if (nearestIndex !== selectedIndexRef.current) {
        selectedIndexRef.current = nearestIndex;
        setSelectedIndex(nearestIndex);
      }
      const centeredScrollLeft =
        nearestCard.offsetLeft - (carousel.clientWidth - nearestCard.clientWidth) / 2;
      if (Math.abs(carousel.scrollLeft - centeredScrollLeft) > 1) {
        carousel.scrollTo({ left: centeredScrollLeft, behavior: "smooth" });
      }
    }, 120);
    selectionFrameRef.current = requestAnimationFrame(() => {
      selectionFrameRef.current = null;
      if (scrollSelectionSuppressedRef.current) {
        return;
      }
      const carousel = carouselRef.current;
      if (!carousel) {
        return;
      }
      const nearestCard = findNearestCarouselCard(carousel);
      updateCarouselCardFocus(carousel);
      const nearestIndex = Number(nearestCard?.dataset.appIndex ?? 0);
      if (nearestIndex !== selectedIndexRef.current) {
        selectedIndexRef.current = nearestIndex;
        setSelectedIndex(nearestIndex);
      }
    });
  };

  const prepareCarouselGesture = () => {
    const carousel = carouselRef.current;
    if (!carousel) {
      return;
    }
    if (scrollSettleTimeoutRef.current !== null) {
      window.clearTimeout(scrollSettleTimeoutRef.current);
      scrollSettleTimeoutRef.current = null;
    }
    if (selectionFrameRef.current !== null) {
      cancelAnimationFrame(selectionFrameRef.current);
      selectionFrameRef.current = null;
    }
    const nearestCard = normalizeCarouselLoop(carousel, findNearestCarouselCard(carousel));
    if (!nearestCard) {
      return;
    }
    updateCarouselCardFocus(carousel);
    const nearestIndex = Number(nearestCard.dataset.appIndex ?? 0);
    if (nearestIndex !== selectedIndexRef.current) {
      selectedIndexRef.current = nearestIndex;
      setSelectedIndex(nearestIndex);
    }
  };

  const runWindowAction = (type: "activate" | "close", window: AppsWindowSummary) => {
    if (!revision || pendingWindowId || listOperationRef.current || state !== "paired") {
      return;
    }
    const operationId = localId();
    actionOperationRef.current = { operationId, type, target: window };
    setPendingWindowId(window.windowId);
    setClosing(type === "close");
    send({
      type: type === "close" ? "apps.close" : "apps.activate",
      operationId,
      revision,
      windowId: window.windowId,
    });
  };

  const loadingCardText = closing ? "Closing…" : refreshing ? "Refreshing…" : "Loading…";
  const openCardIndex = windows.length;
  const carouselCardCount = openCardIndex + 1;
  const canonicalCarouselCards = Array.from({ length: carouselCardCount }, (_, logicalIndex) => ({
    key: logicalIndex === openCardIndex ? "open" : windows[logicalIndex]!.windowId,
    logicalIndex,
    loopClone: false,
  }));
  let carouselCards = canonicalCarouselCards;
  if (windows.length > 1) {
    carouselCards = [
      ...canonicalCarouselCards.map((card) => ({
        ...card,
        key: `loop-start-${card.key}`,
        loopClone: true,
      })),
      ...canonicalCarouselCards,
      ...canonicalCarouselCards.map((card) => ({
        ...card,
        key: `loop-end-${card.key}`,
        loopClone: true,
      })),
    ];
  }

  const launchApp = (actionId: string) => {
    launchedActionRef.current = actionId;
    onAppLaunch(actionId);
  };

  const launcherGrid =
    supportsRemoteLaunch && appLaunchActions.length > 0 ? (
      <div className="apps-launch-grid" aria-label="Application launcher">
        {appLaunchActions.map((action) => (
          <button
            key={action.id}
            type="button"
            disabled={pendingAppLaunchId !== null}
            onClick={() => launchApp(action.id)}
          >
            <AppWindow aria-hidden="true" />
            <span>{pendingAppLaunchId === action.id ? "Starting…" : action.label}</span>
          </button>
        ))}
      </div>
    ) : (
      <p className="apps-launch-unavailable">App launching is not enabled on this PC.</p>
    );

  return (
    <section
      className="apps-workspace"
      aria-labelledby="apps-title"
      aria-busy={loading || showLoadingCard}
    >
      <header className="apps-header">
        <button type="button" className="icon-button" aria-label="Back" onClick={onBack}>
          <ArrowLeft aria-hidden="true" />
        </button>
        <div>
          <h2 id="apps-title">Apps</h2>
          <p>Open applications on your PC</p>
        </div>
        <div className="apps-header-actions">
          <button
            type="button"
            className="icon-button"
            aria-label="Open Trackpad"
            title="Open Trackpad"
            onClick={onOpenTrackpad}
          >
            <MousePointer2 aria-hidden="true" />
          </button>
          <button
            type="button"
            className="icon-button"
            aria-label="Refresh applications"
            disabled={loading || unavailableMessage !== null}
            onClick={() => requestList()}
          >
            <RefreshCw className={loading ? "apps-refreshing" : undefined} aria-hidden="true" />
          </button>
        </div>
      </header>

      {unavailableMessage ? (
        <div className="apps-empty-state" role="status">
          <AppWindow aria-hidden="true" />
          <p>{unavailableMessage}</p>
        </div>
      ) : showLoadingCard ? (
        <>
          <div
            className="apps-carousel apps-loading-carousel"
            role="status"
            aria-label={
              closing
                ? "Closing application"
                : refreshing
                  ? "Refreshing applications"
                  : "Loading open applications"
            }
          >
            <div className="apps-carousel-item">
              <article className="apps-window-card apps-loading-card is-selected">
                <span className="apps-loading-content" aria-hidden="true">
                  <LoaderCircle className="apps-loading-spinner" />
                  <span className="apps-loading-text">{loadingCardText}</span>
                </span>
              </article>
            </div>
          </div>
          <p className="apps-status" role="status">
            {message}
          </p>
        </>
      ) : launcherExpanded ? (
        <>
          <section className="apps-launcher-panel" aria-labelledby="apps-launcher-title">
            <header className="apps-launcher-panel-header">
              <span className="apps-open-icon" aria-hidden="true">
                <Plus />
              </span>
              <div>
                <strong id="apps-launcher-title">Open app</strong>
                <span>Approved shortcuts on this PC</span>
              </div>
              <button
                type="button"
                className="icon-button"
                aria-label="Close application launcher"
                onClick={() => {
                  setLauncherExpanded(false);
                  requestAnimationFrame(recenterSelectedCard);
                }}
              >
                <X aria-hidden="true" />
              </button>
            </header>
            <div className="apps-launcher-scroll">{launcherGrid}</div>
          </section>
          <p className="apps-status" role="status">
            {message}
          </p>
        </>
      ) : (
        <>
          <div
            ref={carouselRef}
            className="apps-carousel"
            aria-label="Open applications"
            onScroll={onCarouselScroll}
            onTouchStart={prepareCarouselGesture}
          >
            {carouselCards.map(({ key, logicalIndex, loopClone }) => {
              const window = windows[logicalIndex];
              return (
                <div
                  key={key}
                  className="apps-carousel-item"
                  data-app-index={logicalIndex}
                  data-app-canonical={loopClone ? undefined : "true"}
                  data-app-loop-clone={loopClone ? "true" : undefined}
                  aria-hidden={loopClone ? "true" : undefined}
                  inert={loopClone ? true : undefined}
                >
                  {window ? (
                    <AppsWindowCard
                      busy={loading || pendingWindowId !== null}
                      onActivate={() => runWindowAction("activate", window)}
                      onClose={() => runWindowAction("close", window)}
                      onSelect={() => scrollToIndex(logicalIndex)}
                      previewState={
                        previewStates.get(window.windowId) ??
                        (capability.previewAvailable && window.previewSupported
                          ? "loading"
                          : "unavailable")
                      }
                      previewUrl={previewUrls.get(window.windowId)}
                      selected={!loopClone && selectedIndex === logicalIndex}
                      window={window}
                    />
                  ) : (
                    <article
                      className={`apps-window-card apps-open-card${!loopClone && selectedIndex === openCardIndex ? " is-selected" : ""}`}
                    >
                      <button
                        type="button"
                        className="apps-open-card-main"
                        aria-label="Open application launcher"
                        onClick={() => {
                          selectedIndexRef.current = openCardIndex;
                          setSelectedIndex(openCardIndex);
                          setLauncherExpanded(true);
                        }}
                      >
                        <span className="apps-open-card-heading">
                          <span className="apps-open-icon" aria-hidden="true">
                            <Plus />
                          </span>
                          <span>
                            <strong>Open app</strong>
                            <span>Approved shortcuts on this PC</span>
                          </span>
                        </span>
                        <span className="apps-open-card-action" aria-hidden="true">
                          <Maximize2 />
                          View available apps
                        </span>
                      </button>
                    </article>
                  )}
                </div>
              );
            })}
          </div>
          <p className="apps-status" role="status">
            {message}
          </p>
        </>
      )}
    </section>
  );
}
