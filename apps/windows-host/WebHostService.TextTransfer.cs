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
            var outcome = _inputDispatcher.TransferText(
                GetString(root, "text"),
                root.GetProperty("sendEnter").GetBoolean());

            if (outcome == InputDispatchOutcome.Blocked)
            {
                LogCommandOutcome(clientId, "text.send", "text_transfer", "blocked");
                await SendTextTransferResultAsync(
                    socket,
                    operationId,
                    false,
                    "VAIR-TEXT-HOST-FOCUSED",
                    "Text was not sent because the Voltura Air host window has focus. Select the destination application and try again.",
                    cancellationToken);
                return;
            }

            LogCommandOutcome(clientId, "text.send", "text_transfer", "executed");
            await SendTextTransferResultAsync(
                socket,
                operationId,
                true,
                null,
                "Text sent successfully.",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            WriteInputDispatchDiagnostic("text.send", root, ex);
            LogCommandOutcome(clientId, "text.send", "text_transfer", "failed");
            await SendTextTransferResultAsync(
                socket,
                operationId,
                false,
                ex is InputDispatchException ? "VAIR-TEXT-NATIVE-SEND-FAILED" : "VAIR-TEXT-DELIVERY-FAILED",
                "Windows did not accept the complete text. Check the destination before retrying.",
                cancellationToken);
        }
    }

    private Task SendTextTransferResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string? code,
        string message,
        CancellationToken cancellationToken) =>
        SendSocketAsync(socket, new { type = "text.send.result", operationId, succeeded, code, message }, cancellationToken);
}
