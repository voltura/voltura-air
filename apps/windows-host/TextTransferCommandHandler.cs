using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class TextTransferCommandHandler(
    ITextDestinationService textDestinationService,
    ISystemPowerController powerController,
    HostCommandLog commandLog,
    WebSocketTransport transport)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(root, "operationId");
        try
        {
            _ = powerController.DismissBlackoutIfActive();
            var outcome = await textDestinationService.DeliverAsync(
                ProtocolMessageFields.GetString(root, "text"),
                root.GetProperty("sendEnter").GetBoolean(),
                cancellationToken);

            commandLog.Outcome(clientId, "text.send", "text_transfer", outcome.Succeeded ? outcome.Kind : "failed");
            await SendResultAsync(socket, operationId, outcome.Succeeded, outcome.Code, outcome.Message, outcome.Kind, cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            InputDispatchDiagnostics.Write("text.send", null, string.Empty, ex);
            commandLog.Outcome(clientId, "text.send", "text_transfer", "failed");
            await SendResultAsync(
                socket,
                operationId,
                false,
                ex is InputDispatchException ? "VAIR-TEXT-NATIVE-SEND-FAILED" : "VAIR-TEXT-DELIVERY-FAILED",
                "Windows did not accept the complete text. Check the destination before retrying.",
                "typed",
                cancellationToken);
        }
    }

    private Task SendResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string? code,
        string message,
        string deliveryKind,
        CancellationToken cancellationToken) =>
        transport.SendAsync(socket, new { type = "text.send.result", operationId, succeeded, code, message, deliveryKind }, cancellationToken);
}
