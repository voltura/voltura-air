import { describe, expect, it } from "vitest";
import type { ClientMessage } from "../protocol";
import {
  getPowerCapabilities,
  getAwakeCapability,
  getPresentationCapability,
  getPcDisconnectedMessage,
  getUrlOpenCapability,
  normalizeAppLaunchActions,
  normalizeHostStatus,
  shouldTrackInputAck,
  trimPendingInputAcks
} from "./connectionProtocol";

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
    expect(getPresentationCapability({ presentation: {} as never })).toBeUndefined();
  });

  it("keeps recognized Windows lock availability metadata", () => {
    const power = { lock: true, lockAvailability: "disabledByPolicy" as const, blackoutDisplay: true, displayOff: false, screenSaver: true, screenSaverAvailable: true, signOut: false, restart: false, shutdown: false };

    expect(getPowerCapabilities({ power })).toEqual(power);
  });
});
