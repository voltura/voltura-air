import { describe, expect, it } from "vitest";
import { parseFileTransferServerMessage } from "./fileTransferServerProtocol";

describe("file transfer server protocol", () => {
  it("requires exact and internally consistent action results", () => {
    expect(
      parseFileTransferServerMessage(
        JSON.stringify({
          type: "file.transfer.start.result",
          operationId: "start-a",
          succeeded: true,
          message: "Ready.",
          transferId: "transfer-a",
        }),
      ),
    ).not.toBeNull();
    expect(
      parseFileTransferServerMessage(
        JSON.stringify({
          type: "file.transfer.start.result",
          operationId: "start-a",
          succeeded: true,
          message: "Ready.",
        }),
      ),
    ).toBeNull();
    expect(
      parseFileTransferServerMessage(
        JSON.stringify({
          type: "file.transfer.start.result",
          operationId: "start-a",
          succeeded: false,
          code: "busy",
          message: "Busy.",
          transferId: "transfer-a",
        }),
      ),
    ).toBeNull();
    expect(
      parseFileTransferServerMessage(
        JSON.stringify({
          type: "file.transfer.cancel.result",
          operationId: "cancel-a",
          succeeded: true,
          message: "Canceled.",
          extra: true,
        }),
      ),
    ).toBeNull();
  });
});
