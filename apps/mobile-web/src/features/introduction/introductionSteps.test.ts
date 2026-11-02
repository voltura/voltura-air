import { describe, expect, it } from "vitest";
import {
  collectDrawerSteps,
  getIntroductionCopy,
  type IntroductionCapabilities,
} from "./introductionSteps";

describe("introduction tool coverage", () => {
  it("follows rendered order, groups adjacent pointer/keyboard controls and introduces custom screens once", () => {
    const root = document.createElement("div");
    root.innerHTML =
      '<button data-introduction-target="remote"></button><button data-introduction-target="trackpad"></button><button data-introduction-target="keyboard"></button><button data-introduction-target="text-transfer"></button><button data-introduction-target="clipboard-read"></button><h3 data-introduction-target="custom"></h3>' +
      "<button>Custom screen</button>".repeat(100);
    const steps = collectDrawerSteps(root, {
      ...getIntroductionCopy({} as IntroductionCapabilities),
    });
    expect(steps.map((step) => step.targets[0]?.dataset.introductionTarget)).toEqual([
      "remote",
      "trackpad",
      "text-transfer",
      "clipboard-read",
      "custom",
    ]);
    expect(steps[1]?.copy).toBe("Control the pointer and type on your PC.");
    expect(steps[1]?.targets).toHaveLength(2);
  });
  it("groups permitted text exchange and keeps restricted tools separate", () => {
    const root = document.createElement("div");
    root.innerHTML =
      '<button data-introduction-target="text-transfer"></button><button data-introduction-target="clipboard-read"></button>';
    const permitted = getIntroductionCopy({
      supportsTextTransfer: true,
      clipboardReadPermission: true,
    } as IntroductionCapabilities);
    expect(collectDrawerSteps(root, permitted).map((step) => step.copy)).toEqual([
      "Send text or fetch your PC’s clipboard.",
    ]);
    const denied = getIntroductionCopy({
      supportsTextTransfer: true,
      clipboardReadPermission: false,
    } as IntroductionCapabilities);
    expect(collectDrawerSteps(root, denied)).toHaveLength(2);
  });

  it("does not join separated targets or include absent/unknown targets", () => {
    const root = document.createElement("div");
    root.innerHTML =
      '<button data-introduction-target="trackpad"></button><button data-introduction-target="remote"></button><button data-introduction-target="keyboard"></button><button data-introduction-target="assistant"></button>';
    expect(collectDrawerSteps(root)).toHaveLength(3);
  });
  it("shortens permission-dependent claims for blocked and older hosts", () => {
    const copy = getIntroductionCopy({
      fileManagerCapability: { canBrowse: true, canTransfer: false },
      screenViewCapability: { canView: true },
      presentationCapability: { canControl: false },
    } as IntroductionCapabilities);
    expect(copy.files).toBe("Browse files on your PC.");
    expect(copy.screen).toBe("See your PC screen here.");
    expect(copy.presentation).toBe("Track your presentation here.");
    expect(copy.terminal).not.toContain("Run PowerShell");
  });
});
