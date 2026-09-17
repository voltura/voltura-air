import { useCallback, useEffect, useRef, useState } from "react";
import { readLocalStorage, writeLocalStorage } from "../platform/browserStorage";

const automaticAttemptKey = "voltura-air.storageProtection.autoAttempted.v1";

export type StorageProtectionState =
  | "checking"
  | "available"
  | "requesting"
  | "enabled"
  | "declined"
  | "unavailable"
  | "failed";

function supportsPersistentStorage(): boolean {
  return (
    typeof navigator.storage?.persisted === "function" &&
    typeof navigator.storage?.persist === "function"
  );
}

async function shouldRequestAutomatically(): Promise<boolean> {
  const userAgent = navigator.userAgent;
  const isIos =
    /iPhone|iPad|iPod/i.test(userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  if (
    isIos &&
    /AppleWebKit\//i.test(userAgent) &&
    !/CriOS|FxiOS|EdgiOS|OPiOS/i.test(userAgent) &&
    (/Safari\//i.test(userAgent) || navigator.standalone === true)
  ) {
    return true;
  }
  try {
    // Querying does not prompt. Only request automatically with permission in hand.
    const permission = await navigator.permissions?.query({ name: "persistent-storage" });
    return permission?.state === "granted";
  } catch {
    return false;
  }
}

export function usePersistentStorage() {
  const [storageProtection, setStorageProtection] = useState<StorageProtectionState>(() =>
    supportsPersistentStorage() ? "checking" : "unavailable",
  );
  const initialCheck = useRef<Promise<boolean> | null>(null);
  const mounted = useRef(false);
  const requesting = useRef(false);
  const requested = useRef(false);
  const autoAttempted = useRef(false);

  const requestProtection = useCallback(async () => {
    if (!mounted.current || requesting.current) {
      return;
    }
    if (!supportsPersistentStorage()) {
      setStorageProtection("unavailable");
      return;
    }
    requested.current = true;
    requesting.current = true;
    setStorageProtection("requesting");
    try {
      const persistent = await navigator.storage.persist();
      if (mounted.current) {
        setStorageProtection(persistent ? "enabled" : "declined");
      }
    } catch {
      if (mounted.current) {
        setStorageProtection("failed");
      }
    } finally {
      requesting.current = false;
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    let cancelled = false;
    if (supportsPersistentStorage()) {
      // Reuse the check across React's effect replay before any automatic request.
      initialCheck.current ??= Promise.resolve().then(() => navigator.storage.persisted());
      void initialCheck.current.then(
        async (persistent) => {
          if (!cancelled && !requested.current) {
            if (persistent) {
              setStorageProtection("enabled");
            } else {
              const automatic =
                !autoAttempted.current &&
                readLocalStorage(automaticAttemptKey) !== "1" &&
                (await shouldRequestAutomatically());
              if (cancelled || requested.current) {
                return;
              }
              if (automatic) {
                autoAttempted.current = true;
                writeLocalStorage(automaticAttemptKey, "1");
                void requestProtection();
              } else {
                setStorageProtection("available");
              }
            }
          }
        },
        () => {
          if (!cancelled && !requested.current) {
            setStorageProtection("failed");
          }
        },
      );
    }
    return () => {
      cancelled = true;
      mounted.current = false;
    };
  }, [requestProtection]);

  const protectSavedData = useCallback(async () => {
    if (!mounted.current || requesting.current || storageProtection === "enabled") {
      return;
    }
    // Invoke directly from the settings action, without awaiting the initial check.
    await requestProtection();
  }, [requestProtection, storageProtection]);

  return { protectSavedData, storageProtection };
}
