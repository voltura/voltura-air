export function appsPreviewOfferTranscript(
  clientId: string,
  hostPublicKey: string,
  operationId: string,
  previewId: string,
  offerHash: string,
): string {
  return `VolturaAir apps-preview:offer:v1\n${clientId}\n${hostPublicKey}\n${operationId}\n${previewId}\n${offerHash}`;
}

export function appsPreviewAnswerTranscript(
  clientId: string,
  hostPublicKey: string,
  offerOperationId: string,
  answerOperationId: string,
  previewId: string,
  offerHash: string,
  answerHash: string,
): string {
  return `VolturaAir apps-preview:answer:v1\n${clientId}\n${hostPublicKey}\n${offerOperationId}\n${answerOperationId}\n${previewId}\n${offerHash}\n${answerHash}`;
}
