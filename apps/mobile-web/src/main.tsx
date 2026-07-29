import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { CustomScreenBrowserPreviewRoot } from "./app/CustomScreenBrowserPreviewRoot";
import {
  readCustomScreenPreviewControlDepth,
  readCustomScreenPreviewId
} from "./features/custom-screens";
import { getDisplayMode } from "./foundation/platform/clientEnvironment";
import "./styles.css";

document.documentElement.dataset.displayMode = getDisplayMode();
const previewScreenId = readCustomScreenPreviewId(window.location.href);
const previewControlDepth =
  readCustomScreenPreviewControlDepth(window.location.href);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {previewScreenId === null
      ? <App />
      : (
          <CustomScreenBrowserPreviewRoot
            controlDepth={previewControlDepth}
            screenId={previewScreenId}
          />
        )}
  </StrictMode>
);

if (previewScreenId === null && "serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`/sw.js?build=${encodeURIComponent(__WEB_BUILD_ID__)}`).catch(() => {
      // The app still works without offline caching.
    });
  });
}
