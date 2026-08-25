import { describe, expect, it } from "vitest";
import { parseServerMessage } from "./connectionProtocol";

describe("file-transfer frame routing", () => {
  it("parses ordinary frames before inspecting file-transfer text in their fields", () => {
    expect(parseServerMessage(JSON.stringify({
      type: "file.jobs.status",
      jobs: [{
        jobId: "job-a",
        operation: "upload",
        state: "running",
        queuePosition: 0,
        currentName: "file.transfer.txt",
        itemsCompleted: 0,
        itemsTotal: 1,
        bytesCompleted: 0,
        bytesTotal: 12,
        canPause: false,
        canResume: false,
        canCancel: true
      }]
    }))).not.toBeNull();
  });
});
