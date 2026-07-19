import { useCallback, useEffect, useRef, useState } from "react";
import { getAutoRefreshSessionKey } from "../settings/appStorage";
import type { ConnectionState } from "../connection/connectionTypes";
import type { PcProfile } from "../connection/pcProfiles";
import type { HostStatusMetadata } from "../protocol/messages";
import { refreshWithFreshAppUrl, type FreshAppRefreshResult } from "./freshAppRefresh";

export type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed"; platform: string }>;
};

declare global {
  interface Navigator {
    standalone?: boolean;
  }
}

interface PwaLifecycleOptions {
  activePc: PcProfile | null;
  autoRefresh: boolean;
  clientId: string;
  hostStatus: HostStatusMetadata | null;
  state: ConnectionState;
}

export function usePwaLifecycle({ activePc, autoRefresh, clientId, hostStatus, state }: PwaLifecycleOptions) {
  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isInstalled, setIsInstalled] = useState(() => isRunningStandalone());
  const [refreshMessage, setRefreshMessage] = useState("Reload from the PC if the home screen app looks stale.");
  const refreshAttemptRef = useRef<Promise<FreshAppRefreshResult> | null>(null);
  const refreshKeysRef = useRef(new Set<string>());
  const refreshGuardsCommittedRef = useRef(false);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => { isMountedRef.current = false; };
  }, []);

  useEffect(() => {
    const onBeforeInstallPrompt = (event: Event) => {
      event.preventDefault();
      setInstallPrompt(event as BeforeInstallPromptEvent);
    };
    const onAppInstalled = () => {
      setIsInstalled(true);
      setInstallPrompt(null);
    };

    window.addEventListener("beforeinstallprompt", onBeforeInstallPrompt);
    window.addEventListener("appinstalled", onAppInstalled);

    return () => {
      window.removeEventListener("beforeinstallprompt", onBeforeInstallPrompt);
      window.removeEventListener("appinstalled", onAppInstalled);
    };
  }, []);

  const installApp = async () => {
    if (!installPrompt) {
      return;
    }

    await installPrompt.prompt();
    const choice = await installPrompt.userChoice;
    if (choice.outcome === "accepted") {
      setIsInstalled(true);
    }
    setInstallPrompt(null);
  };

  const runRefreshAttempt = useCallback((refreshKey?: string): Promise<FreshAppRefreshResult> => {
    if (refreshKey) {
      refreshKeysRef.current.add(refreshKey);
      if (refreshGuardsCommittedRef.current) {
        setRefreshGuard(refreshKey);
      }
    }

    const activeAttempt = refreshAttemptRef.current;
    if (activeAttempt) {
      return activeAttempt;
    }

    refreshGuardsCommittedRef.current = false;
    const attempt = (async () => {
      let result: FreshAppRefreshResult;
      try {
        result = await refreshWithFreshAppUrl(undefined, () => {
          refreshGuardsCommittedRef.current = true;
          for (const key of refreshKeysRef.current) {
            setRefreshGuard(key);
          }
        });
      } catch (error) {
        result = {
          navigationStarted: false,
          navigationMethod: null,
          warnings: [`refresh boundary failed: ${error instanceof Error ? error.message : String(error)}`]
        };
      }

      if (!result.navigationStarted) {
        for (const key of refreshKeysRef.current) {
          clearRefreshGuard(key);
        }
      }
      return result;
    })();
    refreshAttemptRef.current = attempt;
    void attempt.then(() => {
      if (refreshAttemptRef.current === attempt) {
        refreshAttemptRef.current = null;
        refreshKeysRef.current.clear();
        refreshGuardsCommittedRef.current = false;
      }
    });
    return attempt;
  }, []);

  const refreshInstalledApp = useCallback(async () => {
    setRefreshMessage("Refreshing app...");
    const result = await runRefreshAttempt();
    if (!result.navigationStarted && isMountedRef.current) {
      setRefreshMessage("Could not refresh the app. Check this browser's site permissions and try again.");
    }
  }, [runRefreshAttempt]);

  useEffect(() => {
    if (!autoRefresh || state !== "paired" || !activePc) {
      return;
    }

    const webClientBuildId = hostStatus?.webClientBuildId;
    if (!shouldRefreshWebClient(webClientBuildId)) {
      return;
    }

    const refreshKey = getAutoRefreshSessionKey(clientId, activePc.id, webClientBuildId);
    if (hasRefreshGuard(refreshKey)) {
      return;
    }

    void runRefreshAttempt(refreshKey).then((result) => {
      if (!result.navigationStarted && isMountedRef.current) {
        setRefreshMessage("Could not refresh the app automatically. Use Refresh app to try again.");
      }
    });
  }, [activePc, autoRefresh, clientId, hostStatus, runRefreshAttempt, state]);

  return { installApp, installPrompt, isInstalled, refreshInstalledApp, refreshMessage };
}

export function shouldRefreshWebClient(webClientBuildId: string | undefined): webClientBuildId is string {
  return Boolean(webClientBuildId && webClientBuildId !== __WEB_BUILD_ID__);
}

function isRunningStandalone(): boolean {
  return window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
}

function hasRefreshGuard(key: string): boolean {
  try {
    return sessionStorage.getItem(key) === "true";
  } catch {
    return false;
  }
}

function setRefreshGuard(key: string): void {
  try {
    sessionStorage.setItem(key, "true");
  } catch {
    // Navigation remains safer than leaving a known stale application loaded.
  }
}

function clearRefreshGuard(key: string): void {
  try {
    sessionStorage.removeItem(key);
  } catch {
    // A blocked storage API cannot retain a guard written by this attempt.
  }
}
