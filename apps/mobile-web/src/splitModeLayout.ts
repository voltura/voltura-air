export const splitModeMinimumWidth = 640;

export function supportsSplitModeLayout(width: number, height: number): boolean {
  return width >= splitModeMinimumWidth && width > height;
}
