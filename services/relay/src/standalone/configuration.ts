import { isIPv4 } from "node:net";

const nonPublicIpv4Ranges: ReadonlyArray<readonly [number, number]> = [
  [0x00000000, 8], [0x0a000000, 8], [0x64400000, 10], [0x7f000000, 8],
  [0xa9fe0000, 16], [0xac100000, 12], [0xc0000000, 24], [0xc0000200, 24],
  [0xc0a80000, 16], [0xc6120000, 15], [0xc6336400, 24], [0xcb007100, 24],
  [0xe0000000, 4], [0xf0000000, 4]
];

export function validateOptionalTurnPublicIp(value: string | undefined): void {
  if (value === undefined) return;
  if (!isIPv4(value)) throw new Error("TURN_PUBLIC_IP must be a public IPv4 address.");
  const numeric = value.split(".").reduce((result, octet) => (result * 256 + Number(octet)) >>> 0, 0);
  if (nonPublicIpv4Ranges.some(([network, prefix]) =>
    (numeric & (0xffffffff << (32 - prefix))) >>> 0 === network)) {
    throw new Error("TURN_PUBLIC_IP must be a public IPv4 address.");
  }
}

export function parsePort(value: string | undefined): number {
  const configured = value ?? "8787";
  if (!/^\d{1,5}$/u.test(configured)) throw new Error("RELAY_PORT must be a valid port.");
  const parsed = Number.parseInt(configured, 10);
  if (parsed < 1 || parsed > 65535) throw new Error("RELAY_PORT must be a valid port.");
  return parsed;
}
