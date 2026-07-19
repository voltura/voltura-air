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
        CancellationToken cancellationToken)
    {
        var sequence = command.Sequence;
        var messageType = command.Type;

        try
        {
            _ = powerController.DismissBlackoutIfActive();
            if (!inputDispatcher.Dispatch(command, out var dispatchOutcome))
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
            InputDispatchDiagnostics.Write(command, ex);
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
    public static void Write(ValidatedInputCommand command, Exception exception) =>
        Write(command.Type, command.Key, string.Join("+", command.Modifiers), exception);

    public static void Write(string type, string? key, string modifiers, Exception exception)
    {
        if (exception is InputDispatchException inputException)
        {
            Console.Error.WriteLine(
                "Voltura Air input dispatch failed: type={0}, key={1}, modifiers={2}, requested={3}, accepted={4}, win32={5}, cleanupAttempted={6}, cleanupSucceeded={7}",
                type,
                key,
                modifiers,
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
