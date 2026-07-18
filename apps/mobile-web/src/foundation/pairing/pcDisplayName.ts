export interface PcDisplaySource {
  customName?: boolean;
  name: string;
  url: string;
}

export function getPcDisplayName(pc: PcDisplaySource): string {
  const name = pc.name.trim();
  if (pc.customName) {
    return name || "PC";
  }

  const host = new URL(pc.url).host;
  return name.length > 0 && name !== host && !isIpHost(name) ? name : "PC";
}

export function isIpHost(value: string): boolean {
  const hostName = value.split(":", 1)[0] ?? "";
  return /^(\d{1,3}\.){3}\d{1,3}$/.test(hostName);
}
