using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class AwakeCommandHandler(
    IAwakeService awakeService,
    HostStatusPayloadFactory statusFactory,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public async Task HandleAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!statusFactory.CanControlAwake(clientId))
        {
            await SendResultAsync(socket, operationId, enabled, false, "VAIR-AWAKE-DENIED", "Keep awake control is disabled by the PC host.", cancellationToken);
            LogAction(clientId, enabled, "permission_denied");
            return;
        }

        var result = enabled
            ? await awakeService.SetIndefiniteAsync(cancellationToken).ConfigureAwait(false)
            : await awakeService.SetOffAsync(cancellationToken).ConfigureAwait(false);
        LogAction(clientId, enabled, result.Succeeded ? "succeeded" : "execution_failed", result.Error);
        await SendResultAsync(
            socket,
            operationId,
            enabled,
            result.Succeeded,
            result.Succeeded ? null : "VAIR-AWAKE-EXECUTION-FAILED",
            result.Succeeded
                ? enabled ? "The PC will stay awake using the host screen setting." : "The selected Windows power plan is active."
                : result.Error ?? "Windows rejected the keep-awake request.",
            cancellationToken);
    }

    private Task SendResultAsync(
        WebSocket socket,
        string operationId,
        bool enabled,
        bool succeeded,
        string? code,
        string message,
        CancellationToken cancellationToken)
    {
        appLog.Write(new AppLogEntry(
            Event: "response_sent",
            Source: "remote_client",
            MessageType: "awake.result",
            Action: enabled ? "enable" : "disable",
            Outcome: succeeded ? "succeeded" : "failed",
            Code: code));
        return transport.SendAsync(socket, new { type = "awake.result", operationId, enabled, succeeded, code, message }, cancellationToken);
    }

    private void LogAction(string clientId, bool enabled, string outcome, string? detail = null)
    {
        appLog.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "awake.set",
            Action: enabled ? "enable" : "disable",
            Outcome: outcome,
            Detail: detail));
    }
}
