import { hashSessionDescription } from "../webrtc/sessionCrypto";

export const assistantOpenTranscript = (clientId: string, hostKey: string, operationId: string) =>
  `VolturaAir ai-assistant:open:v1\n${clientId}\n${hostKey}\n${operationId}`;

export const assistantAskTranscript = (
  clientId: string,
  hostKey: string,
  operationId: string,
  question: string,
) =>
  `VolturaAir ai-assistant:ask:v1\n${clientId}\n${hostKey}\n${operationId}\n${hashSessionDescription(question)}`;

export const assistantResetTranscript = (clientId: string, hostKey: string, operationId: string) =>
  `VolturaAir ai-assistant:reset:v1\n${clientId}\n${hostKey}\n${operationId}`;
