import type { ClientMessage } from "../protocol/messages";
import type { ControllerSocket } from "./controllerSocket";

export function trySendClientMessage(socket: ControllerSocket, payload: ClientMessage) {
  try {
    socket.send(JSON.stringify(payload));
    return true;
  } catch {
    return false;
  }
}

export function requestHostState(socket: ControllerSocket, includeAudio: boolean) {
  if (!trySendClientMessage(socket, { type: "status.get" })) {
    return false;
  }

  return !includeAudio || trySendClientMessage(socket, { type: "audio.get" });
}
