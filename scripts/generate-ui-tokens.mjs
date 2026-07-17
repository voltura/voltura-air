import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, "..");
const sourcePath = path.join(repositoryRoot, "assets", "ui-tokens.json");
const cssPath = path.join(repositoryRoot, "apps", "mobile-web", "src", "styles", "generated", "tokens.css");
const typescriptPath = path.join(repositoryRoot, "apps", "mobile-web", "src", "ui", "tokens.g.ts");
const xamlPath = path.join(repositoryRoot, "apps", "windows-host", "Styles", "Generated", "UiTokens.xaml");
const csharpPath = path.join(repositoryRoot, "apps", "windows-host", "UiTokens.g.cs");
const checkOnly = process.argv.includes("--check");

const source = JSON.parse(await readFile(sourcePath, "utf8"));

const toKebabCase = (value) => value.replaceAll(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
const toPascalCase = (value) => value.split(/[^a-zA-Z0-9]+/).map((part) => `${part[0]?.toUpperCase() ?? ""}${part.slice(1)}`).join("");
const cssColor = (value) => {
  if (/^#[0-9A-Fa-f]{8}$/.test(value)) {
    const alpha = Number.parseInt(value.slice(1, 3), 16) / 255;
    const red = Number.parseInt(value.slice(3, 5), 16);
    const green = Number.parseInt(value.slice(5, 7), 16);
    const blue = Number.parseInt(value.slice(7, 9), 16);
    return `rgb(${red} ${green} ${blue} / ${Math.round(alpha * 100)}%)`;
  }

  return value;
};

function renderCssTheme(theme) {
  return Object.entries(theme).map(([name, value]) => `  --${toKebabCase(name)}: ${cssColor(value)};`).join("\n");
}

function renderCssDimensions(group, suffix, prefix) {
  const tokenPrefix = prefix.length > 0 ? `${prefix}-` : "";
  return Object.entries(group).map(([name, value]) => `  --${tokenPrefix}${toKebabCase(name)}: ${value}${suffix};`).join("\n");
}

const css = `/* Generated from assets/ui-tokens.json. Do not edit directly. */
:root {
  color-scheme: dark;
${renderCssTheme(source.color.dark)}
${renderCssDimensions(source.space, "px", "space")}
${renderCssDimensions(source.radius, "px", "radius")}
${renderCssDimensions(source.size, "px", "")}
${renderCssDimensions(source.duration, "ms", "motion")}
}

@media (prefers-color-scheme: light) {
  :root:not([data-theme="dark"]) {
    color-scheme: light;
${renderCssTheme(source.color.light)}
  }
}

:root[data-theme="light"] {
  color-scheme: light;
${renderCssTheme(source.color.light)}
}
`;

const typescript = `// Generated from assets/ui-tokens.json. Do not edit directly.
export const uiThemeColors = {
  dark: { background: "${source.color.dark.bg}" },
  light: { background: "${source.color.light.bg}" }
} as const;
`;

const xamlNumbers = [
  ...Object.entries(source.space).map(([name, value]) => `    <system:Double x:Key="Space${toPascalCase(name)}">${value}</system:Double>`),
  ...Object.entries(source.size).map(([name, value]) => `    <system:Double x:Key="${toPascalCase(name)}">${value}</system:Double>`)
].join("\n");
const xamlInsets = Object.entries(source.space)
  .map(([name, value]) => `    <Thickness x:Key="Inset${toPascalCase(name)}">${value}</Thickness>`)
  .join("\n");
const xamlSizeInsets = Object.entries(source.size)
  .map(([name, value]) => `    <Thickness x:Key="${toPascalCase(name)}Inset">${value}</Thickness>`)
  .join("\n");
const xamlGridLengths = Object.entries(source.space)
  .map(([name, value]) => `    <GridLength x:Key="GridSpace${toPascalCase(name)}">${value}</GridLength>`)
  .join("\n");
const xamlRadii = Object.entries(source.radius)
  .map(([name, value]) => `    <CornerRadius x:Key="Radius${toPascalCase(name)}">${value}</CornerRadius>`)
  .join("\n");

const xaml = `<!-- Generated from assets/ui-tokens.json. Do not edit directly. -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:system="clr-namespace:System;assembly=System.Runtime">
${xamlNumbers}
${xamlInsets}
${xamlSizeInsets}
${xamlGridLengths}
${xamlRadii}
</ResourceDictionary>
`;

function renderColor(value) {
  const normalized = value.slice(1);
  const rgb = normalized.length === 8 ? normalized.slice(2) : normalized;
  const red = Number.parseInt(rgb.slice(0, 2), 16);
  const green = Number.parseInt(rgb.slice(2, 4), 16);
  const blue = Number.parseInt(rgb.slice(4, 6), 16);
  return `System.Drawing.Color.FromArgb(${red}, ${green}, ${blue})`;
}

function renderPalette(name, theme) {
  return `    public static ThemePalette ${name} { get; } = new(
        ${name === "DarkPalette" ? "true" : "false"},
        ${renderColor(theme.bg)},
        ${renderColor(theme.surface)},
        ${renderColor(theme.surfaceRaised)},
        ${renderColor(theme.text)},
        ${renderColor(theme.muted)},
        ${renderColor(theme.border)},
        ${renderColor(theme.accent)},
        ${renderColor(theme.onAccent)},
        ${renderColor(theme.danger)},
        ${renderColor(theme.qrBackground)});`;
}

const csharp = `// <auto-generated />
// Generated from assets/ui-tokens.json. Do not edit directly.
namespace VolturaAir.Host;

internal static class UiTokens
{
${Object.entries(source.space)
  .map(([name, value]) => `    public const double Space${toPascalCase(name)} = ${value}d;`)
  .join("\n")}

${renderPalette("DarkPalette", source.color.dark)}

${renderPalette("LightPalette", source.color.light)}
}
`;

async function updateGeneratedFile(targetPath, contents) {
  let current = null;
  try {
    current = await readFile(targetPath, "utf8");
  } catch (error) {
    if (error.code !== "ENOENT") {
      throw error;
    }
  }

  if (current === contents) {
    return false;
  }

  if (checkOnly) {
    throw new Error(`${path.relative(repositoryRoot, targetPath)} is stale. Run npm run ui:tokens:generate.`);
  }

  await mkdir(path.dirname(targetPath), { recursive: true });
  await writeFile(targetPath, contents, "utf8");
  return true;
}

const changed = await Promise.all([
  updateGeneratedFile(cssPath, css),
  updateGeneratedFile(typescriptPath, typescript),
  updateGeneratedFile(xamlPath, xaml),
  updateGeneratedFile(csharpPath, csharp)
]);

if (!checkOnly && changed.some(Boolean)) {
  process.stdout.write("Generated shared React and WPF UI tokens.\n");
}
