import { afterEach, describe, expect, it, vi } from "vitest";
import { createTurnResponse, type TurnEnvironment } from "../src/cloudflare/turn";

const environment: TurnEnvironment = {
  TURN_KEY_ID: "turn-key",
  TURN_API_TOKEN: "turn-token",
  CLOUDFLARE_ACCOUNT_ID: "account",
  CLOUDFLARE_ANALYTICS_TOKEN: "analytics-token",
  USAGE_WARNING_BYTES: "750000000000",
  USAGE_CUTOFF_BYTES: "850000000000"
};

describe("Cloudflare TURN quota policy", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("issues bounded 15-minute credentials below the warning", async () => {
    const fetch = mockFetch(100_000_000_000);
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, "r".repeat(22));
    const body = await response.json() as {
      allowed: boolean;
      forcedQuality: string | null;
      usageWarningBytes: number;
      usageCutoffBytes: number;
      iceServers: unknown[];
    };

    expect(response.status).toBe(200);
    expect(body).toMatchObject({
      allowed: true,
      forcedQuality: null,
      usageWarningBytes: 750_000_000_000,
      usageCutoffBytes: 850_000_000_000
    });
    expect(body.iceServers).toHaveLength(1);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("forces Data Saver at the warning threshold", async () => {
    vi.stubGlobal("fetch", mockFetch(750_000_000_000));
    const response = await createTurnResponse(environment, "r".repeat(22));
    await expect(response.json()).resolves.toMatchObject({ allowed: true, forcedQuality: "data-saver" });
  });

  it("blocks credentials at the cutoff", async () => {
    const fetch = mockFetch(850_000_000_000);
    vi.stubGlobal("fetch", fetch);
    const response = await createTurnResponse(environment, "r".repeat(22));

    expect(response.status).toBe(429);
    await expect(response.json()).resolves.toMatchObject({
      allowed: false,
      code: "quota-blocked",
      usageWarningBytes: 750_000_000_000,
      usageCutoffBytes: 850_000_000_000
    });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("publishes valid configured limits and fails safe for invalid ordering", async () => {
    vi.stubGlobal("fetch", mockFetch(100_000_000_000));
    const configured = await createTurnResponse({
      ...environment,
      USAGE_WARNING_BYTES: "600000000000",
      USAGE_CUTOFF_BYTES: "700000000000"
    }, "r".repeat(22));
    await expect(configured.json()).resolves.toMatchObject({
      usageWarningBytes: 600_000_000_000,
      usageCutoffBytes: 700_000_000_000
    });

    const invalid = await createTurnResponse({
      ...environment,
      USAGE_WARNING_BYTES: "900000000000",
      USAGE_CUTOFF_BYTES: "800000000000"
    }, "r".repeat(22));
    expect(invalid.status).toBe(503);
    await expect(invalid.json()).resolves.toEqual({ code: "quota-configuration-invalid" });
  });
});

function mockFetch(usageBytes: number) {
  return vi.fn(async (input: string | URL | Request) => {
    if (String(input).includes("graphql")) {
      return Response.json({
        data: { viewer: { accounts: [{ callsTurnUsageAdaptiveGroups: [{ sum: { egressBytes: usageBytes, ingressBytes: 0 } }] }] } }
      });
    }
    return Response.json({
      iceServers: [{
        urls: ["turns:turn.cloudflare.com:443?transport=tcp", "turn:turn.cloudflare.com:3478?transport=udp"],
        username: "user",
        credential: "credential"
      }]
    });
  });
}
