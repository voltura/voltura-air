import { describe, expect, it } from "vitest";
import { parseTerminalServerMessage } from "./terminalServerProtocol";

const terminalId = "0123456789abcdef0123456789abcdef";

describe("terminal server protocol", () => {
  it("accepts an exact bounded offer", () => {
    const message = {
      type: "terminal.offer",
      operationId: "terminal-op-1",
      terminalId,
      columns: 80,
      rows: 24,
      acknowledgedOffset: 17,
      offerSdp: "v=0\r\n",
      hostSignature: "signature",
    };

    expect(parseTerminalServerMessage(JSON.stringify(message))).toEqual(message);
  });

  it("rejects extra fields, invalid dimensions, and unsafe offsets", () => {
    const base = {
      type: "terminal.offer",
      operationId: "terminal-op-1",
      terminalId,
      columns: 80,
      rows: 24,
      acknowledgedOffset: 0,
      offerSdp: "v=0\r\n",
      hostSignature: "signature",
    };

    expect(parseTerminalServerMessage(JSON.stringify({ ...base, command: "secret" }))).toBeNull();
    expect(parseTerminalServerMessage(JSON.stringify({ ...base, columns: 9 }))).toBeNull();
    expect(
      parseTerminalServerMessage(
        JSON.stringify({ ...base, acknowledgedOffset: Number.MAX_SAFE_INTEGER + 1 }),
      ),
    ).toBeNull();
  });

  it("accepts lifecycle frames and rejects unknown states", () => {
    expect(
      parseTerminalServerMessage(
        JSON.stringify({
          type: "terminal.status",
          terminalId,
          state: "detached",
          acknowledgedOffset: 42,
        }),
      ),
    ).not.toBeNull();
    expect(
      parseTerminalServerMessage(
        JSON.stringify({
          type: "terminal.status",
          terminalId,
          state: "unknown",
          acknowledgedOffset: 42,
        }),
      ),
    ).toBeNull();
  });
});
