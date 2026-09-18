import { useEffect, useRef, type TouchEvent } from "react";
import { Minus, Plus } from "lucide-react";
import type { AudioStateMessage, ClientMessage } from "../../foundation/protocol/messages";

interface Props {
  audioState: AudioStateMessage | null;
  enabled: boolean;
  visible: boolean;
  send: (message: ClientMessage) => void;
}

export function ScreenViewVolumeControls({ audioState, enabled, visible, send }: Props) {
  const pendingVolumes = useRef<number[]>([]);
  useEffect(() => {
    if (!enabled || !audioState) {
      pendingVolumes.current = [];
      return;
    }
    // Host responses are ordered, but React may batch intermediate updates.
    const acknowledged = pendingVolumes.current.indexOf(audioState.volume);
    pendingVolumes.current = acknowledged < 0 ? [] : pendingVolumes.current.slice(acknowledged + 1);
  }, [audioState, enabled]);

  const adjustVolume = (delta: number) => {
    // Bound outstanding work if the host stops acknowledging commands.
    if (!visible || !enabled || !audioState || pendingVolumes.current.length >= 64) {
      return;
    }
    const current = pendingVolumes.current.at(-1) ?? audioState.volume;
    const volume = Math.max(0, Math.min(100, Math.round(current + delta)));
    if (volume === current && (pendingVolumes.current.length > 0 || !audioState.muted)) {
      return;
    }
    pendingVolumes.current.push(volume);
    send({ type: "audio.volume.set", inputContext: "media-controls", volume });
  };
  const stopGesture = (event: TouchEvent<HTMLButtonElement>) => event.stopPropagation();

  return (
    <>
      {([-5, 5] as const).map((delta) => (
        <button
          key={delta}
          type="button"
          className="screen-view-volume-action"
          hidden={!visible}
          disabled={!enabled || !audioState}
          aria-label={delta < 0 ? "Decrease PC volume" : "Increase PC volume"}
          title={delta < 0 ? "Decrease PC volume" : "Increase PC volume"}
          onClick={() => adjustVolume(delta)}
          onTouchStart={stopGesture}
          onTouchMove={stopGesture}
          onTouchEnd={stopGesture}
          onTouchCancel={stopGesture}
        >
          {delta < 0 ? <Minus aria-hidden="true" /> : <Plus aria-hidden="true" />}
        </button>
      ))}
    </>
  );
}
