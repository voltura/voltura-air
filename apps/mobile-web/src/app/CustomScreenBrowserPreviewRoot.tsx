import { CustomScreenBrowserPreview } from "../features/custom-screens";
import { useAppTheme } from "./useAppTheme";

export function CustomScreenBrowserPreviewRoot({
  controlDepth,
  screenId,
}: {
  controlDepth: boolean;
  screenId: string;
}) {
  useAppTheme();
  return <CustomScreenBrowserPreview controlDepth={controlDepth} screenId={screenId} />;
}
