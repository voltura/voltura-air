import { lazy, StrictMode, Suspense } from "react";
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
const screenViewPreview = import.meta.env.DEV && new URL(window.location.href).searchParams.get("screenPreview") === "1";
const ScreenViewBrowserPreviewRoot = lazy(() => import("./features/screen-view").then((module) => ({ default: module.ScreenViewBrowserPreviewRoot })));

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {screenViewPreview
      ? <Suspense fallback={null}><ScreenViewBrowserPreviewRoot /></Suspense>
      : previewScreenId === null
      ? <App />
      : (
          <CustomScreenBrowserPreviewRoot
            controlDepth={previewControlDepth}
            screenId={previewScreenId}
          />
        )}
  </StrictMode>
);

if (!screenViewPreview && previewScreenId === null && "serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js?build=${encodeURIComponent(__WEB_BUILD_ID__)}`, { scope: import.meta.env.BASE_URL }).catch(() => {
      // The app still works without offline caching.
    });
  });
}
