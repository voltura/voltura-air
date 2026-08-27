export const terminalStartTranscript = (
  clientId: string,
  hostKey: string,
  operationId: string,
  columns: number,
  rows: number,
) => `VolturaAir terminal:start:v1\n${clientId}\n${hostKey}\n${operationId}\n${columns}\n${rows}`;

export const terminalAttachTranscript = (
  clientId: string,
  hostKey: string,
  operationId: string,
  terminalId: string,
  acknowledgedOffset: number,
  columns: number,
  rows: number,
) =>
  `VolturaAir terminal:attach:v1\n${clientId}\n${hostKey}\n${operationId}\n${terminalId}\n${acknowledgedOffset}\n${columns}\n${rows}`;

export const terminalOfferTranscript = (
  clientId: string,
  hostKey: string,
  operationId: string,
  terminalId: string,
  columns: number,
  rows: number,
  acknowledgedOffset: number,
  offerHash: string,
) =>
  `VolturaAir terminal:offer:v1\n${clientId}\n${hostKey}\n${operationId}\n${terminalId}\n${columns}\n${rows}\n${acknowledgedOffset}\n${offerHash}`;

export const terminalAnswerTranscript = (
  clientId: string,
  hostKey: string,
  offerOperationId: string,
  answerOperationId: string,
  terminalId: string,
  offerHash: string,
  answerHash: string,
) =>
  `VolturaAir terminal:answer:v1\n${clientId}\n${hostKey}\n${offerOperationId}\n${answerOperationId}\n${terminalId}\n${offerHash}\n${answerHash}`;
