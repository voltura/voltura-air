import { fireEvent, render, screen } from "@testing-library/react";
import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { RemoteMode } from "./RemoteMode";

function renderRemote(overrides: Partial<ComponentProps<typeof RemoteMode>> = {}) {
  const props: ComponentProps<typeof RemoteMode> = {
    audioState: { type: "audio.state", volume: 50, muted: false },
    supportsVolumeControl: true,
    onSetVolume: vi.fn(),
    onToggleMute: vi.fn(),
    sendSpecial: vi.fn(),
    ...overrides
  };

  render(<RemoteMode {...props} />);
  return props;
}

describe("RemoteMode", () => {
  it.each([
    ["Previous track", "MediaPreviousTrack", undefined],
    ["Play or pause", "MediaPlayPause", undefined],
    ["Next track", "MediaNextTrack", undefined],
    ["Seek backward", "ArrowLeft", undefined],
    ["Space", "Space", undefined],
    ["Seek forward", "ArrowRight", undefined],
    ["Esc or back", "Escape", undefined],
    ["Fullscreen", "F11", undefined],
    ["D-pad up", "ArrowUp", undefined],
    ["D-pad left", "ArrowLeft", undefined],
    ["OK", "Enter", undefined],
    ["D-pad right", "ArrowRight", undefined],
    ["D-pad down", "ArrowDown", undefined],
    ["Start or search", "Win", undefined],
    ["Alt Tab", "Tab", ["Alt"]],
    ["Browser back", "BrowserBack", undefined]
  ] as const)("sends %s", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });

  it("maps volume buttons to bounded volume changes", () => {
    const onSetVolume = vi.fn();
    renderRemote({ audioState: { type: "audio.state", volume: 96, muted: false }, onSetVolume });

    fireEvent.click(screen.getByRole("button", { name: "Volume up" }));
    fireEvent.click(screen.getByRole("button", { name: "Volume down" }));

    expect(onSetVolume).toHaveBeenNthCalledWith(1, 100);
    expect(onSetVolume).toHaveBeenNthCalledWith(2, 88);
  });

  it("toggles mute through the audio command path", () => {
    const onToggleMute = vi.fn();
    renderRemote({ onToggleMute });

    fireEvent.click(screen.getByRole("button", { name: "Mute PC" }));

    expect(onToggleMute).toHaveBeenCalledOnce();
  });

  it("disables volume controls when the host does not allow volume control", () => {
    renderRemote({ supportsVolumeControl: false });

    expect((screen.getByRole("button", { name: "Volume down" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Mute PC" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Volume up" }) as HTMLButtonElement).disabled).toBe(true);
  });
});
