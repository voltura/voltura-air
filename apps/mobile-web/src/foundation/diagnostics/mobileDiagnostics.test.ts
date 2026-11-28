import { describe, expect, it, vi } from "vitest";
import { buildMobileDiagnostics, sanitizeUrl } from "./mobileDiagnostics";

it("omits the pairing fragment from hosted URLs in copied diagnostics", () => {
  const address = new URL("https://voltura.se/air/app/?r=route&v=1.5.7");
  const withFragment = new URL(address);
  withFragment.hash = "A".repeat(32);

  expect(sanitizeUrl(withFragment.href)).toBe(address.href);
});

describe("buildMobileDiagnostics", () => {
  it("does not invent a WebSocket or TCP port for Secure Direct", () => {
    vi.stubGlobal("__APP_VERSION__", "test-version");
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false })),
    );
    const route = "r".repeat(22);
    const diagnostics = JSON.parse(
      buildMobileDiagnostics({
        activePc: {
          customName: false,
          id: `relay:voltura-cloud-v1:${route}`,
          name: "PC",
          url: `https://voltura.se/s/${route}`,
          transportMode: "secure-direct",
          relayRouteId: route,
          relayServiceId: "voltura-cloud-v1",
        },
        connectionState: "paired",
        message: "Connected",
        pairedPcCount: 1,
        hostStatus: {
          selectedIp: "192.168.1.10",
          selectedPort: 51396,
          webSocketUrl: "ws://192.168.1.10:51396/ws",
        },
      }),
    ) as Record<string, unknown>;

    expect(diagnostics.selectedIp).toBe("192.168.1.10");
    expect(diagnostics.selectedPort).toBeNull();
    expect(diagnostics.currentWebSocketUrl).toBeNull();
    expect(diagnostics.speechRecognition).toEqual({ lifecycleVersion: 5, events: [] });
  });
});
