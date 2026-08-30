import { Captions, FastForward, Info, Layers, Rewind, SkipBack, SkipForward } from "lucide-react";
import { RemoteButton } from "./RemoteButton";

interface KodiRemoteUtilityGridProps {
  actions: {
    onAspectRatio: () => void;
    onFastForward: () => void;
    onNextChapter: () => void;
    onPlayerDetails: () => void;
    onPreviousChapter: () => void;
    onRewind: () => void;
    onSubtitleTrack: () => void;
  };
}

const tools = [
  { action: "onRewind", label: "Rewind", text: "Rewind", Icon: Rewind },
  { action: "onFastForward", label: "Fast forward", text: "Forward", Icon: FastForward },
  { action: "onSubtitleTrack", label: "Subtitle track", text: "Subtitle", Icon: Captions },
  {
    action: "onPreviousChapter",
    label: "Previous item or chapter",
    text: "Previous",
    Icon: SkipBack,
  },
  {
    action: "onNextChapter",
    label: "Next item or chapter",
    text: "Next",
    Icon: SkipForward,
  },
  { action: "onPlayerDetails", label: "Player details", text: "Details", Icon: Info },
  { action: "onAspectRatio", label: "Aspect ratio", text: "Aspect", Icon: Layers },
] as const;

export default function KodiRemoteUtilityGrid({ actions }: KodiRemoteUtilityGridProps) {
  return (
    <div className="remote-utility-grid remote-kodi-utility-grid" aria-label="Kodi tools">
      {tools.map(({ action, label, text, Icon }) => (
        <RemoteButton key={action} label={label} onClick={actions[action]}>
          <Icon aria-hidden="true" />
          <span>{text}</span>
        </RemoteButton>
      ))}
    </div>
  );
}
