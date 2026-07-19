using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class ClipboardCommandHandler(
    IClipboardTextReader clipboardTextReader,
    HostStatusPayloadFactory statusFactory,
    HostCommandLog commandLog,
    WebSocketTransport transport)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var result = statusFactory.CanReadClipboard(clientId)
            ? clipboardTextReader.ReadText()
            : new ClipboardTextReadResult(false, null, "VAIR-CLIPBOARD-PERMISSION-DENIED", "PC clipboard access is disabled for this device on the PC.");

        commandLog.Outcome(clientId, "clipboard.get", "read_pc_clipboard", result.Succeeded ? "succeeded" : result.Code ?? "failed");
        await transport.SendAsync(socket, new
        {
            type = "clipboard.get.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            text = result.Text
        }, cancellationToken);
    }
}
