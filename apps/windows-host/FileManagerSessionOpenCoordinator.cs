using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class FileManagerSessionOpenCoordinator(
    Func<string, CancellationToken, Task<FileManagerSessionSnapshot>> openSession,
    Func<string, bool> canBrowseFiles,
    Action<string> revokeClient,
    WebSocketTransport transport,
    IAppLogWriter? appLog = null,
    TimeSpan? shutdownTimeout = null) : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PendingOpen> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IAppLogWriter _appLog = appLog ?? NullAppLog.Instance;
    private readonly TimeSpan _shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;
    private bool _disposed;

    public Task StartAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            var target = new OpenTarget(socket, operationId, cancellationToken);
            if (_pending.TryGetValue(clientId, out var current))
            {
                current.AddTarget(target);
                return Task.CompletedTask;
            }

            var pending = new PendingOpen(target);
            _pending.Add(clientId, pending);
            pending.Task = RunAsync(clientId, pending);
        }

        return Task.CompletedTask;
    }

    public void ClientDisconnected(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(clientId, out var pending))
            {
                pending.RemoveSocket(socket);
            }
        }
    }

    private async Task RunAsync(string clientId, PendingOpen pending)
    {
        try
        {
            var snapshot = await openSession(clientId, _lifetime.Token).ConfigureAwait(false);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!canBrowseFiles(clientId))
            {
                revokeClient(clientId);
                await DeliverAsync(
                    clientId,
                    pending,
                    target => SendFailurePayloadAsync(target, "permission-denied", "Browse and open files is disabled for this device on the PC.")).ConfigureAwait(false);
                return;
            }

            var delivered = await DeliverAsync(clientId, pending, target => SendSuccessAsync(target, snapshot)).ConfigureAwait(false);
            if (!delivered)
            {
                revokeClient(clientId);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            await DeliverAsync(
                clientId,
                pending,
                target => SendFailurePayloadAsync(target, "directory-unavailable", "The initial folders are unavailable.")).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _appLog.Write(new AppLogEntry(
                "files",
                "windows_host",
                Action: "session_open_failed",
                Outcome: "failed",
                Code: "background"));
            await DeliverAsync(
                clientId,
                pending,
                target => SendFailurePayloadAsync(target, "directory-unavailable", "The initial folders are unavailable.")).ConfigureAwait(false);
        }
        finally
        {
            RemovePending(clientId, pending);
        }
    }

    private async Task<bool> DeliverAsync(
        string clientId,
        PendingOpen pending,
        Func<OpenTarget, Task<bool>> send)
    {
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        grace.CancelAfter(ReconnectGrace);
        var delivered = false;
        while (!grace.IsCancellationRequested)
        {
            (OpenTarget[] Targets, Task Changed) state;
            lock (_gate)
            {
                if (!_pending.TryGetValue(clientId, out var current) || !ReferenceEquals(current, pending))
                {
                    return delivered;
                }
                state = pending.Read();
            }

            if (state.Targets.Length == 0)
            {
                try
                {
                    await state.Changed.WaitAsync(grace.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (grace.IsCancellationRequested)
                {
                    return delivered;
                }
                continue;
            }

            foreach (var target in state.Targets)
            {
                if (target.CancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    delivered |= await send(target).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
                {
                }
            }

            lock (_gate)
            {
                if (!_pending.TryGetValue(clientId, out var current) || !ReferenceEquals(current, pending))
                {
                    return delivered;
                }
                pending.RemoveTargets(state.Targets);
                if (pending.TargetCount == 0 && delivered)
                {
                    _pending.Remove(clientId);
                    return true;
                }
            }
        }

        return delivered;
    }

    private Task<bool> SendSuccessAsync(OpenTarget target, FileManagerSessionSnapshot snapshot) =>
        transport.TrySendAsync(target.Socket, new
        {
            type = "file.session.open.result",
            operationId = target.OperationId,
            succeeded = true,
            message = "Files opened.",
            session = snapshot
        }, target.CancellationToken);

    private Task<bool> SendFailurePayloadAsync(OpenTarget target, string code, string message) =>
        transport.TrySendAsync(target.Socket, new
        {
            type = "file.session.open.result",
            operationId = target.OperationId,
            succeeded = false,
            code,
            message
        }, target.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        Task completion;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            completion = Task.WhenAll(_pending.Values.Select(item => item.Task));
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await completion.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            _appLog.Write(new AppLogEntry(
                "files",
                "windows_host",
                Action: "session_open_shutdown",
                Outcome: "timed_out",
                Code: "background"));
        }

        if (completion.IsCompleted)
        {
            _lifetime.Dispose();
        }
        else
        {
            _ = DisposeLifetimeWhenCompletedAsync(completion);
        }
    }

    private void RemovePending(string clientId, PendingOpen pending)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(clientId, out var current) && ReferenceEquals(current, pending))
            {
                _pending.Remove(clientId);
            }
        }
    }

    private async Task DisposeLifetimeWhenCompletedAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        _lifetime.Dispose();
    }

    private sealed class PendingOpen(OpenTarget target)
    {
        private readonly List<OpenTarget> _targets = [target];
        private TaskCompletionSource _changed = NewSignal();

        public int TargetCount => _targets.Count;
        public Task Task { get; set; } = Task.CompletedTask;

        public (OpenTarget[] Targets, Task Changed) Read() => ([.. _targets], _changed.Task);

        public void AddTarget(OpenTarget target)
        {
            _targets.Add(target);
            SignalChanged();
        }

        public void RemoveSocket(WebSocket socket)
        {
            if (_targets.RemoveAll(target => ReferenceEquals(target.Socket, socket)) > 0)
            {
                SignalChanged();
            }
        }

        public void RemoveTargets(IEnumerable<OpenTarget> targets)
        {
            var removed = false;
            foreach (var target in targets)
            {
                removed |= _targets.Remove(target);
            }
            if (removed)
            {
                SignalChanged();
            }
        }

        private void SignalChanged()
        {
            var previous = _changed;
            _changed = NewSignal();
            previous.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record OpenTarget(WebSocket Socket, string OperationId, CancellationToken CancellationToken);
}
