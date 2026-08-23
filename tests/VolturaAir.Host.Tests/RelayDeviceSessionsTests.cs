using System.Net.WebSockets;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayDeviceSessionsTests
{
    [Fact]
    public async Task CloseAndDrainWaitsForSessionCleanup()
    {
        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new RelayDeviceSessions(async (_, _, _, _) =>
        {
            entered.TrySetResult(null);
            await release.Task;
        }, () => Assert.Fail("The session handler should not fail."), _ => { }, _ => { });
        using var socket = CreateSocket();

        Assert.True(sessions.TryStart(Guid.NewGuid(), socket, [], CancellationToken.None));
        await entered.Task;
        var draining = sessions.CloseAndDrainAsync();

        Assert.False(draining.IsCompleted);
        release.TrySetResult(null);
        await draining;
        Assert.Equal(0, sessions.Count);
    }

    [Fact]
    public async Task DisconnectCancelsAndDrainsTheOwnedHandler()
    {
        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionId = Guid.NewGuid();
        var sessions = new RelayDeviceSessions(async (_, _, _, cancellationToken) =>
        {
            entered.TrySetResult(null);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }, () => Assert.Fail("Cancellation should not be logged as a failure."), _ => { }, _ => { });
        using var socket = CreateSocket();

        Assert.True(sessions.TryStart(sessionId, socket, [], CancellationToken.None));
        await entered.Task;
        sessions.Disconnect(sessionId);
        await sessions.CloseAndDrainAsync();

        Assert.Equal(0, sessions.Count);
    }

    [Fact]
    public async Task RejectedBinaryDeliveryClosesAndDrainsTheRelayDevice()
    {
        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? closeRequested = null;
        var sessionId = Guid.NewGuid();
        var sessions = new RelayDeviceSessions(async (webSocket, _, _, cancellationToken) =>
        {
            entered.TrySetResult(null);
            var buffer = new byte[32];
            _ = await webSocket.ReceiveAsync(buffer, cancellationToken);
        }, () => Assert.Fail("A rejected relay frame should close cleanly."), _ => { }, id => closeRequested = id);
        using var socket = CreateSocket(sessionId);

        Assert.True(sessions.TryStart(sessionId, socket, [], CancellationToken.None));
        await entered.Task;
        Assert.True(sessions.TryDeliver(sessionId, [1, 2, 3], isBinary: true));
        await sessions.CloseAndDrainAsync();

        Assert.Equal(sessionId, closeRequested);
        Assert.Equal(0, sessions.Count);
    }

    [Fact]
    public async Task FullDeviceQueueRequestsCloseWithoutWaitingForUpstreamSend()
    {
        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeRequested = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionId = Guid.NewGuid();
        var sessions = new RelayDeviceSessions(async (_, _, _, _) =>
        {
            entered.TrySetResult(null);
            await release.Task;
        }, () => Assert.Fail("A queue overload should close cleanly."), _ => { }, id => closeRequested.TrySetResult(id));
        using var socket = CreateSocket(sessionId);

        Assert.True(sessions.TryStart(sessionId, socket, [], CancellationToken.None));
        await entered.Task;
        for (var index = 0; index < 32; index++)
        {
            Assert.True(sessions.TryDeliver(sessionId, [1], isBinary: false));
        }

        Assert.True(sessions.TryDeliver(sessionId, [1], isBinary: false));
        Assert.True(closeRequested.Task.IsCompletedSuccessfully);
        Assert.Equal(sessionId, await closeRequested.Task);

        release.TrySetResult(null);
        await sessions.CloseAndDrainAsync();
        Assert.Equal(0, sessions.Count);
    }

    [Fact]
    public async Task RejectsDuplicateAndOverCapacitySessions()
    {
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new RelayDeviceSessions((_, _, authenticated, _) =>
        {
            authenticated();
            return release.Task;
        }, () => Assert.Fail("The session handler should not fail."), _ => { }, _ => { });
        var sockets = new List<RelayVirtualWebSocket>();
        try
        {
            var firstId = Guid.NewGuid();
            var first = CreateSocket();
            sockets.Add(first);
            Assert.True(sessions.TryStart(firstId, first, [], CancellationToken.None));
            using var duplicate = CreateSocket();
            Assert.False(sessions.TryStart(firstId, duplicate, [], CancellationToken.None));

            for (var index = 1; index < 64; index++)
            {
                var socket = CreateSocket();
                sockets.Add(socket);
                Assert.True(sessions.TryStart(Guid.NewGuid(), socket, [], CancellationToken.None));
            }

            using var overflow = CreateSocket();
            Assert.False(sessions.TryStart(Guid.NewGuid(), overflow, [], CancellationToken.None));
            Assert.Equal(64, sessions.Count);
            release.TrySetResult(null);
            await sessions.CloseAndDrainAsync();
        }
        finally
        {
            release.TrySetResult(null);
            await sessions.CloseAndDrainAsync();
            foreach (var socket in sockets) socket.Dispose();
        }
    }

    [Fact]
    public void UsesStableRelaySourceKeysWithoutSharingLegacySessions()
    {
        var source = Enumerable.Range(0, 16).Select(Convert.ToByte).ToArray();
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();

        Assert.True(RelayDeviceSessions.TryCreateRateLimitKey(firstSession, source, out var first));
        Assert.True(RelayDeviceSessions.TryCreateRateLimitKey(secondSession, source, out var sameSource));
        Assert.Equal(first, sameSource);

        Assert.True(RelayDeviceSessions.TryCreateRateLimitKey(firstSession, [], out var firstLegacy));
        Assert.True(RelayDeviceSessions.TryCreateRateLimitKey(secondSession, [], out var secondLegacy));
        Assert.NotEqual(firstLegacy, secondLegacy);
        Assert.False(RelayDeviceSessions.TryCreateRateLimitKey(firstSession, new byte[15], out _));
    }

    private static RelayVirtualWebSocket CreateSocket(
        Guid? sessionId = null,
        Func<RelayEnvelope, CancellationToken, Task>? send = null) =>
        new(sessionId ?? Guid.NewGuid(), new string('r', 22), send ?? ((_, _) => Task.CompletedTask));
}
