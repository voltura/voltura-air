import { isIpHost } from "../pairing/pcDisplayName";

export const activePcIdKey = "voltura-air.activePcId";
export const pcProfilesKey = "voltura-air.pcProfiles";

export interface PcProfile {
  customName: boolean;
  id: string;
  name: string;
  url: string;
}

export function createPcProfile(pcUrl: string): PcProfile {
  const url = new URL(pcUrl);
  const origin = url.origin;
  return {
    customName: false,
    id: origin,
    name: "PC",
    url: origin
  };
}

export function normalizePcProfile(value: unknown): PcProfile | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }

  const candidate = value as Partial<Record<keyof PcProfile, unknown>>;
  if (typeof candidate.url !== "string") {
    return null;
  }

  try {
    const profile = createPcProfile(candidate.url);
    const customName = candidate.customName === true;
    const name = typeof candidate.name === "string" && candidate.name.trim().length > 0 ? candidate.name : profile.name;
    return {
      ...profile,
      customName,
      name: customName || !isIpHost(name) ? name : profile.name
    };
  } catch {
    return null;
  }
}

export function loadPcProfiles(storage: Storage = localStorage): PcProfile[] {
  const stored = storage.getItem(pcProfilesKey);
  if (!stored) {
    return [];
  }

  try {
    const parsed: unknown = JSON.parse(stored);
    return Array.isArray(parsed) ? (parsed as unknown[]).map(normalizePcProfile).filter((pc): pc is PcProfile => pc !== null) : [];
  } catch {
    return [];
  }
}

export function savePcProfiles(profiles: PcProfile[], storage: Storage = localStorage): void {
  storage.setItem(pcProfilesKey, JSON.stringify(profiles));
}

export function loadActivePcId(storage: Storage = localStorage): string | null {
  const stored = storage.getItem(activePcIdKey);
  if (!stored) {
    return null;
  }

  try {
    return new URL(stored).origin;
  } catch {
    return stored;
  }
}

export function saveActivePcId(pcId: string | null, storage: Storage = localStorage): void {
  if (pcId) {
    storage.setItem(activePcIdKey, pcId);
  } else {
    storage.removeItem(activePcIdKey);
  }
}

export function getEffectiveStoredActivePcId(storedActivePcId: string | null, profiles: PcProfile[], addressPcId: string, source: string): string | null {
  if (!import.meta.env.DEV || storedActivePcId !== addressPcId || !isViteClientAddress(source)) {
    return storedActivePcId;
  }

  return profiles.find((profile) => profile.id !== addressPcId)?.id ?? storedActivePcId;
}

export function addPcProfile(profiles: PcProfile[], pcUrl: string): PcProfile[] {
  return upsertPcProfile(profiles, createPcProfile(pcUrl));
}

export function upsertPcProfile(profiles: PcProfile[], profile: PcProfile): PcProfile[] {
  const existing = profiles.find((pc) => pc.id === profile.id);
  if (!existing) {
    return [...profiles, profile];
  }

  return profiles.map((pc) => (pc.id === profile.id ? { ...profile, customName: pc.customName, name: pc.name } : pc));
}

export function selectPcProfile(profiles: PcProfile[], pcId: string): PcProfile | null {
  return profiles.find((pc) => pc.id === pcId) ?? null;
}

export function renamePcProfile(profiles: PcProfile[], pcId: string, name: string): PcProfile[] {
  return profiles.map((pc) =>
    pc.id === pcId
      ? {
          ...pc,
          customName: true,
          name
        }
      : pc
  );
}

export function forgetPcProfile(profiles: PcProfile[], activePcId: string | null, pcId: string): { profiles: PcProfile[]; activePcId: string | null } {
  return {
    profiles: profiles.filter((pc) => pc.id !== pcId),
    activePcId: activePcId === pcId ? null : activePcId
  };
}

export function applyPcNameFromHost(profiles: PcProfile[], pcId: string, pcName: string): PcProfile[] {
  const name = pcName.trim();
  if (!name) {
    return profiles;
  }

  let changed = false;
  const next = profiles.map((pc) => {
    if (pc.id !== pcId || pc.customName || pc.name === name) {
      return pc;
    }

    changed = true;
    return { ...pc, name };
  });

  return changed ? next : profiles;
}

export function getWebSocketUrl(pc: PcProfile): string {
  const url = new URL(pc.url);
  const protocol = url.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${url.host}/ws`;
}

function isViteClientAddress(source: string): boolean {
  try {
    return new URL(source).port === "5173";
  } catch {
    return false;
  }
}
