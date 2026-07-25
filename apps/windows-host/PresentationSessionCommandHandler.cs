using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class PresentationSessionCommandHandler(
    PairingManager pairingManager,
    HostStatusPayloadFactory statusFactory,
    PowerPointPresentationSessionService session,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    internal async Task HandleAsync(
        WebSocket socket,
        string clientId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(message, "operationId");
        var action = ProtocolMessageFields.GetString(message, "action");
        SessionOperationResult result;
        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            result = new(false, "feature-disabled", "Presentation tracking is disabled on the PC.");
        }
        else if (!statusFactory.CanControlPresentations(clientId))
        {
            result = new(false, "permission-denied", "Presentation tracking is disabled for this device.");
        }
        else
        {
            result = await ExecuteAsync(
                clientId,
                action,
                message,
                cancellationToken).ConfigureAwait(false);
        }

        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.session",
            Action: action,
            Outcome: result.Succeeded ? "executed" : result.Code));
        await transport.SendAsync(socket, new
        {
            type = "presentation.session.result",
            operationId,
            action,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SessionOperationResult> ExecuteAsync(
        string clientId,
        string action,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        if (action == "start")
        {
            var runtimeId = ProtocolMessageFields.GetString(
                message,
                "runtimePresentationId");
            var candidates = string.IsNullOrEmpty(runtimeId)
                ? statusFactory.GetPowerPointSnapshot().Presentations.Where(item => item.IsPresenting).ToArray()
                : [.. statusFactory.GetPowerPointSnapshot().Presentations.Where(item =>
                    item.IsPresenting &&
                    string.Equals(item.RuntimePresentationId, runtimeId, StringComparison.Ordinal))];
            if (candidates.Length != 1)
            {
                return new(
                    false,
                    candidates.Length == 0 ? "powerpoint-not-presenting" : "powerpoint-selection-required",
                    candidates.Length == 0
                        ? "Start the selected PowerPoint slideshow before tracking it."
                        : "Choose which running PowerPoint presentation to track.");
            }

            var deviceName = pairingManager.GetDeviceName(clientId);
            return deviceName is null
                ? new(false, "device-revoked", "This device is no longer paired with the PC.")
                : session.Start(clientId, deviceName, candidates[0]);
        }

        if (action == "break")
        {
            return message.GetProperty("enabled").GetBoolean()
                ? session.SetBreak(clientId, enabled: true)
                : await session.ResumeAsync(
                    clientId,
                    cancellationToken).ConfigureAwait(false);
        }

        return await session.CompleteAsync(
            clientId,
            save: action == "save",
            cancellationToken).ConfigureAwait(false);
    }
}
