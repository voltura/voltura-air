using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class WebSocketConnectionRegistry : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<WebSocket>> _activeSockets = new(StringComparer.Ordinal);
    private readonly Dictionary<WebSocket, SendGateState> _sendGates = [];
    private bool _disposed;

    internal int ActiveSocketCount
    {
        get
        {
            lock (_gate)
            {
                return _activeSockets.Values.Sum(sockets => sockets.Count);
            }
        }
    }

    internal int SendGateCount
    {
        get
        {
            lock (_gate)
            {
                return _sendGates.Count;
            }
        }
    }

    public void Register(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sendGates.ContainsKey(socket))
            {
                throw new InvalidOperationException("The WebSocket is already registered.");
            }

            if (!_activeSockets.TryGetValue(clientId, out var sockets))
            {
                sockets = [];
                _activeSockets[clientId] = sockets;
            }

            sockets.Add(socket);
            _sendGates.Add(socket, new SendGateState());
        }
    }

    public void Unregister(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            if (!_activeSockets.TryGetValue(clientId, out var sockets))
            {
                return;
            }

            sockets.Remove(socket);
            if (sockets.Count == 0)
            {
                _activeSockets.Remove(clientId);
            }

            RemoveSendGate(socket);
        }
    }

    public WebSocket[] TakeAll()
    {
        lock (_gate)
        {
            var sockets = _activeSockets.Values.SelectMany(items => items).ToArray();
            _activeSockets.Clear();
            ClearSendGates();
            return sockets;
        }
    }

    public WebSocket[] TakeRevoked(string? clientId)
    {
        lock (_gate)
        {
            if (clientId is null)
            {
                var revokedSockets = _activeSockets.Values.SelectMany(items => items).ToArray();
                _activeSockets.Clear();
                ClearSendGates(revokedSockets);
                return revokedSockets;
            }

            if (!_activeSockets.Remove(clientId, out var deviceSockets))
            {
                return [];
            }

            var sockets = deviceSockets.ToArray();
            ClearSendGates(sockets);
            return sockets;
        }
    }

    public (string ClientId, WebSocket Socket)[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _activeSockets.SelectMany(pair => pair.Value.Select(socket => (pair.Key, socket)))];
        }
    }

    public async Task<bool> TrySendAsync(WebSocket socket, Func<Task> send, CancellationToken cancellationToken)
    {
        SendGateState? sendGate;
        lock (_gate)
        {
            if (!_sendGates.TryGetValue(socket, out sendGate))
            {
                sendGate = null;
            }
            else
            {
                sendGate.Users += 1;
            }
        }

        if (sendGate is null)
        {
            return false;
        }

        var entered = false;
        try
        {
            await sendGate.Semaphore.WaitAsync(cancellationToken);
            entered = true;
            await send();
        }
        finally
        {
            if (entered)
            {
                sendGate.Semaphore.Release();
            }

            ReleaseSendGate(sendGate);
        }

        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeSockets.Clear();
            ClearSendGates();
        }
    }

    private void ClearSendGates()
    {
        foreach (var sendGate in _sendGates.Values)
        {
            RetireSendGate(sendGate);
        }

        _sendGates.Clear();
    }

    private void ClearSendGates(IEnumerable<WebSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            RemoveSendGate(socket);
        }
    }

    private void RemoveSendGate(WebSocket socket)
    {
        if (_sendGates.Remove(socket, out var sendGate))
        {
            RetireSendGate(sendGate);
        }
    }

    private void ReleaseSendGate(SendGateState sendGate)
    {
        lock (_gate)
        {
            sendGate.Users -= 1;
            if (sendGate.Retired && sendGate.Users == 0)
            {
                sendGate.Semaphore.Dispose();
            }
        }
    }

    private static void RetireSendGate(SendGateState sendGate)
    {
        sendGate.Retired = true;
        if (sendGate.Users == 0)
        {
            sendGate.Semaphore.Dispose();
        }
    }

    private sealed class SendGateState
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }

        public bool Retired { get; set; }
    }
}
