using System.Net.WebSockets;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleClipboardReadAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        var result = CanReadClipboard(clientId)
            ? _clipboardTextReader.ReadText()
            : new ClipboardTextReadResult(false, null, "VAIR-CLIPBOARD-PERMISSION-DENIED", "PC clipboard access is disabled for this device on the PC.");

        LogCommandOutcome(clientId, "clipboard.get", "read_pc_clipboard", result.Succeeded ? "succeeded" : result.Code ?? "failed");
        await SendSocketAsync(socket, new
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
