import { defineConfig } from "vitest/config";

export default defineConfig({
  define: {
    __RELAY_SERVICE_ID__: JSON.stringify("voltura-cloud-v1"),
    __RELAY_HTTPS_BASE__: JSON.stringify("https://voltura-air-relay.voltura-air.workers.dev")
  },
  test: {
    environment: "jsdom",
    globals: true,
    include: ["src/**/*.test.{ts,tsx}"]
  }
});
