import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ConnectionState } from "./connectionTypes";
import {
  powerPointRefreshResponseTimeoutMs,
  presentationCommandResponseTimeoutMs,
  presentationSessionResponseTimeoutMs,
  usePresentationControl
} from "./usePresentationControl";

describe("usePresentationControl", () => {
  afterEach(() => vi.useRealTimers());

  it("allows only one command in flight and completes only its matching result", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));
    let operationId: string | null = null;

    act(() => {
      operationId = result.current.requestPresentationCommand("powerpoint", "next");
      result.current.requestPresentationCommand("powerpoint", "next");
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.command",
      operationId,
      target: "powerpoint",
      action: "next"
    });

    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: "unrelated",
      target: "powerpoint",
      action: "next",
      succeeded: false,
      message: "Unrelated",
      laserPointerActive: false
    }); });
    expect(result.current.pendingPresentationCommand).not.toBeNull();

    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: operationId!,
      target: "powerpoint",
      action: "next",
      succeeded: true,
      message: "Next slide command sent.",
      laserPointerActive: false
    }); });
    expect(result.current.pendingPresentationCommand).toBeNull();
    expect(result.current.presentationResult?.succeeded).toBe(true);
  });

  it("keeps command failures visible while successful confirmations expire", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));

    let operationId: string | null = null;
    act(() => {
      operationId = result.current.requestPresentationCommand("powerpoint", "activate");
    });
    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: operationId!,
      target: "powerpoint",
      action: "activate",
      succeeded: false,
      code: "powerpoint-focus-failed",
      message: "Windows could not bring PowerPoint to the foreground.",
      laserPointerActive: false
    }); });

    await act(() => vi.advanceTimersByTime(6000));
    expect(result.current.presentationResult?.succeeded).toBe(false);

    act(() => {
      operationId = result.current.requestPresentationCommand("powerpoint", "activate");
    });
    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: operationId!,
      target: "powerpoint",
      action: "activate",
      succeeded: true,
      message: "PowerPoint activated.",
      laserPointerActive: false
    }); });

    await act(() => vi.advanceTimersByTime(5000));
    expect(result.current.presentationResult).toBeNull();
  });

  it("reports an acknowledgement timeout and stops pending work on disconnect", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, rerender } = renderHook(({ state }: { state: ConnectionState }) => usePresentationControl(state, send), {
      initialProps: { state: "paired" as ConnectionState }
    });

    await act(() => result.current.requestPresentationCommand("google-slides", "black"));
    await act(() => vi.advanceTimersByTime(presentationCommandResponseTimeoutMs - 1));
    expect(result.current.presentationResult).toBeNull();
    expect(result.current.pendingPresentationCommand).not.toBeNull();

    await act(() => vi.advanceTimersByTime(1));
    expect(result.current.presentationResult?.code).toBe("VAIR-PRESENTATION-RESPONSE-TIMEOUT");

    await act(() => result.current.requestPresentationCommand("google-slides", "next"));
    rerender({ state: "unavailable" });
    expect(result.current.pendingPresentationCommand).toBeNull();
    expect(result.current.presentationResult).toBeNull();
  });

  it("sends idempotent laser cleanup while another presenter command is pending", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));

    act(() => {
      result.current.requestPresentationCommand("powerpoint", "next");
      result.current.requestPresentationCommand("pdf", "pointer", false);
    });

    expect(send).toHaveBeenCalledTimes(2);
    expect(send.mock.calls[1]?.[0]).toMatchObject({
      type: "presentation.command",
      target: "pdf",
      action: "pointer",
      enabled: false
    });
    expect(result.current.pendingPresentationCommand?.action).toBe("next");
  });

  it("correlates one pending authoritative session command", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));
    let operationId: string | null = null;

    act(() => {
      operationId = result.current.requestPresentationSession("break", { enabled: true });
      result.current.requestPresentationSession("break", { enabled: false });
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.session",
      operationId,
      action: "break",
      enabled: true
    });
    expect(result.current.pendingPresentationSession?.action).toBe("break");

    act(() => { result.current.completePresentationSession({
      type: "presentation.session.result",
      operationId: operationId!,
      action: "break",
      succeeded: true,
      message: "Break started."
    }); });

    expect(result.current.pendingPresentationSession).toBeNull();
    expect(result.current.presentationSessionResult?.succeeded).toBe(true);
  });

  it("reports authoritative session acknowledgement failures and timeouts", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));

    let operationId: string | null = null;
    act(() => {
      operationId = result.current.requestPresentationSession("save");
    });
    act(() => { result.current.completePresentationSession({
      type: "presentation.session.result",
      operationId: operationId!,
      action: "save",
      succeeded: false,
      code: "session-persistence-failed",
      message: "The session could not be saved."
    }); });

    expect(result.current.presentationSessionResult?.code).toBe("session-persistence-failed");

    act(() => {
      result.current.requestPresentationSession("break", { enabled: true });
    });
    await act(() => vi.advanceTimersByTime(presentationSessionResponseTimeoutMs));

    expect(result.current.pendingPresentationSession).toBeNull();
    expect(result.current.presentationSessionResult?.code)
      .toBe("VAIR-PRESENTATION-SESSION-RESPONSE-TIMEOUT");
  });

  it("tracks and correlates PowerPoint refresh acknowledgements", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));

    let operationId: string | null = null;
    act(() => {
      operationId = result.current.requestPowerPointRefresh();
      result.current.requestPowerPointRefresh();
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.powerpoint.refresh",
      operationId
    });
    expect(result.current.pendingPowerPointRefresh?.operationId).toBe(operationId);

    act(() => { result.current.completePowerPointRefresh({
      type: "presentation.powerpoint.refresh.result",
      operationId: "unrelated",
      succeeded: true,
      message: "Ignored.",
      state: "ready",
      presentations: []
    }); });
    expect(result.current.pendingPowerPointRefresh).not.toBeNull();

    act(() => { result.current.completePowerPointRefresh({
      type: "presentation.powerpoint.refresh.result",
      operationId: operationId!,
      succeeded: false,
      code: "powerpoint-busy",
      message: "PowerPoint is busy.",
      state: "busy",
      presentations: []
    }); });
    expect(result.current.pendingPowerPointRefresh).toBeNull();
    expect(result.current.powerPointRefreshResult?.code).toBe("powerpoint-busy");

    act(() => {
      result.current.requestPowerPointRefresh();
    });
    await act(() => vi.advanceTimersByTime(powerPointRefreshResponseTimeoutMs));
    expect(result.current.pendingPowerPointRefresh).toBeNull();
    expect(result.current.powerPointRefreshResult?.code)
      .toBe("VAIR-POWERPOINT-REFRESH-RESPONSE-TIMEOUT");
  });

  it("tracks and correlates a saved PowerPoint launch", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));
    let operationId: string | null = null;

    act(() => {
      operationId = result.current.requestPowerPointLaunch("report-1");
      result.current.requestPowerPointLaunch("report-2");
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.powerpoint.launch",
      operationId,
      presentationId: "report-1"
    });
    expect(result.current.pendingPowerPointLaunch?.presentationId).toBe("report-1");

    act(() => { result.current.completePowerPointLaunch({
      type: "presentation.powerpoint.launch.result",
      operationId: operationId!,
      presentationId: "report-1",
      succeeded: true,
      message: "Presentation opened and started.",
      runtimePresentationId: "runtime-1"
    }); });
    expect(result.current.pendingPowerPointLaunch).toBeNull();
    expect(result.current.powerPointLaunchResult?.runtimePresentationId).toBe("runtime-1");
  });
});
