import { describe, expect, it } from "vitest";
import { parseAppsServerMessage } from "./appsServerProtocol";

const opaque = "0123456789abcdef0123456789abcdef";

describe("Apps server protocol", () => {
  it("accepts a bounded window list without native identifiers", () => {
    const message = {
      type: "apps.list.result",
      operationId: "apps-1",
      succeeded: true,
      code: "accepted",
      message: "Open applications refreshed.",
      revision: opaque,
      windows: [
        {
          windowId: opaque,
          title: "Notes",
          applicationName: "Notepad",
          active: true,
          minimized: false,
          maximizeSupported: true,
          previewSupported: true,
        },
      ],
    };

    expect(parseAppsServerMessage(JSON.stringify(message))).toEqual(message);
  });

  it("rejects duplicate IDs, extra fields, and native window details", () => {
    const window = {
      windowId: opaque,
      title: "Notes",
      applicationName: "Notepad",
      active: false,
      minimized: false,
      maximizeSupported: true,
      previewSupported: true,
    };
    const base = {
      type: "apps.list.result",
      operationId: "apps-1",
      succeeded: true,
      code: "accepted",
      message: "Refreshed.",
      revision: opaque,
      windows: [window],
    };

    expect(
      parseAppsServerMessage(JSON.stringify({ ...base, windows: [window, window] })),
    ).toBeNull();
    expect(parseAppsServerMessage(JSON.stringify({ ...base, processId: 42 }))).toBeNull();
    expect(
      parseAppsServerMessage(
        JSON.stringify({ ...base, windows: [{ ...window, hwnd: "0x10042" }] }),
      ),
    ).toBeNull();
  });

  it("accepts exact preview signaling and rejects unbounded relay fields", () => {
    const offer = {
      type: "apps.preview.offer",
      operationId: "apps-1",
      previewId: opaque,
      offerSdp: "v=0\r\n",
      hostSignature: "signature",
      iceServers: [
        {
          urls: ["turns:relay.example:443?transport=tcp"],
          username: "user",
          credential: "credential",
        },
      ],
      turnExpiresAt: "2026-08-30T12:00:00Z",
    };

    expect(parseAppsServerMessage(JSON.stringify(offer))).toEqual(offer);
    expect(
      parseAppsServerMessage(
        JSON.stringify({ ...offer, iceServers: [{ ...offer.iceServers[0], urls: ["https://x"] }] }),
      ),
    ).toBeNull();
  });

  it("catalogues each exact recoverable result shape", () => {
    const messages = [
      {
        type: "apps.list.result",
        operationId: "apps-list-failed",
        succeeded: false,
        code: "permission-denied",
        message: "Blocked.",
        windows: [],
      },
      {
        type: "apps.activate.result",
        operationId: "apps-activate-1",
        windowId: opaque,
        succeeded: true,
        code: "accepted",
        message: "Activated.",
      },
      {
        type: "apps.close.result",
        operationId: "apps-close-1",
        windowId: opaque,
        succeeded: true,
        code: "close-requested",
        message: "Close requested.",
      },
      {
        type: "apps.preview.answer.result",
        operationId: "apps-answer-1",
        succeeded: false,
        code: "offer-expired",
        message: "Expired.",
      },
      {
        type: "apps.preview.ended",
        previewId: opaque,
        reason: "permission-revoked",
        message: "Stopped.",
      },
    ];

    for (const message of messages) {
      expect(parseAppsServerMessage(JSON.stringify(message))).toEqual(message);
      expect(parseAppsServerMessage(JSON.stringify({ ...message, nativeHandle: 42 }))).toBeNull();
    }
  });
});
