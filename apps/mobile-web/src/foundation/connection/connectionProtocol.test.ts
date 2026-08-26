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
  trimPendingInputAcks,
} from "./connectionProtocol";
import { catalogFrames, serverFrameCatalog } from "./serverFrameCatalog.testData";
import { parseFileTransferServerMessage } from "./fileTransferServerProtocol";

describe("connection protocol policy", () => {
  it("throttles movement acknowledgements without throttling discrete input", () => {
    const move = { type: "pointer.move", dx: 1, dy: 2 } satisfies ClientMessage;
    const directMove = {
      type: "screen.pointer.move",
      displayId: "display-1",
      x: 0.25,
      y: 0.75,
    } satisfies ClientMessage;
    const key = { type: "keyboard.special", key: "Enter" } satisfies ClientMessage;

    expect(shouldTrackInputAck(move, 1_000, 900)).toBe(false);
    expect(shouldTrackInputAck(move, 1_100, 900)).toBe(true);
    expect(shouldTrackInputAck(directMove, 1_000, 900)).toBe(false);
    expect(shouldTrackInputAck(directMove, 1_100, 900)).toBe(true);
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
    expect(
      normalizeHostStatus({
        defaultRemoteMode: "unknown" as never,
        developerMode: false,
        controlDepth: false,
        hostVersion: " 0.2.0 ",
        inputBlockedByElevation: true,
        webClientBuildId: " build-a ",
        pointerSpeed: 500,
        selectedPort: Number.NaN,
      }),
    ).toEqual({
      defaultRemoteMode: "standard",
      controlDepth: false,
      hostVersion: "0.2.0",
      inputBlockedByElevation: true,
      webClientBuildId: "build-a",
      pointerSpeed: 100,
    });
  });

  it("accepts optional direct-pointer support only with a boolean permission", () => {
    const capability = {
      enabled: true,
      permissionGranted: true,
      canView: true,
      requiresRepair: false,
      encrypted: true,
      maxWidth: 1920,
      maxHeight: 1080,
      maxFramesPerSecond: 30,
    };
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: {
            screenView: { ...capability, directPointer: { permissionGranted: false } },
          },
        }),
      ),
    ).not.toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: {
            screenView: { ...capability, directPointer: { permissionGranted: "yes" } },
          },
        }),
      ),
    ).toBeNull();
  });

  it("accepts receiver-quality feedback only as the optional true capability", () => {
    const capability = {
      enabled: true,
      permissionGranted: true,
      canView: true,
      requiresRepair: false,
      encrypted: true,
      maxWidth: 1920,
      maxHeight: 1080,
      maxFramesPerSecond: 30,
    };
    for (const receiverQualityFeedback of [undefined, true]) {
      expect(
        parseServerMessage(
          JSON.stringify({
            type: "status",
            connected: true,
            capabilities: { screenView: { ...capability, receiverQualityFeedback } },
          }),
        ),
      ).not.toBeNull();
    }
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { screenView: { ...capability, receiverQualityFeedback: false } },
        }),
      ),
    ).toBeNull();
  });

  it("accepts enhanced capability authority only with an explicit boolean", () => {
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { enhancedCapabilities: { enabled: true } },
        }),
      ),
    ).not.toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { enhancedCapabilities: { enabled: "yes" } },
        }),
      ),
    ).toBeNull();
  });

  it("normalizes untrusted audio state without accepting coerced values", () => {
    expect(normalizeAudioState({ muted: true, volume: 101.6 })).toEqual({
      type: "audio.state",
      muted: true,
      volume: 100,
    });
    expect(normalizeAudioState({ muted: "true", volume: "75" })).toEqual({
      type: "audio.state",
      muted: false,
      volume: 0,
    });
  });

  it("does not append a second retry suffix to disconnect feedback", () => {
    const pc = { id: "pc", url: "https://pc.example", name: "Office", customName: true };

    expect(getPcDisconnectedMessage(pc, "Connection lost. Retrying...")).toBe(
      "Connection lost. Retrying...",
    );
    expect(getPcDisconnectedMessage(pc, "Connection lost.")).toBe("Connection lost. Retrying...");
  });

  it("keeps only bounded host-approved application launch summaries", () => {
    expect(
      normalizeAppLaunchActions([
        { id: "preset.browser", label: " Browser ", kind: "browser" },
        { id: "custom.notes", label: "Notes", kind: "custom" },
        { id: "custom.notes", label: "Duplicate", kind: "custom" },
        { id: "custom.long", label: "ElevenChars", kind: "custom" },
        { id: "../unsafe", label: "Unsafe", kind: "custom" },
        { id: "preset.bad", label: "Bad", kind: "shell" },
      ]),
    ).toEqual([
      { id: "preset.browser", label: "Browser", kind: "browser" },
      { id: "custom.notes", label: "Notes", kind: "custom" },
    ]);
  });

  it("accepts only complete boolean power capability sets", () => {
    const power = {
      lock: true,
      blackoutDisplay: true,
      displayOff: false,
      screenSaver: true,
      screenSaverAvailable: true,
      signOut: false,
      restart: true,
      shutdown: false,
    };

    expect(getPowerCapabilities({ power })).toEqual(power);
    expect(getPowerCapabilities({ power: { ...power, restart: undefined as never } })).toBeNull();
    expect(getPowerCapabilities(undefined)).toBeNull();
  });

  it("accepts only complete Awake capability state", () => {
    const awake = {
      canControl: true,
      active: true,
      mode: "timed" as const,
      expiresAt: "2026-07-13T12:00:00Z",
    };

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
    expect(
      getPresentationCapability({
        presentation: { canControl: true, canSaveReports: true, laserPointerActive: false },
      }),
    ).toEqual({ canControl: true, canSaveReports: true, laserPointerActive: false });
    expect(
      getPresentationCapability({
        presentation: { canControl: false, canSaveReports: false, laserPointerActive: true },
      }),
    ).toEqual({ canControl: false, canSaveReports: false, laserPointerActive: true });
    expect(
      getPresentationCapability({
        presentation: {
          canControl: true,
          canSaveReports: true,
          laserPointerActive: true,
          laserPointerColor: "green",
          laserPointerDefaultColor: "red",
        },
      }),
    ).toEqual({
      canControl: true,
      canSaveReports: true,
      laserPointerActive: true,
      laserPointerColor: "green",
      laserPointerDefaultColor: "red",
    });
    expect(
      getPresentationCapability({
        presentation: {
          canControl: true,
          canSaveReports: true,
          laserPointerActive: false,
          powerPoint: {
            state: "ready",
            foregroundActivationSupported: true,
            presentations: [],
          },
        },
      }),
    ).toEqual({
      canControl: true,
      canSaveReports: true,
      laserPointerActive: false,
      powerPoint: {
        state: "ready",
        foregroundActivationSupported: true,
        presentations: [],
      },
    });
    expect(getPresentationCapability({})).toBeUndefined();
    expect(getPresentationCapability({ presentation: {} as never })).toBeUndefined();
  });

  it("keeps recognized Windows lock availability metadata", () => {
    const power = {
      lock: true,
      lockAvailability: "disabledByPolicy" as const,
      blackoutDisplay: true,
      displayOff: false,
      screenSaver: true,
      screenSaverAvailable: true,
      signOut: false,
      restart: false,
      shutdown: false,
    };

    expect(getPowerCapabilities({ power })).toEqual(power);
  });
});

