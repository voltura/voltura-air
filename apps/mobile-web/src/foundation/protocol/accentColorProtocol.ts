const canonicalAccentColorPattern = /^#[0-9A-F]{6}$/;

export function normalizeAccentColor(value: unknown): string | null {
  return typeof value === "string" && canonicalAccentColorPattern.test(value) ? value : null;
}
