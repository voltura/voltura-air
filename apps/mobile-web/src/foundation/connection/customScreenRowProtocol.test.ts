import { describe, expect, it } from "vitest";
import {
  parseRejectedCustomScreenGetResult,
  parseServerMessage
} from "./connectionProtocol";

describe("custom-screen button-row protocol", () => {
  const frame = {
    type: "custom.screen.get.result",
    operationId: "op-six-rows",
    succeeded: true,
    screen: {
      id: "screen.six-rows",
      name: "Six rows",
      revision: "revision.six-rows",
      orientationLayoutsEnabled: true,
      showNavigationHeader: true,
      sections: [{
        id: "section.six-rows",
        name: "Controls",
        showHeader: true,
        widthColumns: 12,
        heightMode: "fill",
        fillWeight: 1,
        rowLimit: 6,
        buttonAlignment: "start",
        kind: "buttons",
        collapsible: false,
        initiallyExpanded: true,
        trackpadLeftClick: true,
        trackpadRightClick: true,
        trackpadButtonSide: "right",
        trackpadFullscreenControl: false,
        trackpadGyroControl: false,
        trackpadEnabled: true,
        volumeEnabled: true,
        buttons: [{
          id: "button.six",
          name: "Sixth row",
          label: "Six",
          icon: "command",
          presentation: "iconLabel",
          size: "standard",
          repeat: false,
          row: 6,
          portrait: { order: 0, visible: true, row: 6 },
          landscape: { order: 0, visible: true, row: 6 },
          enabled: true
        }]
      }]
    }
  };

  it("accepts six rows", () => {
    expect(parseServerMessage(JSON.stringify(frame))).toEqual(frame);
  });

  it("rejects a seventh section row", () => {
    const rejectedFrame = JSON.stringify({
      ...frame,
      screen: {
        ...frame.screen,
        sections: [{ ...frame.screen.sections[0], rowLimit: 7 }]
      }
    });

    expect(parseServerMessage(rejectedFrame)).toBeNull();
    expect(parseRejectedCustomScreenGetResult(rejectedFrame)).toEqual({
      type: "custom.screen.get.result",
      operationId: frame.operationId
    });
  });

  it("does not classify valid or uncorrelatable frames as rejected screen results", () => {
    expect(parseRejectedCustomScreenGetResult(JSON.stringify(frame))).toBeNull();
    expect(parseRejectedCustomScreenGetResult(JSON.stringify({
      ...frame,
      operationId: "invalid operation id",
      screen: {
        ...frame.screen,
        sections: [{ ...frame.screen.sections[0], rowLimit: 7 }]
      }
    }))).toBeNull();
  });

  it("does not classify unrelated malformed correlated results as rejected definitions", () => {
    const malformedResults = [
      { ...frame, succeeded: "yes" },
      { type: frame.type, operationId: frame.operationId, succeeded: false },
      { ...frame, code: 12 },
      { type: frame.type, operationId: frame.operationId, succeeded: true }
    ];

    for (const malformedResult of malformedResults) {
      expect(parseRejectedCustomScreenGetResult(JSON.stringify(malformedResult))).toBeNull();
    }
  });

  it("rejects a seventh button row", () => {
    expect(parseServerMessage(JSON.stringify({
      ...frame,
      screen: {
        ...frame.screen,
        sections: [{
          ...frame.screen.sections[0],
          buttons: [{ ...frame.screen.sections[0]!.buttons[0]!, row: 7 }]
        }]
      }
    }))).toBeNull();
  });

  it("rejects a seventh orientation row", () => {
    expect(parseServerMessage(JSON.stringify({
      ...frame,
      screen: {
        ...frame.screen,
        sections: [{
          ...frame.screen.sections[0],
          buttons: [{
            ...frame.screen.sections[0]!.buttons[0]!,
            landscape: { order: 0, visible: true, row: 7 }
          }]
        }]
      }
    }))).toBeNull();
  });
});
