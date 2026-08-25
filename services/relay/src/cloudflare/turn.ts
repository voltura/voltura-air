export interface TurnEnvironment {
  TURN_KEY_ID: string;
  TURN_API_TOKEN: string;
  CLOUDFLARE_ACCOUNT_ID: string;
  CLOUDFLARE_ANALYTICS_TOKEN: string;
  USAGE_WARNING_BYTES: string;
  USAGE_CUTOFF_BYTES: string;
}

const mediaCredentialTtlSeconds = 15 * 60;
const fileTransferCredentialTtlSeconds = 60 * 60;

export async function createTurnResponse(env: TurnEnvironment, routeId: string, purpose?: "file-transfer"): Promise<Response> {
  const credentialTtlSeconds = purpose === "file-transfer" ? fileTransferCredentialTtlSeconds : mediaCredentialTtlSeconds;
  const limits = resolveUsageLimits(env);
  if (!limits) return Response.json({ code: "quota-configuration-invalid" }, { status: 503 });
  const { warningBytes, cutoffBytes } = limits;
  let usageBytes: number;
  try { usageBytes = await readMonthlyTurnUsage(env); }
  catch { return Response.json({ code: "usage-unavailable" }, { status: 503 }); }
  const checkedAt = new Date().toISOString();
  if (usageBytes >= cutoffBytes) {
    return Response.json({
      code: "quota-blocked",
      allowed: false,
      usageBytes,
      checkedAt,
      usageWarningBytes: warningBytes,
      usageCutoffBytes: cutoffBytes
    }, { status: 429 });
  }

  const response = await fetch(`https://rtc.live.cloudflare.com/v1/turn/keys/${env.TURN_KEY_ID}/credentials/generate-ice-servers`, {
    method: "POST",
    headers: { Authorization: `Bearer ${env.TURN_API_TOKEN}`, "Content-Type": "application/json" },
    body: JSON.stringify({ ttl: credentialTtlSeconds, customIdentifier: routeId })
  });
  if (!response.ok) return Response.json({ code: "credential-unavailable" }, { status: 503 });
  const payload = await response.json() as { iceServers?: Array<{ urls?: string[]; username?: string; credential?: string }> };
  const turn = payload.iceServers?.find((server) => server.username && server.credential);
  const urls = turn?.urls?.filter((url) => url === "turns:turn.cloudflare.com:443?transport=tcp" || url === "turn:turn.cloudflare.com:3478?transport=udp") ?? [];
  if (!turn?.username || !turn.credential || urls.length === 0) return Response.json({ code: "credential-invalid" }, { status: 503 });
  return Response.json({
    allowed: true,
    forcedQuality: usageBytes >= warningBytes ? "data-saver" : null,
    usageBytes,
    checkedAt,
    usageWarningBytes: warningBytes,
    usageCutoffBytes: cutoffBytes,
    expiresAt: new Date(Date.now() + credentialTtlSeconds * 1000).toISOString(),
    iceServers: [{ urls, username: turn.username, credential: turn.credential }]
  });
}

function resolveUsageLimits(env: TurnEnvironment): { warningBytes: number; cutoffBytes: number } | null {
  const warningBytes = parseUsageLimit(env.USAGE_WARNING_BYTES);
  const cutoffBytes = parseUsageLimit(env.USAGE_CUTOFF_BYTES);
  return warningBytes && cutoffBytes && warningBytes < cutoffBytes
    ? { warningBytes, cutoffBytes }
    : null;
}

function parseUsageLimit(value: string | undefined): number | null {
  if (!value || !/^\d{1,16}$/u.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

export async function readMonthlyTurnUsage(env: TurnEnvironment): Promise<number> {
  const now = new Date();
  const dateFrom = `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, "0")}-01`;
  const dateTo = now.toISOString().slice(0, 10);
  const query = `query Usage($accountId: String!, $dateFrom: Date!, $dateTo: Date!) {
    viewer { accounts(filter: { accountTag: $accountId }) {
      callsTurnUsageAdaptiveGroups(limit: 10000, filter: { date_geq: $dateFrom, date_leq: $dateTo, keyId: "${env.TURN_KEY_ID}" }) {
        sum { egressBytes ingressBytes }
      }
    } }
  }`;
  const response = await fetch("https://api.cloudflare.com/client/v4/graphql", {
    method: "POST",
    headers: { Authorization: `Bearer ${env.CLOUDFLARE_ANALYTICS_TOKEN}`, "Content-Type": "application/json" },
    body: JSON.stringify({ query, variables: { accountId: env.CLOUDFLARE_ACCOUNT_ID, dateFrom, dateTo } })
  });
  if (!response.ok) throw new Error("Analytics request failed.");
  const payload = await response.json() as {
    errors?: unknown[];
    data?: { viewer?: { accounts?: Array<{ callsTurnUsageAdaptiveGroups?: Array<{ sum?: { egressBytes?: number; ingressBytes?: number } }> }> } };
  };
  if (payload.errors?.length) throw new Error("Analytics response failed.");
  const groups = payload.data?.viewer?.accounts?.[0]?.callsTurnUsageAdaptiveGroups ?? [];
  return groups.reduce((total, group) => total + (group.sum?.egressBytes ?? 0) + (group.sum?.ingressBytes ?? 0), 0);
}
