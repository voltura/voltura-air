import { describe, expect, it } from "vitest";
import {
  createTerminalAcknowledgement,
  createTerminalInput,
  createTerminalResize,
  maximumTerminalPayloadBytes,
  parseTerminalOutput,
  splitTerminalInput,
} from "./terminalRecords";

describe("terminal records", () => {
  it("creates bounded input and splits larger gestures", () => {
    const payload = new Uint8Array(maximumTerminalPayloadBytes + 1).fill(0x61);
    const parts = splitTerminalInput(payload);
    expect(parts.map((part) => part.length)).toEqual([maximumTerminalPayloadBytes, 1]);
    expect(new Uint8Array(createTerminalInput(parts[0]!))[0]).toBe(0x11);
  });

  it("parses binary output without UTF-8 normalization", () => {
    const bytes = new Uint8Array(12);
    bytes[0] = 0x12;
    new DataView(bytes.buffer).setBigUint64(1, 42n);
    bytes.set([0xff, 0x00, 0x61], 9);
    expect(parseTerminalOutput(bytes.buffer)).toEqual({
      offset: 42,
      payload: Uint8Array.of(0xff, 0x00, 0x61),
    });
  });

  it("writes acknowledgement and resize fields in network byte order", () => {
    const acknowledgement = createTerminalAcknowledgement(258);
    expect(new DataView(acknowledgement).getBigUint64(1)).toBe(258n);
    const resize = createTerminalResize(120, 40);
    expect([...new Uint8Array(resize)]).toEqual([0x14, 0, 120, 0, 40]);
  });
});
