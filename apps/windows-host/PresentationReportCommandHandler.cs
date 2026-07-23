using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class PresentationReportCommandHandler(
    PairingManager pairingManager,
    HostStatusPayloadFactory statusFactory,
    IPresentationReportStore reportStore,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(message, "operationId");
        var reportId = ProtocolMessageFields.GetString(message, "reportId");
        PresentationReportSaveResult result;
        if (!AppDeveloperSettings.EnableAlphaFeatures())
        {
            result = new(false, "feature-disabled", "Presentation reporting is disabled on the PC.", reportId);
        }
        else if (!statusFactory.CanControlPresentations(clientId))
        {
            result = new(false, "permission-denied", "Saving presentation data is disabled for this device on the PC.", reportId);
        }
        else if (!PresentationReportProtocol.TryParse(message, out var request))
        {
            result = new(false, "invalid-report", "The presentation data was invalid or exceeded its limits.", reportId);
        }
        else if (pairingManager.GetDeviceName(clientId) is not { } deviceName)
        {
            result = new(false, "device-revoked", "This device is no longer paired with the PC.", reportId);
        }
        else
        {
            result = await reportStore.SaveAsync(request, clientId, deviceName, cancellationToken);
        }

        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "presentation.report.save",
            Action: "save",
            Outcome: result.Succeeded ? "executed" : result.Code));

        await transport.SendAsync(socket, new
        {
            type = "presentation.report.save.result",
            operationId,
            reportId = result.ReportId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }
}
