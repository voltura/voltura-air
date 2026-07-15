using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WebSocketConnectionRegistryTests
{
    [Fact]
    public async Task UnregisterDefersSendGateDisposalUntilActiveSendCompletes()
    {
        using var registry = new WebSocketConnectionRegistry();
        using var socket = new ClientWebSocket();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register("client", socket);

        var sendTask = registry.TrySendAsync(
            socket,
            async () =>
            {
                sendStarted.SetResult();
                await releaseSend.Task;
            },
            CancellationToken.None);

        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        registry.Unregister("client", socket);
        releaseSend.SetResult();
        Assert.True(await sendTask.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Equal(0, registry.ActiveSocketCount);
        Assert.Equal(0, registry.SendGateCount);
    }

    [Fact]
    public void RepeatedRegistrationAndRemovalLeavesNoRegistryState()
    {
        using var registry = new WebSocketConnectionRegistry();
        using var socket = new ClientWebSocket();

        for (var iteration = 0; iteration < 100; iteration += 1)
        {
            registry.Register("client", socket);
            registry.Unregister("client", socket);
        }

        Assert.Equal(0, registry.ActiveSocketCount);
        Assert.Equal(0, registry.SendGateCount);
    }

    [Fact]
    public async Task SendAfterUnregisterIsRejectedWithoutUsingSocket()
    {
        using var registry = new WebSocketConnectionRegistry();
        using var socket = new ClientWebSocket();
        var sendCalled = false;
        registry.Register("client", socket);
        registry.Unregister("client", socket);

        var sent = await registry.TrySendAsync(
            socket,
            () =>
            {
                sendCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(sent);
        Assert.False(sendCalled);
    }

    [Fact]
    public void DuplicateSocketRegistrationIsRejectedWithoutChangingRegistryState()
    {
        using var registry = new WebSocketConnectionRegistry();
        using var socket = new ClientWebSocket();
        registry.Register("client", socket);

        Assert.Throws<InvalidOperationException>(() => registry.Register("client", socket));

        Assert.Equal(1, registry.ActiveSocketCount);
        Assert.Equal(1, registry.SendGateCount);
    }
}
