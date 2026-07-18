import type { FourthMode } from "../../foundation/settings/appSettings";

export type PrimaryAppTab = "trackpad" | "keyboard" | "remote";
export type ToolAppTab = FourthMode;
export type AppTab = PrimaryAppTab | ToolAppTab | "debug";
export type MainAppTab = Exclude<AppTab, "debug">;
