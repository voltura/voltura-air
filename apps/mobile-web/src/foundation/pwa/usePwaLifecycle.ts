import { useEffect, useState } from "react";
import { getAutoRefreshSessionKey } from "../settings/appStorage";
import type { ConnectionState } from "../connection/connectionTypes";
import type { PcProfile } from "../connection/pcProfiles";
import type { HostStatusMetadata } from "../protocol/messages";

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

  const refreshInstalledApp = async () => {
    setRefreshMessage("Refreshing app...");
    await replaceWithFreshAppUrl();
  };

  useEffect(() => {
    if (!autoRefresh || state !== "paired" || !activePc) {
      return;
    }

    const webClientBuildId = hostStatus?.webClientBuildId;
    if (!shouldRefreshWebClient(webClientBuildId)) {
      return;
    }

    const refreshKey = getAutoRefreshSessionKey(clientId, activePc.id, webClientBuildId);
    if (sessionStorage.getItem(refreshKey) === "true") {
      return;
    }

    sessionStorage.setItem(refreshKey, "true");
    void replaceWithFreshAppUrl();
  }, [activePc, autoRefresh, clientId, hostStatus?.webClientBuildId, state]);

  return { installApp, installPrompt, isInstalled, refreshInstalledApp, refreshMessage };
}

export function shouldRefreshWebClient(webClientBuildId: string | undefined): webClientBuildId is string {
  return Boolean(webClientBuildId && webClientBuildId !== __WEB_BUILD_ID__);
}

function isRunningStandalone(): boolean {
  return window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
}

async function replaceWithFreshAppUrl(): Promise<void> {
  if ("serviceWorker" in navigator) {
    const registrations = await navigator.serviceWorker.getRegistrations();
    await Promise.all(registrations.map((registration) => registration.unregister()));
  }

  if ("caches" in window) {
    const cacheNames = await caches.keys();
    await Promise.all(cacheNames.map((cacheName) => caches.delete(cacheName)));
  }

  const freshUrl = new URL(window.location.href);
  freshUrl.searchParams.delete("t");
  freshUrl.searchParams.set("refresh", Date.now().toString());
  window.location.replace(freshUrl);
}
