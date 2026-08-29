import {
  readLocalStorage,
  removeLocalStorage,
  writeLocalStorage,
} from "../platform/browserStorage";
import { normalizeAccentColor } from "../protocol/accentColorProtocol";

export { normalizeAccentColor } from "../protocol/accentColorProtocol";

export type ResolvedTheme = "light" | "dark";

export interface AccentPalette {
  accent: string;
  accentStrong: string;
  action: string;
  onAccent: string;
  onAction: string;
  gradient: string;
}

interface CachedAccentPalette {
  seed: string;
  dark: AccentPalette;
  light: AccentPalette;
}

const accentCacheKey = "voltura-air.accentPalette.v1";
const runtimeProperties = [
  "--accent",
  "--accent-strong",
  "--action",
  "--on-accent",
  "--on-action",
  "--accent-contrast",
  "--app-gradient-primary",
] as const;
let applyRevision = 0;

export function applyAccentColor(seed: string | null, theme: ResolvedTheme): void {
  const revision = ++applyRevision;
  if (!seed) {
    clearRuntimePalette();
    removeLocalStorage(accentCacheKey);
    return;
  }

  const normalized = normalizeAccentColor(seed);
  if (!normalized) {
    return;
  }

  void import("./accentPalette").then(({ createAccentPalette }) => {
    if (revision !== applyRevision) {
      return;
    }
    const cache: CachedAccentPalette = {
      seed: normalized,
      dark: createAccentPalette(normalized, "dark"),
      light: createAccentPalette(normalized, "light"),
    };
    writeLocalStorage(accentCacheKey, JSON.stringify(cache));
    applyPalette(cache[theme]);
  });
}

export function loadCachedAccentColor(): string | null {
  return readCachedPalette()?.seed ?? null;
}

export function applyCachedAccentColor(theme: ResolvedTheme): string | null {
  applyRevision += 1;
  const cached = readCachedPalette();
  if (!cached) {
    clearRuntimePalette();
    return null;
  }

  applyPalette(cached[theme]);
  return cached.seed;
}

function readCachedPalette(): CachedAccentPalette | null {
  const stored = readLocalStorage(accentCacheKey);
  if (!stored) {
    return null;
  }

  try {
    const candidate = JSON.parse(stored) as Partial<CachedAccentPalette>;
    const seed = normalizeAccentColor(candidate.seed);
    if (!seed || !isPalette(candidate.dark) || !isPalette(candidate.light)) {
      removeLocalStorage(accentCacheKey);
      return null;
    }
    return { seed, dark: candidate.dark, light: candidate.light };
  } catch {
    removeLocalStorage(accentCacheKey);
    return null;
  }
}

function isPalette(value: unknown): value is AccentPalette {
  if (!value || typeof value !== "object") {
    return false;
  }
  const palette = value as Partial<AccentPalette>;
  return (
    [
      palette.accent,
      palette.accentStrong,
      palette.action,
      palette.onAccent,
      palette.onAction,
    ].every((color) => normalizeAccentColor(color) !== null) &&
    typeof palette.gradient === "string" &&
    /^#[0-9A-F]{8}$/.test(palette.gradient)
  );
}

function applyPalette(palette: AccentPalette): void {
  const root = document.documentElement.style;
  root.setProperty("--accent", palette.accent);
  root.setProperty("--accent-strong", palette.accentStrong);
  root.setProperty("--action", palette.action);
  root.setProperty("--on-accent", palette.onAccent);
  root.setProperty("--on-action", palette.onAction);
  root.setProperty("--accent-contrast", palette.onAccent);
  root.setProperty("--app-gradient-primary", palette.gradient);
}

function clearRuntimePalette(): void {
  for (const property of runtimeProperties) {
    document.documentElement.style.removeProperty(property);
  }
}
