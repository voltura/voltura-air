import { describe, expect, it } from "vitest";
import {
  createFileTransferAnswerTranscript, createFileTransferOfferTranscript, createFileTransferStartTranscript
} from "./fileTransferTranscripts";

describe("file-transfer signed transcripts", () => {
  it("binds request, identities, metadata, and both SDP hashes exactly", () => {
    expect(createFileTransferStartTranscript(
      "client", "host-key", "request", "upload", "session", "left", "revision", "", "report.txt", 0
    )).toBe("VolturaAir file-transfer:start:v1\nclient\nhost-key\nrequest\nupload\nsession\nleft\nrevision\n\nreport.txt\n0");
    expect(createFileTransferOfferTranscript(
      "client", "host-key", "request", "transfer", "download", "report.txt", 12, "offer-hash"
    )).toBe("VolturaAir file-transfer:offer:v1\nclient\nhost-key\nrequest\ntransfer\ndownload\nreport.txt\n12\noffer-hash");
    expect(createFileTransferAnswerTranscript(
      "client", "host-key", "request", "transfer", "download", "report.txt", 12, "offer-hash", "answer-hash"
    )).toBe("VolturaAir file-transfer:answer:v1\nclient\nhost-key\nrequest\ntransfer\ndownload\nreport.txt\n12\noffer-hash\nanswer-hash");
  });
});
