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
    private int _remoteLaunchInFlight;

    public void HandleRemoteLaunch(string clientId, string action, CancellationToken cancellationToken)
    {
        if (!statusFactory.CanLaunchRemoteApps(clientId))
        {
            commandLog.Outcome(clientId, "remote.launch", action, "blocked");
            return;
        }

        if (Interlocked.CompareExchange(ref _remoteLaunchInFlight, 1, 0) != 0)
        {
            commandLog.Outcome(clientId, "remote.launch", action, "busy");
            return;
        }

        _ = ExecuteRemoteLaunchAsync(clientId, action, cancellationToken);
    }

    private async Task ExecuteRemoteLaunchAsync(
        string clientId,
        string action,
        CancellationToken cancellationToken)
    {
        var outcome = "failed";
        try
        {
            outcome = await remoteActionExecutor.TryExecuteAsync(action, cancellationToken).ConfigureAwait(false)
                ? "executed"
                : "failed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
        }
        catch (Exception)
        {
            outcome = "failed";
        }
        finally
        {
            commandLog.Outcome(clientId, "remote.launch", action, outcome);
            Interlocked.Exchange(ref _remoteLaunchInFlight, 0);
        }
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
