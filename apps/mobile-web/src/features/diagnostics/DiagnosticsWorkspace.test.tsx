import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import type { MobileHostDiagnosticsSnapshot } from "../../foundation/protocol/messages";
import {
  buildDiagnosticsGroups,
  buildDiagnosticsText,
  DiagnosticsWorkspace,
} from "./DiagnosticsWorkspace";

vi.mock("../../foundation/diagnostics/mobileDiagnostics", () => ({ copyTextToClipboard: vi.fn() }));

const snapshot: MobileHostDiagnosticsSnapshot = {
  generatedAt: "2026-08-25T12:00:00.000Z",
  hostVersion: "1.1.0",
  connectionMethod: "Direct",
  enhancedCapabilities: "enabled",
  relayStatus: "Not used",
  relayEndpointType: "Unavailable",
  relayFailureCode: "Unavailable",
  pairingState: "Paired",
  windowsLockPolicy: "Block while locked",
  applicationLogging: "Disabled",
  applicationLogRetention: "7 days",
  pairedDeviceCount: 1,
  connectedDeviceCount: 1,
  pcName: "DESKTOP",
  selectedAdapter: "Ethernet",
  selectedIp: "192.168.1.10",
  selectedPort: 51395,
  advisories: [
    { name: "Network advisory", summary: "Ready", details: "No issue found", code: "none" },
  ],
  computer: {
    windows: "Windows 11 Pro, version 24H2, build 26100",
    system: "Example Model",
    processor: "Example CPU",
    logicalProcessors: "8",
    primaryDisplay: "3840 × 2160 at 60 Hz",
    installedMemory: "16.0 GiB",
    availableMemory: "8.0 GiB",
    systemDisk: "500.0 GiB total, 200.0 GiB free",
    systemUptime: "1d 2h 3m",
  },
};

const baseProps = {
  state: "paired" as const,
  permission: true,
  snapshot,
  pending: false,
  failure: null,
  requestDiagnostics: vi.fn(() => "operation"),
  onBack: vi.fn(),
  onCopyFeedback: vi.fn(),
};

describe("DiagnosticsWorkspace", () => {
  beforeEach(() => {
    vi.stubGlobal("__APP_VERSION__", "web-test");
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false })),
    );
    vi.mocked(copyTextToClipboard).mockReset().mockResolvedValue("copied");
    baseProps.onCopyFeedback.mockReset();
  });

  it.each([
    [
      "disconnected",
      {
        state: "disconnected" as const,
        permission: undefined,
        snapshot: null,
        pending: false,
        failure: null,
      },
      "Connect to a PC",
    ],
    [
      "blocked",
      {
        state: "paired" as const,
        permission: false,
        snapshot: null,
        pending: false,
        failure: null,
      },
      "View diagnostics is blocked",
    ],
    [
      "loading",
      { state: "paired" as const, permission: true, snapshot: null, pending: true, failure: null },
      "Loading PC diagnostics",
    ],
    [
      "failure",
      {
        state: "paired" as const,
        permission: true,
        snapshot: null,
        pending: false,
        failure: { code: "diagnostics-unavailable", message: "Try again." },
      },
      "Try again.",
    ],
  ])("renders the %s state", (_name, overrides, expected) => {
    render(<DiagnosticsWorkspace {...baseProps} {...overrides} />);
    expect(screen.getByText(new RegExp(expected))).toBeTruthy();
  });

  it("renders the successful host snapshot including the primary display", () => {
    render(<DiagnosticsWorkspace {...baseProps} screenSoundQuality="standard" />);
    expect(screen.getByText("Computer")).toBeTruthy();
    expect(screen.getByText("3840 × 2160 at 60 Hz")).toBeTruthy();
    expect(screen.getByText("Sound quality")).toBeTruthy();
    expect(screen.getByText("Standard")).toBeTruthy();
    expect(screen.queryByText(/user profile/i)).toBeNull();
  });

  it("copies exactly one visible label and value", async () => {
    render(<DiagnosticsWorkspace {...baseProps} />);
    fireEvent.click(screen.getByRole("button", { name: "Copy Primary display" }));
    await waitFor(() => {
      expect(copyTextToClipboard).toHaveBeenCalledExactlyOnceWith(
        "Primary display: 3840 × 2160 at 60 Hz",
      );
    });
    expect(baseProps.onCopyFeedback).toHaveBeenCalledWith("Primary display copied.", "success");
  });

  it("copies sound quality from authenticated host status", async () => {
    render(<DiagnosticsWorkspace {...baseProps} screenSoundQuality="low" />);

    fireEvent.click(screen.getByRole("button", { name: "Copy Sound quality" }));

    await waitFor(() => {
      expect(copyTextToClipboard).toHaveBeenCalledExactlyOnceWith("Sound quality: Low");
    });
  });

  it("copies exactly the visible rows in displayed order", async () => {
    render(<DiagnosticsWorkspace {...baseProps} />);
    fireEvent.click(screen.getByRole("button", { name: "Copy all" }));

    const expected = buildDiagnosticsText(
      buildDiagnosticsGroups("paired", snapshot).flatMap((group) => group.rows),
    );
    await waitFor(() => {
      expect(copyTextToClipboard).toHaveBeenCalledExactlyOnceWith(expected);
    });
    expect(expected).not.toContain("C:\\Users");
    expect(expected).not.toContain("WebSocket");
    expect(expected).not.toContain("Other device");
  });

  it("keeps the visible Refresh label stable while a request is pending", () => {
    const { rerender } = render(<DiagnosticsWorkspace {...baseProps} />);
    const button = screen.getByRole("button", { name: "Refresh" });

    rerender(<DiagnosticsWorkspace {...baseProps} pending />);

    expect(screen.getByRole("button", { name: "Refresh" })).toBe(button);
    expect(button.textContent).toBe("Refresh");
  });

  it("requests once on open and once for an explicit refresh", () => {
    const requestDiagnostics = vi.fn(() => "operation");
    const { rerender } = render(
      <DiagnosticsWorkspace
        {...baseProps}
        requestDiagnostics={requestDiagnostics}
        snapshot={null}
      />,
    );
    expect(requestDiagnostics).toHaveBeenCalledOnce();

    rerender(<DiagnosticsWorkspace {...baseProps} requestDiagnostics={requestDiagnostics} />);
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    expect(requestDiagnostics).toHaveBeenCalledTimes(2);
  });
});
