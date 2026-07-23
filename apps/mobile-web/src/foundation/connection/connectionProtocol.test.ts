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
import { catalogFrames, serverFrameCatalog } from "./serverFrameCatalog.testData";

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
    expect(getPresentationCapability({ presentation: { canControl: true, canSaveReports: true, laserPointerActive: false } }))
      .toEqual({ canControl: true, canSaveReports: true, laserPointerActive: false });
    expect(getPresentationCapability({ presentation: { canControl: false, canSaveReports: false, laserPointerActive: true } }))
      .toEqual({ canControl: false, canSaveReports: false, laserPointerActive: true });
    expect(getPresentationCapability({})).toBeUndefined();
    expect(getPresentationCapability({ presentation: {} as never })).toBeUndefined();
  });

  it("keeps recognized Windows lock availability metadata", () => {
    const power = { lock: true, lockAvailability: "disabledByPolicy" as const, blackoutDisplay: true, displayOff: false, screenSaver: true, screenSaverAvailable: true, signOut: false, restart: false, shutdown: false };

    expect(getPowerCapabilities({ power })).toEqual(power);
  });
});

describe("parseServerMessage", () => {
  it.each(Object.entries(serverFrameCatalog).filter(([type]) => type.endsWith(".result")))(
    "covers both outcomes for acknowledged $0 frames",
    (_type, contract) => {
      const frames = contract.frames as unknown as readonly { succeeded: boolean }[];
      expect(frames.some((frame) => frame.succeeded)).toBe(true);
      expect(frames.some((frame) => !frame.succeeded)).toBe(true);
    }
  );

  it("rejects a result with a null optional code", () => {
    expect(parseServerMessage(JSON.stringify({
      type: "text.send.result",
      operationId: "op-text",
      succeeded: true,
      code: null,
      message: "Text was added to a new Notepad document.",
      deliveryKind: "pasted"
    }))).toBeNull();
  });

  it.each([
    { type: "url.open.result", operationId: "op-url", succeeded: false, code: "invalid-url", message: "Invalid URL", normalizedUrl: null },
    { type: "clipboard.get.result", operationId: "op-clipboard", succeeded: false, code: "unavailable", message: "Unavailable", text: null },
    { type: "status", connected: true, capabilities: { presentation: null } },
    { type: "status", connected: true, capabilities: { awake: { canControl: true, active: false, mode: "off", expiresAt: null } } }
  ])("rejects null optional protocol fields: $type", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it.each(catalogFrames)("accepts the catalogued $type frame", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toEqual(message);
  });

  it.each([
    { type: "pair.accepted", clientId: "client-a", pcName: "PC", paired: true, secret: "removed" },
    { type: "pair.challenge", clientId: "client-a", challenge: "challenge", secretNonce: "removed" },
    { type: "pair.rejected", reason: "invalid-token", diagnosticCode: "removed" }
  ])("rejects undeclared pairing fields: $type", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it.each(Object.entries(serverFrameCatalog).flatMap(([type, contract]) =>
    contract.frames.flatMap((message) => contract.required.map((field) => ({ type, message, field })))))(
    "rejects $type when required field $field is missing or null",
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
