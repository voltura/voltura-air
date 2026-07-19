import { describe, expect, it } from "vitest";
import type { ClientMessage } from "../protocol/messages";
import {
  getPowerCapabilities,
  getAwakeCapability,
  getPresentationCapability,
  getPcDisconnectedMessage,
  getUrlOpenCapability,
  normalizeAppLaunchActions,
  normalizeAudioState,
  normalizeHostStatus,
  parseServerMessage,
  shouldTrackInputAck,
  trimPendingInputAcks
} from "./connectionProtocol";

const validServerMessages = [
  {
    message: {
      type: "pair.accepted", clientId: "client-a", pcName: "Office PC", secret: "secret-a", paired: true,
      capabilities: {
        awake: { canControl: true, active: false, mode: "off", expiresAt: null },
        gestureDebug: false, inputAck: true, clipboardRead: true,
        presentation: { canControl: true },
        power: { lock: true, lockAvailability: "notExplicitlyDisabled", blackoutDisplay: true, displayOff: true, screenSaver: true, screenSaverAvailable: true, signOut: true, restart: true, shutdown: true },
        remoteLaunch: true, urlOpen: { canOpen: true }, sleep: true, textTransfer: true, volume: true
      },
      host: {
        appLaunchActions: [{ id: "custom.notes", label: "Notes", kind: "custom" }],
        defaultRemoteMode: "youtube", developerMode: true, developerSessionId: "session-a",
        hostVersion: "0.6.4", webClientBuildId: "build-a", pcName: "Office PC", pointerSpeed: 55,
        customPointerEnabled: true, inputBlockedByElevation: false, selectedAdapterName: "Ethernet",
        selectedIp: "192.168.1.50", selectedPort: 51395,
        textTransferTarget: { mode: "focused", displayName: "Focused app", available: true },
        webSocketUrl: "ws://192.168.1.50:51395/ws"
      }
    },
    required: ["clientId", "pcName", "secret", "paired"]
  },
  { message: { type: "pair.rejected", reason: "invalid-token", diagnosticCode: "VAIR-PAIR-INVALID-TOKEN" }, required: ["reason"] },
  { message: { type: "status", connected: true, message: "Connected", pcName: "Office PC" }, required: ["connected"] },
  { message: { type: "health.pong" }, required: [] },
  { message: { type: "input.ack", seq: 4 }, required: [] },
  { message: { type: "input.error", seq: 4, code: "VAIR-INPUT", message: "Input failed" }, required: ["message"] },
  { message: { type: "presentation.command.result", operationId: "op-1", target: "powerpoint", action: "next", succeeded: true, code: "OK", message: "Done" }, required: ["operationId", "target", "action", "succeeded", "message"] },
  { message: { type: "system.power.result", operationId: "op-power", action: "lock", succeeded: true, code: "OK", message: "Locked" }, required: ["operationId", "action", "succeeded", "message"] },
  { message: { type: "awake.result", operationId: "op-awake", enabled: true, succeeded: true, code: "OK", message: "Awake" }, required: ["operationId", "enabled", "succeeded", "message"] },
  { message: { type: "app.launch.result", actionId: "custom.notes", succeeded: true, code: "OK", message: "Opened" }, required: ["actionId", "succeeded", "message"] },
  { message: { type: "url.open.result", operationId: "op-2", succeeded: true, code: "OK", message: "Opened", normalizedUrl: "https://example.com/" }, required: ["operationId", "succeeded", "message"] },
  { message: { type: "text.send.result", operationId: "op-3", succeeded: true, code: "OK", message: "Sent", deliveryKind: "typed" }, required: ["operationId", "succeeded", "message"] },
  { message: { type: "clipboard.get.result", operationId: "op-4", succeeded: true, code: "OK", message: "Read", text: null }, required: ["operationId", "succeeded", "message"] },
  { message: { type: "audio.state", volume: 72, muted: false }, required: ["volume", "muted"] }
] as const;

