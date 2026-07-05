import React from "react";
import ReactDOM from "react-dom/client";
import { App } from "./App";
import "./styles.css";
import "./pairingFeedback.css";
import "./keyboardViewport.css";
import "./splitMode.css";
import "./remoteMode.css";
import "./remoteNavigationTrackpad.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`/sw.js?v=${encodeURIComponent(__APP_VERSION__)}`).catch(() => {
      // The app still works without offline caching.
    });
  });
}
