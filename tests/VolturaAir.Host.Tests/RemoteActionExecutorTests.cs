using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RemoteActionExecutorTests
{
    [Theory]
    [InlineData(RemoteLaunchActions.OpenYoutube, true, false)]
    [InlineData(RemoteLaunchActions.StartOrActivateKodi, false, true)]
    public async Task RoutesSupportedActionToItsOwner(string action, bool youtubeCalled, bool kodiCalled)
    {
        var youtube = new RecordingRemoteLaunchAction(true);
        var kodi = new RecordingRemoteLaunchAction(true);
        var executor = new RemoteActionExecutor(youtube, kodi);

        var result = await executor.TryExecuteAsync(action, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(youtubeCalled ? 1 : 0, youtube.CallCount);
        Assert.Equal(kodiCalled ? 1 : 0, kodi.CallCount);
    }

    [Fact]
    public async Task RejectsUnsupportedActionWithoutInvokingPlatformOwners()
    {
        var youtube = new RecordingRemoteLaunchAction(true);
        var kodi = new RecordingRemoteLaunchAction(true);
        var executor = new RemoteActionExecutor(youtube, kodi);

        var result = await executor.TryExecuteAsync("cmd.exe", CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, youtube.CallCount);
        Assert.Equal(0, kodi.CallCount);
    }

    [Fact]
    public async Task PropagatesCancellationToSelectedOwner()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var youtube = new CancellationAwareRemoteLaunchAction();
        var executor = new RemoteActionExecutor(youtube, new RecordingRemoteLaunchAction(true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.TryExecuteAsync(RemoteLaunchActions.OpenYoutube, cancellation.Token));
    }

    private sealed class RecordingRemoteLaunchAction(bool result) : IRemoteLaunchAction
    {
        public int CallCount { get; private set; }

        public Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class CancellationAwareRemoteLaunchAction : IRemoteLaunchAction
    {
        public Task<bool> ExecuteAsync(CancellationToken cancellationToken) =>
            Task.FromCanceled<bool>(cancellationToken);
    }
}
