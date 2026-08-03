using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class RelayDeviceSessions(
    Func<WebSocket, string, CancellationToken, Task> handleSession,
    Action sessionFailed,
    Action<Guid> requestClose)
{
    private const int MaximumSessions = 64;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Session> _active = [];
    private readonly Dictionary<Guid, Session> _routable = [];
    private int _drainCount;

    internal int Count
    {
        get
        {
            lock (_gate) return _active.Count;
        }
    }

    internal bool TryStart(
        Guid sessionId,
        RelayVirtualWebSocket socket,
        byte[] relaySourceKey,
        CancellationToken ownerCancellationToken)
    {
        if (!TryCreateRateLimitKey(sessionId, relaySourceKey, out var rateLimitKey)) return false;
        Session session;
        lock (_gate)
        {
            if (_drainCount != 0 || _active.Count >= MaximumSessions || _active.ContainsKey(sessionId)) return false;
            session = new Session(sessionId, socket, rateLimitKey, ownerCancellationToken);
            _active.Add(sessionId, session);
            _routable.Add(sessionId, session);
        }

        _ = RunAsync(session);
        return true;
    }

    internal bool TryDeliver(Guid sessionId, byte[] payload, bool isBinary)
    {
        Session? session;
        lock (_gate) _routable.TryGetValue(sessionId, out session);
        if (session is null) return false;
        if (session.Socket.TryReceive(payload, isBinary)) return true;
        session.CloseFromHost(requestClose);
        return true;
    }

    internal void Disconnect(Guid sessionId)
    {
        Session? session;
        lock (_gate)
        {
            _routable.Remove(sessionId, out session);
        }
        session?.Stop();
    }

    internal async Task CloseAndDrainAsync()
    {
        Session[] sessions;
        lock (_gate)
        {
            _drainCount++;
            sessions = [.. _active.Values];
            _routable.Clear();
        }

        try
        {
            foreach (var session in sessions) session.Stop();
            await Task.WhenAll(sessions.Select(session => session.Completion.Task));
        }
        finally
        {
            lock (_gate) _drainCount--;
        }
    }

    private async Task RunAsync(Session session)
    {
        try
        {
            await handleSession(session.Socket, session.RateLimitKey, session.Cancellation.Token);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            sessionFailed();
        }
        finally
        {
            session.CloseFromHost(requestClose);
            lock (_gate)
            {
                if (_routable.TryGetValue(session.Id, out var routed) && ReferenceEquals(routed, session))
                {
                    _routable.Remove(session.Id);
                }
            }

            session.Dispose();
            session.Completion.TrySetResult(null);
            lock (_gate)
            {
                if (_active.TryGetValue(session.Id, out var active) && ReferenceEquals(active, session))
                {
                    _active.Remove(session.Id);
                }
            }
        }
    }

    internal static bool TryCreateRateLimitKey(Guid sessionId, ReadOnlySpan<byte> relaySourceKey, out string rateLimitKey)
    {
        if (relaySourceKey.Length == 16)
        {
            rateLimitKey = $"relay-source:{Convert.ToHexString(relaySourceKey)}";
            return true;
        }
        if (relaySourceKey.IsEmpty)
        {
            rateLimitKey = $"relay-session:{sessionId:N}";
            return true;
        }
        rateLimitKey = string.Empty;
        return false;
    }

    private sealed class Session(
        Guid id,
        RelayVirtualWebSocket socket,
        string rateLimitKey,
        CancellationToken ownerCancellationToken)
    {
        internal Guid Id { get; } = id;
        internal RelayVirtualWebSocket Socket { get; } = socket;
        internal string RateLimitKey { get; } = rateLimitKey;
        internal CancellationTokenSource Cancellation { get; } =
            CancellationTokenSource.CreateLinkedTokenSource(ownerCancellationToken);
        internal TaskCompletionSource<object?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Stop()
        {
            Socket.CompleteFromRelay();
            Cancel();
        }

        internal void CloseFromHost(Action<Guid> close)
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.Aborted)
            {
                if (Interlocked.Exchange(ref _closeRequested, 1) == 0)
                {
                    close(Id);
                }
                Socket.Abort();
            }
            Cancel();
        }

        private void Cancel()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal void Dispose()
        {
            Socket.Dispose();
            Cancellation.Dispose();
        }

        private int _closeRequested;
    }
}
