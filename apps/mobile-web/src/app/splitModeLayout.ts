export const splitModeMinimumWidth = 640;

export type StableScreenOrientation = "portrait" | "landscape" | null;

interface StableScreenInfo {
  height: number;
  orientation?: { type?: string | undefined } | null | undefined;
  width: number;
}

export function supportsSplitModeLayout(width: number, height: number, stableOrientation: StableScreenOrientation = null, isTouchDevice = false): boolean {
  const viewportSupportsSplit = width >= splitModeMinimumWidth && width > height;
  if (!isTouchDevice || stableOrientation === null) {
    return viewportSupportsSplit;
  }

  return stableOrientation === "landscape" && viewportSupportsSplit;
}

export function getStableScreenOrientation(screenInfo: StableScreenInfo): StableScreenOrientation {
  const orientationType = screenInfo.orientation?.type;
  if (orientationType?.startsWith("portrait")) {
    return "portrait";
  }
  if (orientationType?.startsWith("landscape")) {
    return "landscape";
  }

  if (screenInfo.width > 0 && screenInfo.height > 0 && screenInfo.width !== screenInfo.height) {
    return screenInfo.width > screenInfo.height ? "landscape" : "portrait";
  }
  return null;
}
