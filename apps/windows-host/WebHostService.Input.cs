using System.Net.WebSockets;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleInputMessageAsync(
        WebSocket socket,
        ValidatedInputCommand command,
        string clientId,
        CancellationToken cancellationToken)
    {
        var sequence = command.Sequence;
        var messageType = command.Type;

        try
        {
            _ = _powerController.DismissBlackoutIfActive();
            if (!_inputDispatcher.Dispatch(command, out var dispatchOutcome))
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

            if (ShouldLogClientCommand(messageType))
            {
                LogCommandOutcome(
                    clientId,
                    messageType,
                    messageType switch
                    {
                        "pointer.button" => "pointer_button",
                        "keyboard.text" => "text_input",
                        _ => GetLoggedCommandAction(messageType, command)
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
                await SendInputErrorAsync(
                    socket,
                    sequence,
                    "VAIR-INPUT-DISPATCH-FAILED",
                    "Windows did not complete this input action.",
                    cancellationToken);
                return;
            }

            await SendInputAckAsync(socket, sequence, cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            WriteInputDispatchDiagnostic(command, ex);
            var code = ex is InputDispatchException
                ? "VAIR-INPUT-NATIVE-SEND-FAILED"
                : "VAIR-INPUT-DISPATCH-FAILED";
            await SendInputErrorAsync(
                socket,
                sequence,
                code,
                "Windows did not accept this input action. Try again.",
                cancellationToken);
        }
    }

    private static void WriteInputDispatchDiagnostic(ValidatedInputCommand command, Exception exception)
    {
        WriteInputDispatchDiagnostic(command.Type, command.Key, string.Join("+", command.Modifiers), exception);
    }

    private static void WriteInputDispatchDiagnostic(string type, string? key, string modifiers, Exception exception)
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
