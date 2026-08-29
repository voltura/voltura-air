import { normalizeAccentColor, type AccentPalette, type ResolvedTheme } from "./accentColor";
import { themeSurfaceColors } from "./themeColors.g";

const minimumTextContrast = 4.5;

export function createAccentPalette(seed: string, theme: ResolvedTheme): AccentPalette {
  const normalized = normalizeAccentColor(seed);
  if (!normalized) {
    throw new Error("Accent color must use canonical #RRGGBB format.");
  }

  const surfaces = Object.values(themeSurfaceColors[theme]);
  const direction = theme === "light" ? "darker" : "lighter";
  const accent = ensureContrast(normalized, surfaces, direction);
  const seedHsl = rgbToHsl(parseHex(normalized));
  const strongCandidate = toHex(
    hslToRgb({
      ...seedHsl,
      lightness: clamp(seedHsl.lightness + (theme === "light" ? -0.08 : 0.08), 0, 1),
    }),
  );
  const onAccent = readableTextColor(accent);
  const onAction = readableTextColor(normalized);

  return {
    accent,
    accentStrong: ensureContrast(strongCandidate, surfaces, direction),
    action: normalized,
    onAccent,
    onAction,
    gradient: `${normalized}${theme === "dark" ? "3D" : "1F"}`,
  };
}

function readableTextColor(background: string): "#000000" | "#FFFFFF" {
  return contrastRatio(background, "#000000") >= minimumTextContrast ? "#000000" : "#FFFFFF";
}

export function contrastRatio(first: string, second: string): number {
  const lighter = Math.max(relativeLuminance(parseHex(first)), relativeLuminance(parseHex(second)));
  const darker = Math.min(relativeLuminance(parseHex(first)), relativeLuminance(parseHex(second)));
  return (lighter + 0.05) / (darker + 0.05);
}

function ensureContrast(
  candidate: string,
  backgrounds: readonly string[],
  direction: "darker" | "lighter",
): string {
  if (
    backgrounds.every((background) => contrastRatio(candidate, background) >= minimumTextContrast)
  ) {
    return candidate;
  }

  const hsl = rgbToHsl(parseHex(candidate));
  let failing = hsl.lightness;
  let passing = direction === "darker" ? 0 : 1;
  for (let index = 0; index < 24; index += 1) {
    const lightness = (failing + passing) / 2;
    const color = toHex(hslToRgb({ ...hsl, lightness }));
    if (
      backgrounds.every((background) => contrastRatio(color, background) >= minimumTextContrast)
    ) {
      passing = lightness;
    } else {
      failing = lightness;
    }
  }
  return toHex(hslToRgb({ ...hsl, lightness: passing }));
}

interface Rgb {
  red: number;
  green: number;
  blue: number;
}

interface Hsl {
  hue: number;
  saturation: number;
  lightness: number;
}

function parseHex(value: string): Rgb {
  return {
    red: Number.parseInt(value.slice(1, 3), 16),
    green: Number.parseInt(value.slice(3, 5), 16),
    blue: Number.parseInt(value.slice(5, 7), 16),
  };
}

function toHex({ red, green, blue }: Rgb): string {
  const channel = (value: number) =>
    Math.round(clamp(value, 0, 255))
      .toString(16)
      .padStart(2, "0");
  return `#${channel(red)}${channel(green)}${channel(blue)}`.toUpperCase();
}

function relativeLuminance({ red, green, blue }: Rgb): number {
  const linear = (channel: number) => {
    const value = channel / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue);
}

function rgbToHsl({ red, green, blue }: Rgb): Hsl {
  const r = red / 255;
  const g = green / 255;
  const b = blue / 255;
  const maximum = Math.max(r, g, b);
  const minimum = Math.min(r, g, b);
  const delta = maximum - minimum;
  const lightness = (maximum + minimum) / 2;
  if (delta === 0) {
    return { hue: 0, saturation: 0, lightness };
  }
  const saturation = delta / (1 - Math.abs(2 * lightness - 1));
  const hue =
    maximum === r
      ? 60 * (((g - b) / delta) % 6)
      : maximum === g
        ? 60 * ((b - r) / delta + 2)
        : 60 * ((r - g) / delta + 4);
  return { hue: hue < 0 ? hue + 360 : hue, saturation, lightness };
}

function hslToRgb({ hue, saturation, lightness }: Hsl): Rgb {
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const second = chroma * (1 - Math.abs(((hue / 60) % 2) - 1));
  const offset = lightness - chroma / 2;
  const [red, green, blue] =
    hue < 60
      ? [chroma, second, 0]
      : hue < 120
        ? [second, chroma, 0]
        : hue < 180
          ? [0, chroma, second]
          : hue < 240
            ? [0, second, chroma]
            : hue < 300
              ? [second, 0, chroma]
              : [chroma, 0, second];
  return { red: (red + offset) * 255, green: (green + offset) * 255, blue: (blue + offset) * 255 };
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}
