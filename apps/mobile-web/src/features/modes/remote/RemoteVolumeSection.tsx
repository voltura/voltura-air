import { Volume2, VolumeX } from "lucide-react";
import type { AudioStateMessage } from "../../../protocol";
import type { RemoteModeId } from "../../../remoteSettings";
import type { RemoteModeCopy } from "./remoteModeCopy";
import { RemoteButton, type RepeatablePressProps } from "./RemoteButton";

interface RemoteVolumeSectionProps {
  audioState: AudioStateMessage | null;
  getRepeatablePressProps: (action: () => void) => RepeatablePressProps;
  mode: RemoteModeId;
  modeCopy: RemoteModeCopy;
  onMute: () => void;
  onVolumeDown: () => void;
  onVolumeUp: () => void;
}

export function RemoteVolumeSection({ audioState, getRepeatablePressProps, mode, modeCopy, onMute, onVolumeDown, onVolumeUp }: RemoteVolumeSectionProps) {
  return (
    <div className="remote-section remote-volume-section">
      <div className="remote-section-title">
        <span>Volume</span>
        <small>{mode === "standard" && audioState ? `${audioState.volume}%${audioState.muted ? " muted" : ""}` : modeCopy.volume}</small>
      </div>
      <div className="remote-volume-grid" aria-label="Volume controls">
        <RemoteButton label="Volume down" pressProps={getRepeatablePressProps(onVolumeDown)}>
          <Volume2 aria-hidden="true" />
          <span>Vol -</span>
        </RemoteButton>
        <RemoteButton label={audioState?.muted ? "Unmute PC" : "Mute PC"} onClick={onMute}>
          {audioState?.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
          <span>{audioState?.muted ? "Unmute" : "Mute"}</span>
        </RemoteButton>
        <RemoteButton label="Volume up" pressProps={getRepeatablePressProps(onVolumeUp)}>
          <Volume2 aria-hidden="true" />
          <span>Vol +</span>
        </RemoteButton>
      </div>
    </div>
  );
}
