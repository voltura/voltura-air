using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class ScreenViewCommandHandler(
    ScreenViewCoordinator coordinator,
    WebSocketTransport transport)
{
    public void ClientDisconnected(string clientId) => coordinator.Stop(clientId);

    public Task GetSourcesAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        ScreenViewSourcesResult result = coordinator.GetSources(clientId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.sources.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            sources = result.Sources
        }, cancellationToken);
    }

    public async Task StartAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string displayId,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.StartAsync(clientId, operationId, displayId, clientSignature, cancellationToken).ConfigureAwait(false);
        await transport.SendAsync(socket, new
        {
            type = "screen.view.start.result",
            operationId,
            displayId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            offerSdp = result.OfferSdp,
            hostSignature = result.HostSignature
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task AnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        ScreenViewOperationResult result = coordinator.CompleteAnswer(clientId, operationId, answerSdp, clientSignature);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.answer.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    public Task StopAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        coordinator.Stop(clientId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.stop.result",
            operationId,
            succeeded = true,
            code = "stopped",
            message = "Screen viewing stopped."
        }, cancellationToken);
    }

    public Task SetSourceAsync(WebSocket socket, string clientId, string operationId, string displayId, CancellationToken cancellationToken)
    {
        ScreenViewOperationResult result = coordinator.SetSource(clientId, displayId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.source.result",
            operationId,
            displayId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }
}