describe("parseServerMessage", () => {
  it("accepts exact protocol string limits and rejects one character over", () => {
    const cases = [
      [
        {
          type: "screen.view.ended",
          operationId: "o".repeat(64),
          reason: "host-stopped",
          message: "m".repeat(240),
        },
        "operationId",
        64,
      ],
      [{ type: "input.error", message: "m".repeat(240), code: "c".repeat(80) }, "message", 240],
      [{ type: "pair.rejected", reason: "r".repeat(80) }, "reason", 80],
      [
        { type: "pair.accepted", clientId: "i".repeat(128), pcName: "p".repeat(120), paired: true },
        "pcName",
        120,
      ],
      [
        { type: "status", connected: true, host: { selectedAdapterName: "a".repeat(256) } },
        "selectedAdapterName",
        256,
      ],
      [{ type: "status", connected: true, host: { selectedIp: "i".repeat(64) } }, "selectedIp", 64],
      [
        {
          type: "url.open.result",
          operationId: "op",
          succeeded: true,
          message: "ok",
          normalizedUrl: "u".repeat(512),
        },
        "normalizedUrl",
        512,
      ],
      [
        { type: "status", connected: true, host: { webClientBuildId: "b".repeat(128) } },
        "webClientBuildId",
        128,
      ],
    ] as const;
    for (const [frame, field, limit] of cases) {
      expect(parseServerMessage(JSON.stringify(frame)), field).not.toBeNull();
      const over = structuredClone(frame) as Record<string, unknown>;
      if ("host" in over) {
        over.host = { ...(over.host as Record<string, unknown>), [field]: "x".repeat(limit + 1) };
      } else {
        over[field] = "x".repeat(limit + 1);
      }
      expect(parseServerMessage(JSON.stringify(over)), field).toBeNull();
    }
  });
  it.each(Object.entries(serverFrameCatalog).filter(([type]) => type.endsWith(".result")))(
    "covers both outcomes for acknowledged $0 frames",
    (_type, contract) => {
      const frames = contract.frames as unknown as readonly { succeeded: boolean }[];
      expect(frames.some((frame) => frame.succeeded)).toBe(true);
      expect(frames.some((frame) => !frame.succeeded)).toBe(true);
    },
  );

  it("rejects a result with a null optional code", () => {
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "text.send.result",
          operationId: "op-text",
          succeeded: true,
          code: null,
          message: "Text was added to a new Notepad document.",
          deliveryKind: "pasted",
        }),
      ),
    ).toBeNull();
  });

  it("accepts only the current host-ended screen-view reasons", () => {
    const frame = {
      type: "screen.view.ended",
      operationId: "screen-operation",
      reason: "host-stopped",
      message: "The PC stopped screen viewing.",
    };

    expect(parseServerMessage(JSON.stringify(frame))).toEqual(frame);
    const missingOperationId = {
      type: frame.type,
      reason: frame.reason,
      message: frame.message,
    };
    expect(parseServerMessage(JSON.stringify(missingOperationId))).toBeNull();
    expect(parseServerMessage(JSON.stringify({ ...frame, reason: "legacy-stop" }))).toBeNull();
  });

  it("accepts the audio failure terminal reason for Phone webcam", () => {
    const frame = {
      type: "phone.webcam.ended",
      operationId: "phone-webcam-operation",
      reason: "audio-failed",
      message: "Phone microphone audio stopped on the PC.",
    };

    expect(parseServerMessage(JSON.stringify(frame))).toEqual(frame);
  });

  it("accepts bounded self-hosted TURN URLs and rejects unsafe forms", () => {
    const frame = {
      type: "screen.view.start.result",
      operationId: "op-screen-start",
      displayId: "display-1",
      succeeded: true,
      message: "Ready.",
      iceServers: [
        {
          urls: [
            "turns:turn.example.net:443?transport=tcp",
            "turn:turn.example.net:49160?transport=udp",
          ],
          username: "temporary-user",
          credential: "temporary-credential",
        },
      ],
    };

    expect(parseServerMessage(JSON.stringify(frame))).toEqual(frame);
    for (const url of [
      "stun:turn.example.net:443",
      "turns:user@turn.example.net:443?transport=tcp",
      "turns:turn.example.net:0?transport=tcp",
      "turns:turn.example.net:65536?transport=tcp",
      "turns:turn.example.net:443/path",
      "turns:turn.example.net:443?transport=https",
    ]) {
      expect(
        parseServerMessage(
          JSON.stringify({
            ...frame,
            iceServers: [{ ...frame.iceServers[0], urls: [url] }],
          }),
        ),
      ).toBeNull();
    }
  });

  it("accepts current custom-trackpad fields and enforces compact names", () => {
    const frame = {
      type: "custom.screen.get.result",
      operationId: "op-custom",
      succeeded: true,
      screen: {
        id: "screen.pointer",
        name: "Pointer workspace",
        revision: "revision.pointer",
        orientationLayoutsEnabled: true,
        showNavigationHeader: true,
        sections: [
          {
            id: "section.pointer",
            name: "Collapsible pointer",
            showHeader: true,
            widthColumns: 6,
            heightMode: "fill",
            fillWeight: 2,
            rowLimit: 0,
            buttonAlignment: "start",
            kind: "trackpad",
            collapsible: true,
            initiallyExpanded: true,
            trackpadLeftClick: true,
            trackpadRightClick: true,
            trackpadButtonSide: "right",
            trackpadFullscreenControl: true,
            trackpadGyroControl: true,
            trackpadEnabled: true,
            volumeEnabled: true,
            portrait: { order: 1, visible: true, widthColumns: 12 },
            landscape: { order: 0, visible: true, widthColumns: 6 },
            buttons: [],
          },
        ],
      },
    };

    expect(parseServerMessage(JSON.stringify(frame))).toEqual(frame);
    const navigationRingFrame = {
      ...frame,
      screen: {
        ...frame.screen,
        sections: [
          {
            ...frame.screen.sections[0],
            name: "Navigation ring",
            widthColumns: 8,
            kind: "navigationRing",
            collapsible: false,
            trackpadFullscreenControl: false,
            trackpadGyroControl: false,
          },
        ],
      },
    };
    expect(parseServerMessage(JSON.stringify(navigationRingFrame))).toEqual(navigationRingFrame);
    const dPadFrame = {
      ...navigationRingFrame,
      screen: {
        ...navigationRingFrame.screen,
        sections: [
          {
            ...navigationRingFrame.screen.sections[0],
            name: "D-pad",
            kind: "dpad",
          },
        ],
      },
    };
    expect(parseServerMessage(JSON.stringify(dPadFrame))).toEqual(dPadFrame);
    expect(
      parseServerMessage(
        JSON.stringify({
          ...navigationRingFrame,
          screen: {
            ...navigationRingFrame.screen,
            sections: [
              {
                ...navigationRingFrame.screen.sections[0],
                widthColumns: 4,
              },
            ],
          },
        }),
      ),
    ).toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          ...navigationRingFrame,
          screen: {
            ...navigationRingFrame.screen,
            sections: [
              {
                ...navigationRingFrame.screen.sections[0],
                portrait: { order: 0, visible: true, widthColumns: 4 },
              },
            ],
          },
        }),
      ),
    ).toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          ...frame,
          screen: { ...frame.screen, name: "S".repeat(25) },
        }),
      ),
    ).toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          ...frame,
          screen: {
            ...frame.screen,
            sections: [{ ...frame.screen.sections[0], name: "P".repeat(21) }],
          },
        }),
      ),
    ).toBeNull();
  });

  it.each([
    {
      type: "url.open.result",
      operationId: "op-url",
      succeeded: false,
      code: "invalid-url",
      message: "Invalid URL",
      normalizedUrl: null,
    },
    {
      type: "clipboard.get.result",
      operationId: "op-clipboard",
      succeeded: false,
      code: "unavailable",
      message: "Unavailable",
      text: null,
    },
    { type: "status", connected: true, capabilities: { presentation: null } },
    {
      type: "status",
      connected: true,
      capabilities: { awake: { canControl: true, active: false, mode: "off", expiresAt: null } },
    },
  ])("rejects null optional protocol fields: $type", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it.each(catalogFrames)("accepts the catalogued $type frame", (message) => {
    const parse = message.type.startsWith("file.transfer.")
      ? parseFileTransferServerMessage
      : parseServerMessage;
    expect(parse(JSON.stringify(message))).toEqual(message);
  });

  it.each([
    { type: "pair.accepted", clientId: "client-a", pcName: "PC", paired: true, secret: "removed" },
    {
      type: "pair.challenge",
      clientId: "client-a",
      challenge: "challenge",
      secretNonce: "removed",
    },
    { type: "pair.rejected", reason: "invalid-token", diagnosticCode: "removed" },
  ])("rejects undeclared pairing fields: $type", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it.each(
    Object.entries(serverFrameCatalog).flatMap(([type, contract]) =>
      contract.frames.flatMap((message) =>
        contract.required.map((field) => ({ type, message, field })),
      ),
    ),
  )("rejects $type when required field $field is missing or null", ({ type, message, field }) => {
    const missing = { ...message } as Record<string, unknown>;
    delete missing[field];
    const parse = type.startsWith("file.transfer.")
      ? parseFileTransferServerMessage
      : parseServerMessage;
    expect(parse(JSON.stringify(missing))).toBeNull();
    expect(parse(JSON.stringify({ ...message, [field]: null }))).toBeNull();
  });

  it.each([
    ["not JSON"],
    ["null"],
    ["true"],
    ["42"],
    ['"text"'],
    ["[]"],
    ["{}"],
    [JSON.stringify({ type: "future.message" })],
  ])("rejects invalid envelope %s", (data) => {
    expect(parseServerMessage(data)).toBeNull();
  });

  it.each([
    { type: "status", connected: true, pcName: {} },
    { type: "status", connected: true, message: 3 },
    { type: "status", connected: true, capabilities: [] },
    {
      type: "status",
      connected: true,
      capabilities: { awake: { canControl: true, active: false } },
    },
    { type: "status", connected: true, capabilities: { presentation: [] } },
    { type: "status", connected: true, capabilities: { power: { lock: true } } },
    { type: "status", connected: true, capabilities: { urlOpen: { canOpen: "yes" } } },
    { type: "status", connected: true, host: [] },
    { type: "status", connected: true, host: { appLaunchActions: {} } },
    {
      type: "status",
      connected: true,
      host: { appLaunchActions: [{ id: "../bad", label: "Bad", kind: "custom" }] },
    },
    { type: "status", connected: true, host: { defaultRemoteMode: "future" } },
    { type: "status", connected: true, host: { selectedPort: "51395" } },
    {
      type: "status",
      connected: true,
      host: { textTransferTarget: { mode: "focused", displayName: {}, available: true } },
    },
    { type: "input.ack", seq: "4" },
    { type: "input.error", message: "Failed", code: 9 },
    {
      type: "presentation.command.result",
      operationId: "bad/id",
      target: "powerpoint",
      action: "next",
      succeeded: true,
      message: "Done",
    },
    {
      type: "presentation.command.result",
      operationId: "op-1",
      target: "keynote",
      action: "next",
      succeeded: true,
      message: "Done",
    },
    {
      type: "url.open.result",
      operationId: "op-2",
      succeeded: true,
      message: "Opened",
      normalizedUrl: 4,
    },
    {
      type: "text.send.result",
      operationId: "op-3",
      succeeded: true,
      message: "Sent",
      deliveryKind: "future",
    },
    {
      type: "clipboard.get.result",
      operationId: "op-4",
      succeeded: true,
      message: "Read",
      text: 7,
    },
    { type: "audio.state", volume: "72", muted: false },
  ])("rejects malformed known message %#", (message) => {
    expect(parseServerMessage(JSON.stringify(message))).toBeNull();
  });

  it("rejects non-string socket payloads", () => {
    expect(parseServerMessage(new Blob())).toBeNull();
  });
});
