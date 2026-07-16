using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleTextTransferAsync(
        WebSocket socket,
        string clientId,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var operationId = GetString(root, "operationId");
        try
        {
            _ = _powerController.DismissBlackoutIfActive();
            var outcome = await _textDestinationService.DeliverAsync(
                GetString(root, "text"),
                root.GetProperty("sendEnter").GetBoolean(), cancellationToken);

            LogCommandOutcome(clientId, "text.send", "text_transfer", outcome.Succeeded ? outcome.Kind : "failed");
            await SendTextTransferResultAsync(
                socket,
                operationId,
                outcome.Succeeded,
                outcome.Code,
                outcome.Message,
                outcome.Kind,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            WriteInputDispatchDiagnostic("text.send", null, string.Empty, ex);
            LogCommandOutcome(clientId, "text.send", "text_transfer", "failed");
            await SendTextTransferResultAsync(
                socket,
                operationId,
                false,
                ex is InputDispatchException ? "VAIR-TEXT-NATIVE-SEND-FAILED" : "VAIR-TEXT-DELIVERY-FAILED",
                "Windows did not accept the complete text. Check the destination before retrying.",
                "typed",
                cancellationToken);
        }
    }

    private Task SendTextTransferResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string? code,
        string message,
        string deliveryKind,
        CancellationToken cancellationToken) =>
        SendSocketAsync(socket, new { type = "text.send.result", operationId, succeeded, code, message, deliveryKind }, cancellationToken);
}
