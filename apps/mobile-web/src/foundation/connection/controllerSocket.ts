export type ControllerSocket = WebSocket | RTCDataChannel;

export function isControllerSocketOpen(socket: ControllerSocket | null | undefined): socket is ControllerSocket {
  return typeof socket?.readyState === "number" ? socket.readyState === WebSocket.OPEN : socket?.readyState === "open";
}

export function isControllerSocketConnecting(socket: ControllerSocket | null | undefined): boolean {
  return typeof socket?.readyState === "number" ? socket.readyState === WebSocket.CONNECTING : socket?.readyState === "connecting";
}

export function isControllerSocketClosingOrClosed(socket: ControllerSocket | null | undefined): boolean {
  return typeof socket?.readyState === "number"
    ? socket.readyState === WebSocket.CLOSING || socket.readyState === WebSocket.CLOSED
    : socket?.readyState === "closing" || socket?.readyState === "closed";
}
