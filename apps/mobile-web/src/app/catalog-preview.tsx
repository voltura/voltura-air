import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { CustomScreenWorkspace } from "../features/custom-screens";
import { defaultTrackpadSettings } from "../foundation/input/gestures";
import type {
  CustomScreenButtonDefinition,
  CustomScreenDefinition,
  CustomScreenLayoutOverride,
  CustomScreenSectionDefinition,
} from "../foundation/protocol/messages";
import "../styles.css";

type JsonObject = Record<string, unknown>;

const source = JSON.parse(
  document.getElementById("catalog-screen-package")?.textContent ?? "null",
) as unknown;
const definition = projectPackage(source);
const ignorePreviewAction = () => undefined;

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <div className="app-frame custom-screen-browser-preview control-depth">
      <CustomScreenWorkspace
        audioState={{ type: "audio.state", volume: 50, muted: false }}
        definition={definition}
        invoke={ignorePreviewAction}
        onBack={ignorePreviewAction}
        pendingButtonIds={new Set()}
        requestedName="Custom screen preview"
        send={ignorePreviewAction}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />
      <div className="custom-screen-preview-notice" role="status">
        Preview only · actions are disabled
      </div>
    </div>
  </StrictMode>,
);

function projectPackage(packageValue: unknown): CustomScreenDefinition | null {
  const packageObject = object(packageValue);
  const screen = object(value(packageObject, "screen"));
  if (!screen) {
    return null;
  }
  const sections = array(value(screen, "sections"));
  return {
    id: text(value(screen, "id"), "catalog-preview"),
    name: text(value(screen, "name"), "Custom screen preview"),
    revision: text(value(screen, "revision"), "catalog-preview"),
    orientationLayoutsEnabled: boolean(value(screen, "orientationLayoutsEnabled"), false),
    showNavigationHeader: boolean(value(screen, "showNavigationHeader"), false),
    sections: sections
      .map(projectSection)
      .filter((section): section is CustomScreenSectionDefinition => section !== null),
  };
}

function projectSection(source: unknown, index: number): CustomScreenSectionDefinition | null {
  const section = object(source);
  if (!section) {
    return null;
  }
  const sourceKind = text(value(section, "kind"), "buttons");
  const kind = sourceKind === "collapsible" ? "buttons" : sourceKind;
  const supportedKind = ["buttons", "trackpad", "volume", "navigationRing", "dpad"].includes(kind)
    ? (kind as CustomScreenSectionDefinition["kind"])
    : "buttons";
  return {
    id: text(value(section, "id"), `section-${index}`),
    name: text(value(section, "name"), "Controls"),
    showHeader: boolean(value(section, "showHeader"), true),
    widthColumns: integer(value(section, "widthColumns"), 12),
    heightMode: value(section, "heightMode") === "fill" ? "fill" : "content",
    fillWeight: integer(value(section, "fillWeight"), 1),
    rowLimit: integer(value(section, "rowLimit"), 0),
    buttonAlignment: buttonAlignment(value(section, "buttonAlignment")),
    kind: supportedKind,
    collapsible: sourceKind === "collapsible",
    initiallyExpanded: boolean(value(section, "initiallyExpanded"), true),
    trackpadLeftClick: boolean(value(section, "trackpadLeftClick"), true),
    trackpadRightClick: boolean(value(section, "trackpadRightClick"), true),
    trackpadButtonSide: value(section, "trackpadButtonSide") === "left" ? "left" : "right",
    trackpadFullscreenControl: boolean(value(section, "trackpadFullscreenControl"), false),
    trackpadGyroControl: boolean(value(section, "trackpadGyroControl"), false),
    trackpadEnabled: true,
    volumeEnabled: true,
    portrait: projectLayout(value(section, "portrait")),
    landscape: projectLayout(value(section, "landscape")),
    buttons: array(value(section, "buttons"))
      .map(projectButton)
      .filter((button): button is CustomScreenButtonDefinition => button !== null),
  };
}

function projectButton(source: unknown, index: number): CustomScreenButtonDefinition | null {
  const button = object(source);
  if (!button) {
    return null;
  }
  const presentationValue = value(button, "presentation");
  const sizeValue = value(button, "size");
  return {
    id: text(value(button, "id"), `button-${index}`),
    name: text(value(button, "name"), "Button"),
    label: text(value(button, "label"), "Button"),
    icon: text(value(button, "icon"), "command"),
    presentation:
      presentationValue === "icon" || presentationValue === "label"
        ? presentationValue
        : "iconLabel",
    size:
      sizeValue === "compact" || sizeValue === "wide" || sizeValue === "fill"
        ? sizeValue
        : "standard",
    repeat: boolean(value(button, "repeat"), false),
    row: integer(value(button, "row"), 0),
    portrait: projectLayout(value(button, "portrait")),
    landscape: projectLayout(value(button, "landscape")),
    enabled: true,
    ...(isLaserPointerColor(value(button, "laserPointerColor"))
      ? {
          laserPointerColor: value(button, "laserPointerColor") as
            | "default"
            | "red"
            | "green"
            | "blue",
        }
      : {}),
  };
}

function isLaserPointerColor(candidate: unknown): boolean {
  return (
    candidate === "default" || candidate === "red" || candidate === "green" || candidate === "blue"
  );
}

function projectLayout(source: unknown): CustomScreenLayoutOverride | null {
  const layout = object(source);
  if (!layout) {
    return null;
  }
  const width = value(layout, "widthColumns");
  const size = value(layout, "size");
  const row = value(layout, "row");
  return {
    order: integer(value(layout, "order"), 0),
    visible: boolean(value(layout, "visible"), true),
    widthColumns: typeof width === "number" ? width : null,
    size:
      size === "compact" || size === "standard" || size === "wide" || size === "fill" ? size : null,
    row: typeof row === "number" ? row : null,
  };
}

function object(source: unknown): JsonObject | null {
  return source !== null && typeof source === "object" && !Array.isArray(source)
    ? (source as JsonObject)
    : null;
}

function array(source: unknown): unknown[] {
  return Array.isArray(source) ? source : [];
}

function value(source: JsonObject | null, key: string): unknown {
  if (!source) {
    return undefined;
  }
  return source[key];
}

function text(source: unknown, fallback: string): string {
  return typeof source === "string" && source.length > 0 ? source : fallback;
}

function integer(source: unknown, fallback: number): number {
  return typeof source === "number" && Number.isInteger(source) ? source : fallback;
}

function boolean(source: unknown, fallback: boolean): boolean {
  return typeof source === "boolean" ? source : fallback;
}

function buttonAlignment(source: unknown): CustomScreenSectionDefinition["buttonAlignment"] {
  return ["start", "center", "end", "space-between", "space-around", "space-evenly"].includes(
    String(source),
  )
    ? (source as CustomScreenSectionDefinition["buttonAlignment"])
    : "start";
}
