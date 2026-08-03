import { encodeBase64Url } from "./base64url";
import { maximumControlMessageBytes } from "./constants";
import { parseHostHello, parseHostProof, verifyHostProof, type RelayHostHello } from "./routing";

export type HostAuthenticationResult =
  | { kind: "challenge"; hello: RelayHostHello; challenge: string; response: string }
  | { kind: "accepted"; response: string }
  | { kind: "rejected" };

export async function processHostAuthentication(
  routeId: string,
  text: string,
  pending?: { publicKey: string; challenge: string }
): Promise<HostAuthenticationResult> {
  if (new TextEncoder().encode(text).length > maximumControlMessageBytes) return { kind: "rejected" };
  let value: unknown;
  try { value = JSON.parse(text); } catch { return { kind: "rejected" }; }

  if (!pending) {
    const hello = parseHostHello(value, routeId);
    if (!hello || await (await import("./routing")).deriveRouteId(hello.publicKey) !== routeId) return { kind: "rejected" };
    const challenge = encodeBase64Url(crypto.getRandomValues(new Uint8Array(32)));
    return {
      kind: "challenge",
      hello,
      challenge,
      response: JSON.stringify({ type: "relay.host.challenge", challenge })
    };
  }

  const proof = parseHostProof(value);
  if (!proof || !await verifyHostProof(pending.publicKey, routeId, pending.challenge, proof.signature)) return { kind: "rejected" };
  return { kind: "accepted", response: JSON.stringify({ type: "relay.host.accepted", protocol: 1 }) };
}
