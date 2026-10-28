import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { CustomScreenVolumeSlider } from "./CustomScreenVolumeSlider";
it.each(["disconnect", "revoked"])(
  "retires an unacknowledged optimistic volume after %s",
  (reason) => {
    const audioState = { type: "audio.state" as const, volume: 50, muted: false };
    const props = {
      audioState,
      enabled: true,
      name: "Volume",
      reason: null,
      send: vi.fn(),
      state: "paired" as const,
    };
    const view = render(<CustomScreenVolumeSlider {...props} />);
    fireEvent.change(screen.getByRole("slider"), { target: { value: "80" } });
    expect(screen.getByText("80%")).toBeTruthy();
    view.rerender(
      <CustomScreenVolumeSlider
        {...props}
        enabled={reason !== "revoked"}
        state={reason === "disconnect" ? "connecting" : "paired"}
      />,
    );
    view.rerender(<CustomScreenVolumeSlider {...props} />);
    expect(screen.getByText("50%")).toBeTruthy();
  },
);
