using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private Task HandlePresentationCommandAsync(
        WebSocket socket,
        string clientId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var operationId = GetString(message, "operationId");
        var target = GetString(message, "target");
        var action = GetString(message, "action");
        var result = ExecutePresentationCommand(clientId, target, action);
        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.command",
            Action: $"{target}:{action}",
            Outcome: result.Succeeded ? "executed" : result.Code));

        return SendSocketAsync(socket, new
        {
            type = "presentation.command.result",
            operationId,
            target,
            action,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    private PresentationCommandResult ExecutePresentationCommand(string clientId, string target, string action)
    {
        if (!CanControlPresentations(clientId))
        {
            return new(false, "permission-denied", "Presentation control is disabled for this device on the PC.");
        }

        if (!PresentationCommands.TryResolve(target, action, out var shortcut))
        {
            return new(false, "unsupported-action", "That control is not available for the selected presentation target.");
        }

        try
        {
            return _inputDispatcher.DispatchShortcut(shortcut.Key, shortcut.Modifiers) switch
            {
                InputDispatchOutcome.Executed => new(true, null, shortcut.ResultMessage),
                InputDispatchOutcome.Blocked => new(false, "host-ui-blocked", "Switch focus from Voltura Air to the presentation, then try again."),
                _ => new(false, "input-failed", "Windows did not complete the presentation command. Try again.")
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                ClientId: clientId,
                Action: "presentation_input",
                Outcome: "failed",
                Detail: exception.Message));
            return new(false, "input-failed", "Windows did not accept the presentation command. Try again.");
        }
    }
}