describe("connection protocol policy", () => {
  it("throttles movement acknowledgements without throttling discrete input", () => {
    const move = { type: "pointer.move", dx: 1, dy: 2 } satisfies ClientMessage;
    const key = { type: "keyboard.special", key: "Enter" } satisfies ClientMessage;

    expect(shouldTrackInputAck(move, 1_000, 900)).toBe(false);
    expect(shouldTrackInputAck(move, 1_100, 900)).toBe(true);
    expect(shouldTrackInputAck(key, 1_000, 999)).toBe(true);
  });

  it("bounds pending acknowledgement history to the newest 64 entries", () => {
    const pending = new Map(Array.from({ length: 70 }, (_, index) => [index + 1, index]));

    trimPendingInputAcks(pending);

    expect(pending.size).toBe(64);
    expect([...pending.keys()][0]).toBe(7);
    expect([...pending.keys()].at(-1)).toBe(70);
  });

  it("normalizes host metadata without exposing invalid values", () => {
    expect(normalizeHostStatus({
      defaultRemoteMode: "unknown" as never,
      developerMode: false,
      hostVersion: " 0.2.0 ",
      inputBlockedByElevation: true,
      webClientBuildId: " build-a ",
      pointerSpeed: 500,
      selectedPort: Number.NaN
    })).toEqual({
      defaultRemoteMode: "standard",
      hostVersion: "0.2.0",
      inputBlockedByElevation: true,
      webClientBuildId: "build-a",
      pointerSpeed: 100
    });
  });

  it("normalizes untrusted audio state without accepting coerced values", () => {
    expect(normalizeAudioState({ muted: true, volume: 101.6 })).toEqual({ type: "audio.state", muted: true, volume: 100 });
    expect(normalizeAudioState({ muted: "true", volume: "75" })).toEqual({ type: "audio.state", muted: false, volume: 0 });
  });

  it("does not append a second retry suffix to disconnect feedback", () => {
    const pc = { id: "pc", url: "https://pc.example", name: "Office", customName: true };

    expect(getPcDisconnectedMessage(pc, "Connection lost. Retrying...")).toBe("Connection lost. Retrying...");
    expect(getPcDisconnectedMessage(pc, "Connection lost.")).toBe("Connection lost. Retrying...");
  });

  it("keeps only bounded host-approved application launch summaries", () => {
    expect(normalizeAppLaunchActions([
      { id: "preset.browser", label: " Browser ", kind: "browser" },
      { id: "custom.notes", label: "Notes", kind: "custom" },
      { id: "custom.notes", label: "Duplicate", kind: "custom" },
      { id: "custom.long", label: "ElevenChars", kind: "custom" },
      { id: "../unsafe", label: "Unsafe", kind: "custom" },
      { id: "preset.bad", label: "Bad", kind: "shell" }
    ])).toEqual([
      { id: "preset.browser", label: "Browser", kind: "browser" },
      { id: "custom.notes", label: "Notes", kind: "custom" }
    ]);
  });

  it("accepts only complete boolean power capability sets", () => {
    const power = { lock: true, blackoutDisplay: true, displayOff: false, screenSaver: true, screenSaverAvailable: true, signOut: false, restart: true, shutdown: false };

    expect(getPowerCapabilities({ power })).toEqual(power);
    expect(getPowerCapabilities({ power: { ...power, restart: undefined as never } })).toBeNull();
    expect(getPowerCapabilities(undefined)).toBeNull();
  });

  it("accepts only complete Awake capability state", () => {
    const awake = { canControl: true, active: true, mode: "timed" as const, expiresAt: "2026-07-13T12:00:00Z" };

    expect(getAwakeCapability({ awake })).toEqual(awake);
    expect(getAwakeCapability({ awake: { ...awake, mode: "unknown" as never } })).toBeNull();
    expect(getAwakeCapability(undefined)).toBeNull();
  });

  it("distinguishes URL-open support from its effective permission", () => {
    expect(getUrlOpenCapability({ urlOpen: { canOpen: true } })).toEqual({ canOpen: true });
    expect(getUrlOpenCapability({ urlOpen: { canOpen: false } })).toEqual({ canOpen: false });
    expect(getUrlOpenCapability(undefined)).toBeUndefined();
  });

  it("accepts presentation support only with an explicit effective permission", () => {
    expect(getPresentationCapability({ presentation: { canControl: true } })).toEqual({ canControl: true });
    expect(getPresentationCapability({ presentation: { canControl: false } })).toEqual({ canControl: false });
    expect(getPresentationCapability({ presentation: null })).toBeUndefined();
    expect(getPresentationCapability({ presentation: {} as never })).toBeUndefined();
  });

  it("keeps recognized Windows lock availability metadata", () => {
    const power = { lock: true, lockAvailability: "disabledByPolicy" as const, blackoutDisplay: true, displayOff: false, screenSaver: true, screenSaverAvailable: true, signOut: false, restart: false, shutdown: false };

    expect(getPowerCapabilities({ power })).toEqual(power);
  });
});

