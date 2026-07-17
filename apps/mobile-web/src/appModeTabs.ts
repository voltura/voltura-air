import { ClipboardPaste, Keyboard, Mic, MousePointer2, Presentation as PresentationIcon, Send, Tv } from "lucide-react";

export type PrimaryAppTab = "trackpad" | "keyboard" | "remote";
export type ToolAppTab = "presentation" | "dictation" | "text-transfer" | "clipboard-read";
export type FourthMode = ToolAppTab;
export type AppTab = PrimaryAppTab | ToolAppTab | "debug";
export type MainAppTab = Exclude<AppTab, "debug">;

export interface ModeDefinition {
  id: MainAppTab;
  label: string;
  ariaLabel: string;
  Icon: typeof MousePointer2;
}

export const primaryModeDefinitions: ModeDefinition[] = [
  { id: "trackpad", label: "Trackpad", ariaLabel: "Trackpad mode", Icon: MousePointer2 },
  { id: "keyboard", label: "Keyboard", ariaLabel: "Keyboard mode", Icon: Keyboard },
  { id: "remote", label: "Remote", ariaLabel: "Remote mode", Icon: Tv }
];

export const toolModeDefinitions: Record<ToolAppTab, ModeDefinition> = {
  presentation: { id: "presentation", label: "Presentation", ariaLabel: "Presentation", Icon: PresentationIcon },
  dictation: { id: "dictation", label: "Dictate", ariaLabel: "Dictation", Icon: Mic },
  "text-transfer": { id: "text-transfer", label: "Send text", ariaLabel: "Send text to PC", Icon: Send },
  "clipboard-read": { id: "clipboard-read", label: "Get text", ariaLabel: "Get text from PC", Icon: ClipboardPaste }
};

const toolModeOrder = ["presentation", "dictation", "text-transfer", "clipboard-read"] satisfies ToolAppTab[];
const stableToolModeOrder = ["dictation", "text-transfer", "clipboard-read"] satisfies ToolAppTab[];

export function getAvailableToolModeIds(presentationAvailable: boolean): ToolAppTab[] {
  return presentationAvailable ? toolModeOrder : stableToolModeOrder;
}

export function getEffectiveFourthMode(fourthMode: FourthMode, presentationAvailable: boolean): FourthMode {
  return fourthMode === "presentation" && !presentationAvailable ? "dictation" : fourthMode;
}

export function getModeTabs(fourthMode: FourthMode, presentationAvailable: boolean): ModeDefinition[] {
  const effectiveFourthMode = getEffectiveFourthMode(fourthMode, presentationAvailable);
  return [...primaryModeDefinitions, toolModeDefinitions[effectiveFourthMode]];
}

export function getModeDefinition(tab: MainAppTab): ModeDefinition {
  return primaryModeDefinitions.find((mode) => mode.id === tab) ?? toolModeDefinitions[tab as ToolAppTab] ?? toolModeDefinitions.dictation;
}
