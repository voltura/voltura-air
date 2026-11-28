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

export async function createTurnResponse(
  env: TurnEnvironment,
  routeId: string,
  ownsRoute: () => boolean,
  purpose?: "file-transfer" | "terminal",
): Promise<Response> {
  if (!ownsRoute()) return new Response("Host unavailable", { status: 503 });
  const credentialTtlSeconds =
    purpose === "file-transfer" || purpose === "terminal"
      ? fileTransferCredentialTtlSeconds
      : mediaCredentialTtlSeconds;
  const limits = resolveUsageLimits(env);
  if (!limits) return Response.json({ code: "quota-configuration-invalid" }, { status: 503 });
  const { warningBytes, cutoffBytes } = limits;
  let usageBytes: number;
  try {
    usageBytes = await readMonthlyTurnUsage(env);
  } catch {
    return Response.json({ code: "usage-unavailable" }, { status: 503 });
  }
  if (!ownsRoute()) return new Response("Host unavailable", { status: 503 });
  const credentialRequestedAt = Date.now();
  const checkedAt = new Date(credentialRequestedAt).toISOString();
  if (usageBytes >= cutoffBytes) {
    return Response.json(
      {
        code: "quota-blocked",
        allowed: false,
        usageBytes,
        checkedAt,
        usageWarningBytes: warningBytes,
        usageCutoffBytes: cutoffBytes,
      },
      { status: 429 },
    );
  }

  const response = await fetch(
    `https://rtc.live.cloudflare.com/v1/turn/keys/${env.TURN_KEY_ID}/credentials/generate-ice-servers`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.TURN_API_TOKEN}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ ttl: credentialTtlSeconds, customIdentifier: routeId }),
    },
  ).catch(() => null);
  if (!response?.ok) return Response.json({ code: "credential-unavailable" }, { status: 503 });
  const payload: unknown = await response.json().catch(() => null);
  if (!ownsRoute()) return new Response("Host unavailable", { status: 503 });
  const turn = parseIceServer(payload);
  if (!turn) return Response.json({ code: "credential-invalid" }, { status: 503 });
  return Response.json({
    allowed: true,
    forcedQuality: usageBytes >= warningBytes ? "data-saver" : null,
    usageBytes,
    checkedAt,
    usageWarningBytes: warningBytes,
    usageCutoffBytes: cutoffBytes,
    expiresAt: new Date(credentialRequestedAt + credentialTtlSeconds * 1000).toISOString(),
    iceServers: [turn],
  });
}

function parseIceServer(payload: unknown) {
  if (!isRecord(payload) || !Array.isArray(payload.iceServers)) return null;
  for (const server of payload.iceServers) {
    if (!isRecord(server)) continue;
    const { urls, username, credential } = server;
    if (
      !Array.isArray(urls) ||
      typeof username !== "string" ||
      username.length === 0 ||
      username.length > 512 ||
      typeof credential !== "string" ||
      credential.length === 0 ||
      credential.length > 512
    )
      continue;
    const supportedUrls = new Set(
      urls.filter(
        (url) =>
          url === "turns:turn.cloudflare.com:443?transport=tcp" ||
          url === "turn:turn.cloudflare.com:3478?transport=udp",
      ),
    );
    if (supportedUrls.size > 0) return { urls: [...supportedUrls], username, credential };
  }
  return null;
}

function resolveUsageLimits(
  env: TurnEnvironment,
): { warningBytes: number; cutoffBytes: number } | null {
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
    headers: {
      Authorization: `Bearer ${env.CLOUDFLARE_ANALYTICS_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      query,
      variables: { accountId: env.CLOUDFLARE_ACCOUNT_ID, dateFrom, dateTo },
    }),
  });
  if (!response.ok) throw new Error("Analytics request failed.");
  const payload = (await response.json()) as {
    errors?: unknown;
    data?: {
      viewer?: {
        accounts?: Array<{
          callsTurnUsageAdaptiveGroups?: Array<{
            sum?: { egressBytes?: unknown; ingressBytes?: unknown };
          }>;
        }>;
      };
    };
  } | null;
  const errors = payload?.errors;
  if (errors != null && (!Array.isArray(errors) || errors.length > 0))
    throw new Error("Analytics response failed.");
  const accounts = payload?.data?.viewer?.accounts;
  const groups =
    Array.isArray(accounts) && accounts.length === 1
      ? accounts[0]?.callsTurnUsageAdaptiveGroups
      : undefined;
  if (!Array.isArray(groups)) throw new Error("Analytics totals are unavailable.");
  let total = 0;
  for (const group of groups) {
    const egress = group?.sum?.egressBytes;
    const ingress = group?.sum?.ingressBytes;
    if (!isUsageBytes(egress) || !isUsageBytes(ingress))
      throw new Error("Analytics totals are invalid.");
    total += egress + ingress;
  }
  if (!Number.isSafeInteger(total)) throw new Error("Analytics totals are out of range.");
  return total;
}

function isUsageBytes(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
