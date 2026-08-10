using System.Diagnostics;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

public sealed class FileManagerSessionOpenCoordinatorTests
{
    [Fact]
    public async Task SlowOpenDoesNotHoldDispatchAndReconnectReceivesPendingResult()
    {
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpen = new TaskCompletionSource<FileManagerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSent = false;
        var newSent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var openCount = 0;
        using var oldSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) =>
        {
            oldSent = true;
            return Task.CompletedTask;
        });
        using var newSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('s', 22), (envelope, _) =>
        {
            newSent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", oldSocket);
        await using var coordinator = new FileManagerSessionOpenCoordinator(
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref openCount);
                openStarted.TrySetResult();
                return await releaseOpen.Task.WaitAsync(cancellationToken);
            },
            _ => true,
            _ => { },
            transport);

        Task firstDispatch = coordinator.StartAsync(oldSocket, "client-a", "operation-old", CancellationToken.None);
        await openStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(firstDispatch.IsCompletedSuccessfully);

        coordinator.ClientDisconnected("client-a", oldSocket);
        transport.Unregister("client-a", oldSocket);
        transport.Register("client-a", newSocket);
        Task reconnectDispatch = coordinator.StartAsync(newSocket, "client-a", "operation-new", CancellationToken.None);
        Assert.True(reconnectDispatch.IsCompletedSuccessfully);

        releaseOpen.TrySetResult(CreateSnapshot());
        RelayEnvelope envelope = await newSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(envelope.Payload);

        Assert.False(oldSent);
        Assert.Equal(1, openCount);
        Assert.Equal("file.session.open.result", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("operation-new", document.RootElement.GetProperty("operationId").GetString());
        Assert.True(document.RootElement.GetProperty("succeeded").GetBoolean());
        transport.Unregister("client-a", newSocket);
    }

    [Fact]
    public async Task ConcurrentSocketsReceiveTheSamePendingSession()
    {
        var releaseOpen = new TaskCompletionSource<FileManagerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var openCount = 0;
        using var firstSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (envelope, _) =>
        {
            firstSent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var secondSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('s', 22), (envelope, _) =>
        {
            secondSent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", firstSocket);
        transport.Register("client-a", secondSocket);
        await using var coordinator = new FileManagerSessionOpenCoordinator(
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref openCount);
                return await releaseOpen.Task.WaitAsync(cancellationToken);
            },
            _ => true,
            _ => { },
            transport);

        await coordinator.StartAsync(firstSocket, "client-a", "operation-first", CancellationToken.None);
        await coordinator.StartAsync(secondSocket, "client-a", "operation-second", CancellationToken.None);
        releaseOpen.TrySetResult(CreateSnapshot());

        RelayEnvelope firstEnvelope = await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RelayEnvelope secondEnvelope = await secondSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var firstDocument = JsonDocument.Parse(firstEnvelope.Payload);
        using var secondDocument = JsonDocument.Parse(secondEnvelope.Payload);

        Assert.Equal(1, openCount);
        Assert.Equal("operation-first", firstDocument.RootElement.GetProperty("operationId").GetString());
        Assert.Equal("operation-second", secondDocument.RootElement.GetProperty("operationId").GetString());
        Assert.Equal(
            firstDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString(),
            secondDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString());
        transport.Unregister("client-a", firstSocket);
        transport.Unregister("client-a", secondSocket);
    }

    [Fact]
    public async Task DisconnectingOneConcurrentSocketPreservesTheOtherTarget()
    {
        var releaseOpen = new TaskCompletionSource<FileManagerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSent = false;
        using var firstSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (envelope, _) =>
        {
            firstSent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var secondSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('s', 22), (_, _) =>
        {
            secondSent = true;
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", firstSocket);
        transport.Register("client-a", secondSocket);
        await using var coordinator = new FileManagerSessionOpenCoordinator(
            (_, cancellationToken) => releaseOpen.Task.WaitAsync(cancellationToken),
            _ => true,
            _ => { },
            transport);

        await coordinator.StartAsync(firstSocket, "client-a", "operation-first", CancellationToken.None);
        await coordinator.StartAsync(secondSocket, "client-a", "operation-second", CancellationToken.None);
        coordinator.ClientDisconnected("client-a", secondSocket);
        transport.Unregister("client-a", secondSocket);
        releaseOpen.TrySetResult(CreateSnapshot());

        RelayEnvelope envelope = await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(envelope.Payload);
        Assert.Equal("operation-first", document.RootElement.GetProperty("operationId").GetString());
        Assert.False(secondSent);
        transport.Unregister("client-a", firstSocket);
    }

    [Fact]
    public async Task PermissionRevokedDuringOpenRevokesSessionAndReturnsFailure()
    {
        var releaseOpen = new TaskCompletionSource<FileManagerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canBrowse = true;
        var revoked = false;
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (envelope, _) =>
        {
            sent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", socket);
        await using var coordinator = new FileManagerSessionOpenCoordinator(
            (_, cancellationToken) => releaseOpen.Task.WaitAsync(cancellationToken),
            _ => canBrowse,
            _ => { revoked = true; },
            transport);

        await coordinator.StartAsync(socket, "client-a", "operation-a", CancellationToken.None);
        canBrowse = false;
        releaseOpen.TrySetResult(CreateSnapshot());
        RelayEnvelope envelope = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(envelope.Payload);

        Assert.True(revoked);
        Assert.False(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("permission-denied", document.RootElement.GetProperty("code").GetString());
        transport.Unregister("client-a", socket);
    }

    [Fact]
    public async Task CompletedOpenWaitsForReconnectWithoutStartingAnotherListing()
    {
        var openCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<RelayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var openCount = 0;
        using var oldSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);
        using var newSocket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('s', 22), (envelope, _) =>
        {
            sent.TrySetResult(envelope);
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", oldSocket);
        await using var coordinator = new FileManagerSessionOpenCoordinator(
            (_, _) =>
            {
                Interlocked.Increment(ref openCount);
                openCompleted.TrySetResult();
                return Task.FromResult(CreateSnapshot());
            },
            _ => true,
            _ => { },
            transport);

        coordinator.ClientDisconnected("client-a", oldSocket);
        transport.Unregister("client-a", oldSocket);
        await coordinator.StartAsync(oldSocket, "client-a", "operation-old", CancellationToken.None);
        coordinator.ClientDisconnected("client-a", oldSocket);
        await openCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        transport.Register("client-a", newSocket);
        await coordinator.StartAsync(newSocket, "client-a", "operation-new", CancellationToken.None);
        RelayEnvelope envelope = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(envelope.Payload);

        Assert.Equal(1, openCount);
        Assert.Equal("operation-new", document.RootElement.GetProperty("operationId").GetString());
        transport.Unregister("client-a", newSocket);
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsPendingOpen()
    {
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);
        using var transport = new WebSocketTransport();
        transport.Register("client-a", socket);
        var coordinator = new FileManagerSessionOpenCoordinator(
            async (_, cancellationToken) =>
            {
                openStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateSnapshot();
            },
            _ => true,
            _ => { },
            transport);

        await coordinator.StartAsync(socket, "client-a", "operation-a", CancellationToken.None);
        await openStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        transport.Unregister("client-a", socket);
    }

    [Fact]
    public async Task DisposalReturnsWithinItsBoundWhenOpenIgnoresCancellation()
    {
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpen = new TaskCompletionSource<FileManagerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = false;
        var revoked = false;
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) =>
        {
            sent = true;
            return Task.CompletedTask;
        });
        using var transport = new WebSocketTransport();
        transport.Register("client-a", socket);
        var coordinator = new FileManagerSessionOpenCoordinator(
            async (_, _) =>
            {
                openStarted.TrySetResult();
                return await releaseOpen.Task;
            },
            _ =>
            {
                permissionChecked.TrySetResult();
                return true;
            },
            _ => { revoked = true; },
            transport,
            shutdownTimeout: TimeSpan.FromMilliseconds(50));

        await coordinator.StartAsync(socket, "client-a", "operation-a", CancellationToken.None);
        await openStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        await coordinator.DisposeAsync();
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        releaseOpen.TrySetResult(CreateSnapshot());
        await Task.Delay(100);
        Assert.False(permissionChecked.Task.IsCompleted);
        Assert.False(sent);
        Assert.False(revoked);
        transport.Unregister("client-a", socket);
    }

    private static FileManagerSessionSnapshot CreateSnapshot()
    {
        var left = new FileManagerPanelPage("left", "left-revision", "C:\\", null, "drive-c", "name", false, 0, [], null);
        var right = new FileManagerPanelPage("right", "right-revision", "C:\\", null, "drive-c", "name", false, 0, [], null);
        return new FileManagerSessionSnapshot("session-a", [], [], left, right);
    }
}
