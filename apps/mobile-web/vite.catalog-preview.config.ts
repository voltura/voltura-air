import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  publicDir: false,
  define: {
    __APP_VERSION__: JSON.stringify("catalog-preview"),
    __WEB_BUILD_ID__: JSON.stringify("catalog-preview"),
    "process.env.NODE_ENV": JSON.stringify("production"),
  },
  plugins: [react()],
  build: {
    emptyOutDir: true,
    outDir: fileURLToPath(new URL("../../apps/public-site/screens/assets", import.meta.url)),
    lib: {
      entry: fileURLToPath(new URL("./src/app/catalog-preview.tsx", import.meta.url)),
      formats: ["iife"],
      name: "VolturaAirCatalogPreview",
      fileName: () => "catalog-preview.js",
      cssFileName: "catalog-preview",
    },
  },
});
