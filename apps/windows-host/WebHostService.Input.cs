using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleInputMessageAsync(
        WebSocket socket,
        JsonElement root,
        string clientId,
        CancellationToken cancellationToken)
    {
        var sequence = TryGetInputSequence(root, out var parsedSequence) ? parsedSequence : (long?)null;
        var messageType = ClientMessageValidator.TryReadType(root, out var parsedMessageType)
            ? parsedMessageType ?? "unknown"
            : "unknown";

        try
        {
            _ = _powerController.DismissBlackoutIfActive();
            if (!_inputDispatcher.Dispatch(root))
            {
                await SendInputErrorAsync(socket, sequence, "VAIR-INPUT-UNSUPPORTED", "Unsupported input message.", cancellationToken);
                await CloseAuthenticatedSocketAsync(
                    socket,
                    clientId,
                    "Unsupported input message",
                    WebSocketCloseStatus.PolicyViolation,
                    cancellationToken);
                return;
            }

            await SendInputAckAsync(socket, sequence, cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            var code = ex is InputDispatchException ? "VAIR-INPUT-NATIVE-SEND-FAILED" : "VAIR-INPUT-DISPATCH-FAILED";
            const string message = "Windows did not accept this input action. Try again.";
            WriteInputDispatchDiagnostic(messageType, root, ex);
            await SendInputErrorAsync(socket, sequence, code, message, cancellationToken);
        }
    }

    private static void WriteInputDispatchDiagnostic(string type, JsonElement root, Exception exception)
    {
        if (exception is InputDispatchException inputException)
        {
            Console.Error.WriteLine(
                "Voltura Air input dispatch failed: type={0}, key={1}, modifiers={2}, requested={3}, accepted={4}, win32={5}, cleanupAttempted={6}, cleanupSucceeded={7}",
                type,
                GetString(root, "key"),
                GetModifierDiagnostic(root),
                inputException.RequestedCount,
                inputException.AcceptedCount,
                inputException.Win32Error,
                inputException.CleanupAttempted,
                inputException.CleanupSucceeded);
            return;
        }

        Console.Error.WriteLine("Voltura Air input dispatch failed: type={0}, error={1}", type, exception.Message);
    }
}
