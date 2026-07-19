using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class ExternalActionCommandHandler(
    IRemoteActionExecutor remoteActionExecutor,
    IAppLaunchService appLaunchService,
    IUrlOpenService urlOpenService,
    HostStatusPayloadFactory statusFactory,
    HostCommandLog commandLog,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public async Task HandleRemoteLaunchAsync(string clientId, string action, CancellationToken cancellationToken)
    {
        var outcome = "blocked";
        if (statusFactory.CanLaunchRemoteApps(clientId))
        {
            outcome = await remoteActionExecutor.TryExecuteAsync(action, cancellationToken) ? "executed" : "failed";
        }

        commandLog.Outcome(clientId, "remote.launch", action, outcome);
    }

    public Task HandleAppLaunchAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string actionId,
        CancellationToken cancellationToken)
    {
        var result = statusFactory.CanLaunchRemoteApps(clientId)
            ? appLaunchService.Execute(actionId)
            : new AppLaunchExecutionResult(false, "permission-denied", "Application launch is disabled for this device on the PC.");

        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "app.launch",
            Action: actionId,
            Outcome: result.Succeeded ? "succeeded" : result.Code));

        return transport.SendAsync(socket, new
        {
            type = "app.launch.result",
            operationId,
            actionId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    public Task HandleUrlOpenAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string url,
        CancellationToken cancellationToken)
    {
        var result = statusFactory.CanOpenUrls(clientId)
            ? urlOpenService.Execute(url)
            : new UrlOpenExecutionResult(false, "permission-denied", "Opening web addresses is disabled for this device on the PC.");

        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "url.open",
            Action: "open_url",
            Outcome: result.Succeeded ? "accepted" : result.Code));

        return transport.SendAsync(socket, new
        {
            type = "url.open.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            normalizedUrl = result.NormalizedUrl
        }, cancellationToken);
    }
}
