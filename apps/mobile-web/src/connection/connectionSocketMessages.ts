import type { ClientMessage } from "../protocol";

export function trySendClientMessage(socket: WebSocket, payload: ClientMessage) {
  try {
    socket.send(JSON.stringify(payload));
    return true;
  } catch {
    return false;
  }
}

export function requestHostState(socket: WebSocket, includeAudio: boolean) {
  if (!trySendClientMessage(socket, { type: "status.get" })) {
    return false;
  }

  return !includeAudio || trySendClientMessage(socket, { type: "audio.get" });
}
