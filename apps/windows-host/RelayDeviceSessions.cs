using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class RelayDeviceSessions
{
    private const int MaximumAuthenticatedSessions = 64;
    private const int MaximumPendingSessions = 8;
    private readonly Func<WebSocket, string, Action, CancellationToken, Task> _handleSession;
    private readonly Action _sessionFailed;
    private readonly Action<Guid> _deviceAuthenticated;
    private readonly Action<Guid> _requestClose;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Session> _active = [];
    private readonly Dictionary<Guid, Session> _routable = [];
    private int _drainCount;
    private int _pendingCount;
    private int _authenticatedCount;

    internal RelayDeviceSessions(
        Func<WebSocket, string, Action, CancellationToken, Task> handleSession,
        Action sessionFailed,
        Action<Guid> deviceAuthenticated,
        Action<Guid> requestClose)
    {
        _handleSession = handleSession;
        _sessionFailed = sessionFailed;
        _deviceAuthenticated = deviceAuthenticated;
        _requestClose = requestClose;
    }

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
            if (_drainCount != 0 || _authenticatedCount >= MaximumAuthenticatedSessions ||
                _active.Count >= MaximumAuthenticatedSessions + MaximumPendingSessions ||
                _pendingCount >= MaximumPendingSessions || _active.ContainsKey(sessionId)) return false;
            session = new Session(sessionId, socket, rateLimitKey, ownerCancellationToken);
            _active.Add(sessionId, session);
            _routable.Add(sessionId, session);
            _pendingCount++;
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
        session.CloseFromHost(_requestClose);
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
            await _handleSession(session.Socket, session.RateLimitKey, () => MarkAuthenticated(session.Id), session.Cancellation.Token);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _sessionFailed();
        }
        finally
        {
            session.CloseFromHost(_requestClose);
            lock (_gate)
            {
                if (_routable.TryGetValue(session.Id, out var routed) && ReferenceEquals(routed, session))
                {
                    _routable.Remove(session.Id);
                }
            }

            lock (_gate)
            {
                if (_active.TryGetValue(session.Id, out var active) && ReferenceEquals(active, session))
                {
                    if (session.Authenticated) _authenticatedCount--; else _pendingCount--;
                    _active.Remove(session.Id);
                }
            }
            session.Dispose();
            session.Completion.TrySetResult(null);
        }
    }

    private void MarkAuthenticated(Guid sessionId)
    {
        var marked = false;
        lock (_gate)
        {
            if (_active.TryGetValue(sessionId, out var session) && !session.Authenticated)
            {
                session.Authenticated = true;
                _pendingCount--;
                _authenticatedCount++;
                marked = true;
            }
        }
        if (marked) _deviceAuthenticated(sessionId);
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
        internal bool Authenticated { get; set; }

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
