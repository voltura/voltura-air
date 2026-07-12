import React from "react";
import ReactDOM from "react-dom/client";
import { App } from "./App";
import "./styles.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`/sw.js?build=${encodeURIComponent(__WEB_BUILD_ID__)}`).catch(() => {
      // The app still works without offline caching.
    });
  });
}
