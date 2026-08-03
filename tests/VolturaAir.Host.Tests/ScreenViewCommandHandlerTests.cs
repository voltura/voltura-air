using System.Text.Json;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewCommandHandlerTests
{
    [Fact]
    public async Task RelayTurnProvisioningDoesNotHoldTheCommandDispatchPath()
    {
        var provisioningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvisioning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var socket = new RelayVirtualWebSocket(
            Guid.NewGuid(),
            new string('r', 22),
            (envelope, _) =>
            {
                sent.TrySetResult(envelope);
                return Task.CompletedTask;
            });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", socket);
        await using var handler = new ScreenViewCommandHandler(
            null!,
            transport,
            async cancellationToken =>
            {
                provisioningStarted.TrySetResult();
                await releaseProvisioning.Task.WaitAsync(cancellationToken);
                return null;
            });

        Task dispatch = handler.StartAsync(
            socket,
            "client-a",
            "operation-a",
            "display-a",
            "signature-a",
            CancellationToken.None);
        await provisioningStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        bool completedWhileProvisioningWasBlocked = dispatch.IsCompletedSuccessfully;

        releaseProvisioning.TrySetResult();
        await dispatch;
        RelayEnvelope envelope = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(envelope.Payload);

        Assert.True(completedWhileProvisioningWasBlocked);
        Assert.Equal(RelayEnvelopeKind.Text, envelope.Kind);
        Assert.Equal("screen.view.start.result", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("turn-unavailable", document.RootElement.GetProperty("code").GetString());
        transport.Unregister("client-a", socket);
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsPendingRelayTurnProvisioning()
    {
        var provisioningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);
        using var transport = new WebSocketTransport();
        transport.Register("client-a", socket);
        var handler = new ScreenViewCommandHandler(
            null!,
            transport,
            async cancellationToken =>
            {
                provisioningStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            });

        await handler.StartAsync(
            socket,
            "client-a",
            "operation-a",
            "display-a",
            "signature-a",
            CancellationToken.None);
        await provisioningStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await handler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        transport.Unregister("client-a", socket);
    }
}
