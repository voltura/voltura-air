import { render } from "@testing-library/react";
import type { ComponentProps } from "react";
import { vi } from "vitest";
import { defaultRemoteSettings } from "../../../foundation/settings/remoteSettings";
import { RemoteMode } from "./RemoteMode";

export function renderRemote(overrides: Partial<ComponentProps<typeof RemoteMode>> = {}) {
  const props: ComponentProps<typeof RemoteMode> = {
    appLaunchActions: [],
    audioState: { type: "audio.state", volume: 50, muted: false },
    remoteSettings: defaultRemoteSettings,
    onPointerButtonClick: vi.fn(),
    onPointerMove: vi.fn(),
    onAppLaunch: vi.fn(),
    onUrlOpen: vi.fn(),
    onPowerAction: vi.fn(),
    pendingPowerAction: null,
    pendingAppLaunchId: null,
    pendingUrlOpen: false,
    powerActionResult: null,
    powerCapabilities: null,
    urlOpenCapability: { canOpen: true },
    urlOpenResult: null,
    sendSpecial: vi.fn(),
    ...overrides
  };

  const result = render(<RemoteMode {...props} />);
  return { ...props, ...result };
}
