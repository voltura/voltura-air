import { afterEach, describe, expect, it, vi } from "vitest";
import { createTurnResponse, type TurnEnvironment } from "../src/cloudflare/turn";

const environment: TurnEnvironment = {
  TURN_KEY_ID: "turn-key",
  TURN_API_TOKEN: "turn-token",
  CLOUDFLARE_ACCOUNT_ID: "account",
  CLOUDFLARE_ANALYTICS_TOKEN: "analytics-token",
  USAGE_WARNING_BYTES: "750000000000",
  USAGE_CUTOFF_BYTES: "850000000000",
};
const routeId = "r".repeat(22);
const ownsRoute = () => true;

describe("Cloudflare TURN quota policy", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("issues bounded 15-minute credentials below the warning", async () => {
    const fetch = mockFetch(100_000_000_000);
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute);
    const body = (await response.json()) as {
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
      usageCutoffBytes: 850_000_000_000,
    });
    expect(body.iceServers).toHaveLength(1);
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(JSON.parse(String(fetch.mock.calls[1]?.[1]?.body))).toMatchObject({ ttl: 15 * 60 });
  });

  it("skips credential issuance if the host retires during analytics", async () => {
    let hostConnected = true;
    const fetch = mockFetch(0).mockImplementationOnce(async () => {
      hostConnected = false;
      return Response.json(analyticsResponse([]));
    });
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, () => hostConnected);

    expect(response.status).toBe(503);
    await expect(response.text()).resolves.toBe("Host unavailable");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("does not return credentials if the host retires during issuance", async () => {
    let hostConnected = true;
    const fetch = mockFetch(0, async () => {
      hostConnected = false;
      return validCredentialResponse();
    });
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, () => hostConnected);

    expect(response.status).toBe(503);
    await expect(response.text()).resolves.toBe("Host unavailable");
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("issues 60-minute credentials only for file transfer and keeps quota accounting", async () => {
    const fetch = mockFetch(100_000_000_000);
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute, "file-transfer");
    const body = (await response.json()) as {
      allowed: boolean;
      forcedQuality: string | null;
      expiresAt: string;
    };

    expect(body).toMatchObject({ allowed: true, forcedQuality: null });
    expect(JSON.parse(String(fetch.mock.calls[1]?.[1]?.body))).toMatchObject({ ttl: 60 * 60 });
    expect(new Date(body.expiresAt).getTime() - Date.now()).toBeGreaterThan(59 * 60 * 1000);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("issues 60-minute credentials for terminal transport", async () => {
    const fetch = mockFetch(100_000_000_000);
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute, "terminal");

    expect(response.status).toBe(200);
    expect(JSON.parse(String(fetch.mock.calls[1]?.[1]?.body))).toMatchObject({ ttl: 60 * 60 });
  });

  it("forces Data Saver at the warning threshold", async () => {
    vi.stubGlobal("fetch", mockFetch(750_000_000_000));
    const response = await createTurnResponse(environment, routeId, ownsRoute);
    await expect(response.json()).resolves.toMatchObject({
      allowed: true,
      forcedQuality: "data-saver",
    });
  });

  it("blocks credentials at the cutoff", async () => {
    const fetch = mockFetch(850_000_000_000);
    vi.stubGlobal("fetch", fetch);
    const response = await createTurnResponse(environment, routeId, ownsRoute);

    expect(response.status).toBe(429);
    await expect(response.json()).resolves.toMatchObject({
      allowed: false,
      code: "quota-blocked",
      usageWarningBytes: 750_000_000_000,
      usageCutoffBytes: 850_000_000_000,
    });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it.each([
    { data: { viewer: { accounts: [] } } },
    { data: { viewer: { accounts: [{}] } } },
    analyticsResponse([{ sum: { egressBytes: "100", ingressBytes: 0 } }]),
    analyticsResponse([{ sum: { egressBytes: 100 } }]),
  ])("fails closed when the provider returns incomplete usage data", async (usage) => {
    const fetch = mockFetch(0).mockResolvedValueOnce(Response.json(usage));
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute);

    expect(response.status).toBe(503);
    await expect(response.json()).resolves.toEqual({ code: "usage-unavailable" });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("treats the provider's valid empty usage result as zero", async () => {
    vi.stubGlobal(
      "fetch",
      mockFetch(0).mockResolvedValueOnce(Response.json(analyticsResponse([]))),
    );

    const response = await createTurnResponse(environment, routeId, ownsRoute);

    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({ usageBytes: 0, allowed: true });
  });

  it("returns a service response when the credentials request fails", async () => {
    const fetch = mockFetch(0, async () => {
      throw new Error("Network unavailable");
    });
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute);

    expect(response.status).toBe(503);
    await expect(response.json()).resolves.toEqual({ code: "credential-unavailable" });
  });

  it.each([
    new Response("not JSON"),
    Response.json({ iceServers: [null] }),
    Response.json({
      iceServers: [{ username: "user", credential: "credential", urls: "invalid" }],
    }),
  ])(
    "returns a service response when the provider's credential payload is invalid",
    async (providerResponse) => {
      vi.stubGlobal(
        "fetch",
        mockFetch(0, async () => providerResponse),
      );

      const response = await createTurnResponse(environment, routeId, ownsRoute);

      expect(response.status).toBe(503);
      await expect(response.json()).resolves.toEqual({ code: "credential-invalid" });
    },
  );

  it("returns a parseable credential when the provider includes unsupported ICE servers", async () => {
    const credentials = {
      urls: ["turns:turn.cloudflare.com:443?transport=tcp"],
      username: "user",
      credential: "credential",
    };
    const providerResponse = Response.json({
      iceServers: [
        { ...credentials, urls: ["turn:turn.cloudflare.com:80?transport=tcp"] },
        { ...credentials, urls: Array(10).fill(credentials.urls[0]) },
      ],
    });
    vi.stubGlobal(
      "fetch",
      mockFetch(0, async () => providerResponse),
    );

    const response = await createTurnResponse(environment, routeId, ownsRoute);

    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({ iceServers: [credentials] });
  });

  it("does not extend the reported credential lifetime by the provider response time", async () => {
    const startedAt = new Date("2026-11-28T12:00:00.000Z");
    vi.setSystemTime(startedAt);
    const fetch = mockFetch(0, async () => {
      vi.setSystemTime(new Date(startedAt.getTime() + 30_000));
      return validCredentialResponse();
    });
    vi.stubGlobal("fetch", fetch);

    const response = await createTurnResponse(environment, routeId, ownsRoute);

    await expect(response.json()).resolves.toMatchObject({ expiresAt: "2026-11-28T12:15:00.000Z" });
  });

  it("publishes valid configured limits and fails safe for invalid ordering", async () => {
    vi.stubGlobal("fetch", mockFetch(100_000_000_000));
    const configured = await createTurnResponse(
      {
        ...environment,
        USAGE_WARNING_BYTES: "600000000000",
        USAGE_CUTOFF_BYTES: "700000000000",
      },
      routeId,
      ownsRoute,
    );
    await expect(configured.json()).resolves.toMatchObject({
      usageWarningBytes: 600_000_000_000,
      usageCutoffBytes: 700_000_000_000,
    });

    const invalid = await createTurnResponse(
      {
        ...environment,
        USAGE_WARNING_BYTES: "900000000000",
        USAGE_CUTOFF_BYTES: "800000000000",
      },
      routeId,
      ownsRoute,
    );
    expect(invalid.status).toBe(503);
    await expect(invalid.json()).resolves.toEqual({ code: "quota-configuration-invalid" });
  });
});

function mockFetch(usageBytes: number, fetchCredential = async () => validCredentialResponse()) {
  return vi.fn(async (input: string | URL | Request, _init?: RequestInit) => {
    if (String(input).includes("graphql")) {
      return Response.json(
        analyticsResponse([{ sum: { egressBytes: usageBytes, ingressBytes: 0 } }]),
      );
    }
    return fetchCredential();
  });
}

function validCredentialResponse() {
  return Response.json({
    iceServers: [
      {
        urls: [
          "turns:turn.cloudflare.com:443?transport=tcp",
          "turn:turn.cloudflare.com:3478?transport=udp",
        ],
        username: "user",
        credential: "credential",
      },
    ],
  });
}

function analyticsResponse(groups: unknown[]) {
  return {
    data: { viewer: { accounts: [{ callsTurnUsageAdaptiveGroups: groups }] } },
    errors: null,
  };
}
