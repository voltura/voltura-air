import { getGyroInitialAvailability } from "../../foundation/input/gyroMouse";
import type { useVolturaAirConnection } from "../../foundation/connection/useVolturaAirConnection";

export type IntroductionCapabilities = Pick<
  ReturnType<typeof useVolturaAirConnection>,
  | "remoteInputPermission"
  | "supportsTextTransfer"
  | "clipboardReadPermission"
  | "diagnosticsPermission"
  | "supportsVolumeControl"
  | "appsCapability"
  | "screenViewCapability"
  | "phoneWebcamCapability"
  | "fileManagerCapability"
  | "presentationCapability"
  | "terminalCapability"
>;

export interface IntroductionStep {
  targets: HTMLElement[];
  copy: string;
}

export const toolCopy: Record<string, string> = {
  trackpad: "Control the pointer on your PC.",
  keyboard: "Type on your PC from here.",
  remote: "Control media and navigate your PC.",
  presentation: "Control slides and track your presentation.",
  dictation: "Turn speech into text on your PC.",
  "text-transfer": "Send text to your PC.",
  "clipboard-read": "Fetch text from your PC’s clipboard.",
  files: "Browse PC files and transfer files or photos.",
  terminal: "Run PowerShell commands on your PC.",
};

export function collectDrawerSteps(
  root: HTMLElement,
  copies: Record<string, string> = toolCopy,
): IntroductionStep[] {
  const steps: IntroductionStep[] = [];
  for (const target of root.querySelectorAll<HTMLElement>("[data-introduction-target]")) {
    const copy = copies[target.dataset.introductionTarget ?? ""];
    if (!copy || target.hidden) {
      continue;
    }
    const previous = steps.at(-1);
    const previousId = previous?.targets[0]?.dataset.introductionTarget;
    const id = target.dataset.introductionTarget;
    if (
      previous &&
      previousId === "trackpad" &&
      id === "keyboard" &&
      !copy.includes("permission") &&
      !previous.copy.includes("permission")
    ) {
      previous.targets.push(target);
      previous.copy = "Control the pointer and type on your PC.";
    } else if (
      previous &&
      previousId === "text-transfer" &&
      id === "clipboard-read" &&
      previous.copy === toolCopy["text-transfer"] &&
      copy === toolCopy["clipboard-read"]
    ) {
      previous.targets.push(target);
      previous.copy = "Send text or fetch your PC’s clipboard.";
    } else {
      steps.push({ targets: [target], copy });
    }
  }
  return steps;
}

export function getIntroductionCopy(
  capabilities: IntroductionCapabilities,
): Record<string, string> {
  const screen = capabilities.screenViewCapability;
  const files = capabilities.fileManagerCapability;
  return {
    ...toolCopy,
    trackpad:
      capabilities.remoteInputPermission === false
        ? "Pointer control requires permission on your PC."
        : (toolCopy.trackpad ?? ""),
    keyboard:
      capabilities.remoteInputPermission === false
        ? "Typing requires permission on your PC."
        : (toolCopy.keyboard ?? ""),
    remote:
      capabilities.remoteInputPermission === false
        ? capabilities.supportsVolumeControl
          ? "Control your PC’s volume here."
          : "Remote controls require permission on your PC."
        : "Control media and navigate your PC.",
    dictation: capabilities.supportsTextTransfer
      ? "Turn speech into text on your PC."
      : "Dictation requires text access on your PC.",
    "text-transfer": capabilities.supportsTextTransfer
      ? "Send text to your PC."
      : "Sending text requires permission on your PC.",
    "clipboard-read":
      capabilities.clipboardReadPermission === false
        ? "Clipboard access requires permission on your PC."
        : "Fetch text from your PC’s clipboard.",

    apps: capabilities.appsCapability?.canUse
      ? "Switch between apps on your PC."
      : "Apps access requires permission on your PC.",
    screen: screen?.canView
      ? screen.directPointer?.permissionGranted
        ? "See and control your PC screen here."
        : "See your PC screen here."
      : "Screen viewing requires permission or pairing on your PC.",
    webcam: capabilities.phoneWebcamCapability?.canUse
      ? "Use your phone as a PC webcam."
      : "Phone webcam requires permission or pairing on your PC.",
    gyro:
      capabilities.remoteInputPermission === false
        ? "Motion pointer control requires permission on your PC."
        : getGyroInitialAvailability() === "ready"
          ? "Move the pointer by moving your phone."
          : "Motion controls need a supported secure browser.",
    custom: "Find controls made for your tasks.",
    settings: "Adjust controls, appearance and connection preferences.",
    diagnostics: "Find information to help troubleshoot problems.",
    presentation: capabilities.presentationCapability?.canControl
      ? "Control slides and track your presentation."
      : "Track your presentation here.",
    files: files?.canBrowse
      ? files.canTransfer
        ? "Browse PC files and transfer files or photos."
        : "Browse files on your PC."
      : "File access is blocked by PC permissions.",
    terminal: capabilities.terminalCapability?.canUse
      ? "Run PowerShell commands on your PC."
      : "Terminal access requires permission on your PC.",
  };
}

export function getVisibleModeNavigation(): HTMLElement | undefined {
  const targets = Array.from(
    document.querySelectorAll<HTMLElement>('[data-introduction-target="navigation"]'),
  ).filter((target) => target.getClientRects().length > 0);
  return (
    targets.find((target) => target.classList.contains("bottom-mode-tabs")) ??
    targets.find((target) => target.tagName === "NAV") ??
    targets[0]
  );
}

export function getStepCopy(
  step: IntroductionStep,
  capabilities: IntroductionCapabilities,
): string {
  if (step.targets.length > 1) {
    if (step.targets[0]?.dataset.introductionTarget === "trackpad") {
      return capabilities.remoteInputPermission === false
        ? "Pointer and typing require permission on your PC."
        : step.copy;
    }
    return !capabilities.supportsTextTransfer || capabilities.clipboardReadPermission === false
      ? "Text exchange requires permission on your PC."
      : step.copy;
  }
  return (
    getIntroductionCopy(capabilities)[step.targets[0]?.dataset.introductionTarget ?? ""] ??
    step.copy
  );
}