describe("parseServerMessage", () => {
  it.each(validServerMessages)("accepts a complete $message.type message", ({ message }) => {
    expect(parseServerMessage(JSON.stringify(message))).toEqual(message);
  });

  it.each(validServerMessages.flatMap(({ message, required }) =>
    required.map((field) => ({ message, field }))))(
    "rejects $message.type when required field $field is missing or has the wrong type",
    ({ message, field }) => {
      const missing = { ...message } as Record<string, unknown>;
      delete missing[field];
      expect(parseServerMessage(JSON.stringify(missing))).toBeNull();
      expect(parseServerMessage(JSON.stringify({ ...message, [field]: null }))).toBeNull();
    }
  );

  it.each([
    ["not JSON"], ["null"], ["true"], ["42"], ['"text"'], ["[]"], ["{}"],
    [JSON.stringify({ type: "future.message" })]
  ])("rejects invalid envelope %s", (data) => {
    expect(parseServerMessage(data)).toBeNull();
  });

  it.each([
    { type: "status", connected: true, pcName: {} },
    { type: "status", connected: true, message: 3 },
    { type: "status", connected: true, capabilities: [] },
    { type: "status", connected: true, capabilities: { awake: { canControl: true, active: false } } },
    { type: "status", connected: true, capabilities: { presentation: [] } },
    { type: "status", connected: true, capabilities: { power: { lock: true } } },
    { type: "status", connected: true, capabilities: { urlOpen: { canOpen: "yes" } } },
    { type: "status", connected: true, host: [] },
    { type: "status", connected: true, host: { appLaunchActions: {} } },
    { type: "status", connected: true, host: { appLaunchActions: [{ id: "../bad", label: "Bad", kind: "custom" }] } },
    { type: "status", connected: true, host: { defaultRemoteMode: "future" } },
    { type: "status", connected: true, host: { selectedPort: "51395" } },
    { type: "status", connected: true, host: { textTransferTarget: { mode: "focused", displayName: {}, available: true } } },
    { type: "input.ack", seq: "4" },
    { type: "input.error", message: "Failed", code: 9 },
    { type: "presentation.command.result", operationId: "bad/id", target: "powerpoint", action: "next", succeeded: true, message: "Done" },
    { type: "presentation.command.result", operationId: "op-1", target: "keynote", action: "next", succeeded: true, message: "Done" },
    { type: "url.open.result", operationId: "op-2", succeeded: true, message: "Opened", normalizedUrl: 4 },
    { type: "text.send.result", operationId: "op-3", succeeded: true, message: "Sent", deliveryKind: "future" },
    { type: "clipboard.get.result", operationId: "op-4", succeeded: true, message: "Read", text: 7 },
    { type: "audio.state", volume: "72", muted: false }
  ])("rejects malformed known message %#", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it("rejects non-string socket payloads", () => {
    expect(parseServerMessage(new Blob())).toBeNull();
  });
});
