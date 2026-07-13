import { Keyboard, Mic, MousePointer2, Tv } from "lucide-react";

export type AppTab = "trackpad" | "keyboard" | "remote" | "dictation" | "debug";
export type MainAppTab = Exclude<AppTab, "debug">;

export const modeTabs: Array<{
  id: MainAppTab;
  label: string;
  ariaLabel: string;
  Icon: typeof MousePointer2;
}> = [
  { id: "trackpad", label: "Trackpad", ariaLabel: "Trackpad mode", Icon: MousePointer2 },
  { id: "keyboard", label: "Keyboard", ariaLabel: "Keyboard mode", Icon: Keyboard },
  { id: "remote", label: "Remote", ariaLabel: "Remote mode", Icon: Tv },
  { id: "dictation", label: "Dictate", ariaLabel: "Dictation mode", Icon: Mic }
];
