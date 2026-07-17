import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { getDisplayMode } from "./clientEnvironment";
import "./styles.css";

document.documentElement.dataset.displayMode = getDisplayMode();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`/sw.js?build=${encodeURIComponent(__WEB_BUILD_ID__)}`).catch(() => {
      // The app still works without offline caching.
    });
  });
}
