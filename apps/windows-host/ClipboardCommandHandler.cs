using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class ClipboardCommandHandler(
    IClipboardTextReader clipboardTextReader,
    HostStatusPayloadFactory statusFactory,
    HostCommandLog commandLog,
    WebSocketTransport transport)
{
    private static readonly ClipboardTextReadResult PermissionDenied = new(
        false, null, "VAIR-CLIPBOARD-PERMISSION-DENIED", "PC clipboard access is disabled for this device on the PC.");

    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var read = statusFactory.CanReadClipboard(clientId)
            ? clipboardTextReader.ReadText()
            : PermissionDenied;

        _ = await transport.TrySendAsync(socket, () =>
        {
            var result = statusFactory.CanReadClipboard(clientId) ? read : PermissionDenied;
            commandLog.Outcome(clientId, "clipboard.get", "read_pc_clipboard", result.Succeeded ? "succeeded" : result.Code ?? "failed");
            return new
            {
                type = "clipboard.get.result",
                operationId,
                succeeded = result.Succeeded,
                code = result.Code,
                message = result.Message,
                text = result.Text
            };
        }, cancellationToken);
    }
}
