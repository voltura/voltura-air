import { useState } from "react";
import { Volume2, VolumeX } from "lucide-react";
import type {
  AudioStateMessage,
  ClientMessage
} from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";

interface CustomScreenVolumeSliderProps {
  audioState: AudioStateMessage | null;
  enabled: boolean;
  name: string;
  reason: string | null | undefined;
  send: (payload: ClientMessage) => void;
  state: ConnectionState;
}

export function CustomScreenVolumeSlider({
  audioState,
  enabled,
  name,
  reason,
  send,
  state
}: CustomScreenVolumeSliderProps) {
  const [optimistic, setOptimistic] = useState<{
    source: AudioStateMessage | null;
    value: AudioStateMessage;
  } | null>(null);
  const current = optimistic?.source === audioState
    ? optimistic.value
    : audioState ?? { type: "audio.state", volume: 50, muted: false };
  const interactive = enabled && state === "paired";

  const setVolume = (volume: number) => {
    if (!interactive) {
      return;
    }
    const nextVolume = Math.max(0, Math.min(100, Math.round(volume)));
    setOptimistic({
      source: audioState,
      value: { type: "audio.state", volume: nextVolume, muted: false }
    });
    send({ type: "audio.volume.set", inputContext: "custom-screens", volume: nextVolume });
  };

  return (
    <div
      aria-disabled={!enabled}
      className={`volume-control custom-screen-volume${current.muted ? " muted" : ""}`}
      title={enabled ? name : reason ?? "Volume control is unavailable."}
    >
      <button
        aria-label={current.muted ? "Unmute PC" : "Mute PC"}
        className="icon-button"
        disabled={!interactive}
        onClick={() => { send({ type: "audio.mute.toggle", inputContext: "custom-screens" }); }}
        title={current.muted ? "Unmute PC" : "Mute PC"}
        type="button"
      >
        {current.muted
          ? <VolumeX aria-hidden="true" />
          : <Volume2 aria-hidden="true" />}
      </button>
      <div className="range-row">
        <input
          aria-label="PC volume"
          disabled={!interactive}
          max="100"
          min="0"
          onChange={(event) => { setVolume(Number(event.target.value)); }}
          step="1"
          type="range"
          value={current.volume}
        />
        <output>{current.volume}%</output>
      </div>
    </div>
  );
}
