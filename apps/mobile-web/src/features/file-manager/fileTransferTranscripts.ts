export function createFileTransferStartTranscript(
  clientId: string,
  hostPublicKey: string,
  requestId: string,
  direction: "download" | "upload",
  sessionId: string,
  panel: "left" | "right",
  revision: string,
  entryId: string,
  fileName: string,
  declaredSize: number | null,
) {
  return `VolturaAir file-transfer:start:v1\n${clientId}\n${hostPublicKey}\n${requestId}\n${direction}\n${sessionId}\n${panel}\n${revision}\n${entryId}\n${fileName}\n${declaredSize ?? ""}`;
}

export function createFileTransferOfferTranscript(
  clientId: string,
  hostPublicKey: string,
  requestId: string,
  transferId: string,
  direction: "download" | "upload",
  fileName: string,
  declaredSize: number,
  offerHash: string,
) {
  return `VolturaAir file-transfer:offer:v1\n${clientId}\n${hostPublicKey}\n${requestId}\n${transferId}\n${direction}\n${fileName}\n${declaredSize}\n${offerHash}`;
}

export function createFileTransferAnswerTranscript(
  clientId: string,
  hostPublicKey: string,
  requestId: string,
  transferId: string,
  direction: "download" | "upload",
  fileName: string,
  declaredSize: number,
  offerHash: string,
  answerHash: string,
) {
  return `VolturaAir file-transfer:answer:v1\n${clientId}\n${hostPublicKey}\n${requestId}\n${transferId}\n${direction}\n${fileName}\n${declaredSize}\n${offerHash}\n${answerHash}`;
}
