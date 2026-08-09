import { ClipboardPaste, Files, Keyboard, Mic, MousePointer2, Presentation as PresentationIcon, Send, Tv } from "lucide-react";
import { getEffectiveFourthMode } from "../foundation/settings/appSettings";
import type { MainAppTab, ToolAppTab } from "../features/modes";

export type { AppTab, MainAppTab, PrimaryAppTab, ToolAppTab } from "../features/modes";
export type { FourthMode } from "../foundation/settings/appSettings";
export { getEffectiveFourthMode } from "../foundation/settings/appSettings";

export interface ModeDefinition {
  id: MainAppTab;
  label: string;
  ariaLabel: string;
  Icon: typeof MousePointer2;
}

export const primaryModeDefinitions: ModeDefinition[] = [
  { id: "trackpad", label: "Trackpad", ariaLabel: "Trackpad", Icon: MousePointer2 },
  { id: "keyboard", label: "Keyboard", ariaLabel: "Keyboard", Icon: Keyboard },
  { id: "remote", label: "Remote", ariaLabel: "Remote", Icon: Tv }
];

export const toolModeDefinitions: Record<ToolAppTab, ModeDefinition> = {
  presentation: { id: "presentation", label: "Presentation", ariaLabel: "Presentation", Icon: PresentationIcon },
  dictation: { id: "dictation", label: "Dictate", ariaLabel: "Dictation", Icon: Mic },
  "text-transfer": { id: "text-transfer", label: "Send text", ariaLabel: "Send text to PC", Icon: Send },
  "clipboard-read": { id: "clipboard-read", label: "Get text", ariaLabel: "Get text from PC", Icon: ClipboardPaste },
  files: { id: "files", label: "Files", ariaLabel: "Files on PC", Icon: Files }
};

const toolModeOrder = ["presentation", "dictation", "text-transfer", "clipboard-read", "files"] satisfies ToolAppTab[];
const stableToolModeOrder = ["dictation", "text-transfer", "clipboard-read", "files"] satisfies ToolAppTab[];

export function getAvailableToolModeIds(presentationAvailable: boolean, filesAvailable = false): ToolAppTab[] {
  return (presentationAvailable ? toolModeOrder : stableToolModeOrder).filter((id) => id !== "files" || filesAvailable);
}

export function getModeTabs(fourthMode: ToolAppTab, presentationAvailable: boolean, filesAvailable = false): ModeDefinition[] {
  const effectiveFourthMode = getEffectiveFourthMode(fourthMode, presentationAvailable, filesAvailable);
  return [...primaryModeDefinitions, toolModeDefinitions[effectiveFourthMode]];
}

export function getModeDefinition(tab: MainAppTab): ModeDefinition {
  return primaryModeDefinitions.find((mode) => mode.id === tab) ?? toolModeDefinitions[tab as ToolAppTab] ?? toolModeDefinitions.dictation;
}
