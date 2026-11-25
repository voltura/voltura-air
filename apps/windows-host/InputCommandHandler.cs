using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class InputCommandHandler(
    InputDispatcher inputDispatcher,
    ISystemPowerController powerController,
    HostCommandLog commandLog,
    WebSocketTransport transport)
{
    public async Task<bool> HandleAsync(
        WebSocket socket,
        ValidatedInputCommand command,
        string clientId,
        bool allowHostApplicationControl,
        CancellationToken cancellationToken)
    {
        var sequence = command.Sequence;
        var messageType = command.Type;

        try
        {
            _ = powerController.DismissBlackoutIfActive();
            var (handled, dispatchOutcome) = await inputDispatcher.DispatchAsync(
                command,
                allowHostApplicationControl,
                cancellationToken);
            if (!handled)
            {
                await SendErrorAsync(socket, sequence, "VAIR-INPUT-UNSUPPORTED", "Unsupported input message.", cancellationToken);
                return false;
            }

            if (HostCommandLog.ShouldRecord(messageType))
            {
                commandLog.Outcome(
                    clientId,
                    messageType,
                    messageType switch
                    {
                        "pointer.button" => "pointer_button",
                        "keyboard.text" => "text_input",
                        _ => HostCommandLog.GetAction(messageType, command)
                    },
                    dispatchOutcome switch
                    {
                        InputDispatchOutcome.Executed => "executed",
                        InputDispatchOutcome.Blocked => "blocked",
                        _ => "failed"
                    });
            }

            if (dispatchOutcome == InputDispatchOutcome.Failed)
            {
                await SendErrorAsync(
                    socket,
                    sequence,
                    "VAIR-INPUT-DISPATCH-FAILED",
                    "Windows did not complete this input action.",
                    cancellationToken);
                return true;
            }

            await SendAckAsync(socket, sequence, cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            InputDispatchDiagnostics.Write(messageType, ex);
            await SendErrorAsync(
                socket,
                sequence,
                ex is InputDispatchException ? "VAIR-INPUT-NATIVE-SEND-FAILED" : "VAIR-INPUT-DISPATCH-FAILED",
                "Windows did not accept this input action. Try again.",
                cancellationToken);
        }

        return true;
    }

    private Task SendAckAsync(WebSocket socket, long? sequence, CancellationToken cancellationToken) =>
        sequence.HasValue
            ? transport.SendAsync(socket, new { type = "input.ack", seq = sequence.Value }, cancellationToken)
            : Task.CompletedTask;

    private Task SendErrorAsync(
        WebSocket socket,
        long? sequence,
        string code,
        string message,
        CancellationToken cancellationToken) => sequence.HasValue
            ? transport.SendAsync(socket, new { type = "input.error", seq = sequence.Value, code, message }, cancellationToken)
            : transport.SendAsync(socket, new { type = "input.error", code, message }, cancellationToken);
}

internal static class InputDispatchDiagnostics
{
    public static void Write(string type, Exception exception) =>
        Console.Error.WriteLine(Format(type, exception));

    internal static string Format(string type, Exception exception)
    {
        if (exception is InputDispatchException inputException)
        {
            return FormattableString.Invariant(
                $"Voltura Air input dispatch failed: type={type}, requested={inputException.RequestedCount}, accepted={inputException.AcceptedCount}, win32={inputException.Win32Error}, cleanupAttempted={inputException.CleanupAttempted}, cleanupSucceeded={inputException.CleanupSucceeded}");
        }

        return $"Voltura Air input dispatch failed: type={type}, exception={exception.GetType().Name}";
    }
}
