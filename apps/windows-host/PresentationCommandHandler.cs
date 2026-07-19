using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class PresentationCommandHandler(
    InputDispatcher inputDispatcher,
    HostStatusPayloadFactory statusFactory,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public Task HandleAsync(
        WebSocket socket,
        string clientId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(message, "operationId");
        var target = ProtocolMessageFields.GetString(message, "target");
        var action = ProtocolMessageFields.GetString(message, "action");
        var result = Execute(clientId, target, action);
        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.command",
            Action: $"{target}:{action}",
            Outcome: result.Succeeded ? "executed" : result.Code));

        return transport.SendAsync(socket, new
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

    private PresentationCommandResult Execute(string clientId, string target, string action)
    {
        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            return new(false, "feature-disabled", "Presentation is an alpha feature and is disabled on the PC.");
        }

        if (!statusFactory.CanControlPresentations(clientId))
        {
            return new(false, "permission-denied", "Presentation control is disabled for this device on the PC.");
        }

        if (!PresentationCommands.TryResolve(target, action, out var shortcut))
        {
            return new(false, "unsupported-action", "That control is not available for the selected presentation target.");
        }

        try
        {
            return inputDispatcher.DispatchShortcut(shortcut.Key, shortcut.Modifiers) switch
            {
                InputDispatchOutcome.Executed => new(true, null, shortcut.ResultMessage),
                InputDispatchOutcome.Blocked => new(false, "host-ui-blocked", "Switch focus from Voltura Air to the presentation, then try again."),
                _ => new(false, "input-failed", "Windows did not complete the presentation command. Try again.")
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            appLog.Write(new AppLogEntry(
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
