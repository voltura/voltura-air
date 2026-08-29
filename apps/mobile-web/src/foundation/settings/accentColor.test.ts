import { beforeEach, describe, expect, it } from "vitest";
import { themeSurfaceColors } from "./themeColors.g";
import {
  applyAccentColor,
  applyCachedAccentColor,
  loadCachedAccentColor,
  normalizeAccentColor,
} from "./accentColor";
import { contrastRatio, createAccentPalette } from "./accentPalette";

describe("accent color", () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.removeAttribute("style");
  });

  it("accepts only canonical uppercase wire colors", () => {
    expect(normalizeAccentColor("#5FC8B4")).toBe("#5FC8B4");
    expect(normalizeAccentColor("#5fc8b4")).toBeNull();
    expect(normalizeAccentColor("5FC8B4")).toBeNull();
    expect(normalizeAccentColor("#FFFF")).toBeNull();
  });

  it.each(["#000000", "#FFFFFF", "#808080", "#FFFF00", "#FF0000", "#00FF00", "#0000FF"])(
    "derives accessible roles for %s in both themes",
    (seed) => {
      for (const theme of ["light", "dark"] as const) {
        const palette = createAccentPalette(seed, theme);
        for (const surface of Object.values(themeSurfaceColors[theme])) {
          expect(contrastRatio(palette.accent, surface)).toBeGreaterThanOrEqual(4.5);
          expect(contrastRatio(palette.accentStrong, surface)).toBeGreaterThanOrEqual(4.5);
        }
        expect(contrastRatio(palette.accent, palette.onAccent)).toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(palette.action, palette.onAction)).toBeGreaterThanOrEqual(4.5);
      }
    },
  );

  it("persists the effective palette and removes it on reset", async () => {
    applyAccentColor("#5FC8B4", "dark");
    await import("./accentPalette");
    await Promise.resolve();

    expect(loadCachedAccentColor()).toBe("#5FC8B4");
    expect(document.documentElement.style.getPropertyValue("--action")).toBe("#5FC8B4");

    applyAccentColor(null, "dark");
    expect(loadCachedAccentColor()).toBeNull();
    expect(document.documentElement.style.getPropertyValue("--action")).toBe("");
  });

  it("drops malformed cached palettes", () => {
    window.localStorage.setItem("voltura-air.accentPalette.v1", "{not-json");
    expect(loadCachedAccentColor()).toBeNull();
    expect(window.localStorage.getItem("voltura-air.accentPalette.v1")).toBeNull();
  });

  it("does not let a pending host palette override a newer cache fallback", async () => {
    applyAccentColor("#000000", "dark");
    applyCachedAccentColor("dark");
    await import("./accentPalette");
    await Promise.resolve();

    expect(loadCachedAccentColor()).toBeNull();
    expect(document.documentElement.style.getPropertyValue("--action")).toBe("");
  });
});
