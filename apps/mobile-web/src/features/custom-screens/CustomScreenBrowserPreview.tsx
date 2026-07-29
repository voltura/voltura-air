import { useEffect, useState } from "react";
import { parseServerMessage } from "../../foundation/connection/connectionProtocol";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { CustomScreenDefinition } from "../../foundation/protocol/messages";
import { CustomScreenWorkspace } from "./CustomScreenWorkspace";

const validScreenId = /^[A-Za-z0-9._-]{1,64}$/;
const ignorePreviewAction = () => undefined;
const leavePreview = () => {
  if (window.history.length > 1) {
    window.history.back();
  } else {
    window.location.assign("/");
  }
};

export function readCustomScreenPreviewId(url: string): string | null {
  try {
    const screenId = new URL(url).searchParams.get("customScreenPreview");
    return screenId !== null && validScreenId.test(screenId) ? screenId : null;
  } catch {
    return null;
  }
}

export function readCustomScreenPreviewControlDepth(url: string): boolean {
  try {
    return new URL(url).searchParams.get("controlDepth") === "true";
  } catch {
    return false;
  }
}

export function CustomScreenBrowserPreview({
  controlDepth,
  screenId
}: {
  controlDepth: boolean;
  screenId: string;
}) {
  const [definition, setDefinition] = useState<CustomScreenDefinition | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    void fetch(`/api/custom-screens/preview/${encodeURIComponent(screenId)}`, {
      cache: "no-store",
      signal: abort.signal
    }).then(async (response) => {
      if (!response.ok) {
        throw new Error("The saved custom screen is unavailable.");
      }

      const message = parseServerMessage(await response.text());
      if (message?.type !== "custom.screen.get.result" ||
          !message.succeeded ||
          !message.screen) {
        throw new Error("The custom-screen preview response was invalid.");
      }

      setDefinition(message.screen);
    }).catch((reason: unknown) => {
      if (!abort.signal.aborted) {
        setError(reason instanceof Error
          ? reason.message
          : "The custom screen could not be previewed.");
      }
    });
    return () => { abort.abort(); };
  }, [screenId]);

  return (
    <div className={`app-frame custom-screen-browser-preview${controlDepth ? " control-depth" : ""}`}>
      <CustomScreenWorkspace
        definition={definition}
        error={error}
        invoke={ignorePreviewAction}
        onBack={leavePreview}
        pendingButtonIds={new Set()}
        requestedName="Custom screen preview"
        send={ignorePreviewAction}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
      <div className="custom-screen-preview-notice" role="status">
        Preview only · actions are disabled
      </div>
    </div>
  );
}
