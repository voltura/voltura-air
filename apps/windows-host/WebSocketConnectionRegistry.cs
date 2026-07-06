using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class WebSocketConnectionRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<WebSocket>> _activeSockets = new(StringComparer.Ordinal);
    private readonly Dictionary<WebSocket, SemaphoreSlim> _sendGates = new();

    public void Register(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            if (!_activeSockets.TryGetValue(clientId, out var sockets))
            {
                sockets = new List<WebSocket>();
                _activeSockets[clientId] = sockets;
            }

            sockets.Add(socket);
            _sendGates.TryAdd(socket, new SemaphoreSlim(1, 1));
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
            return _activeSockets
                .SelectMany(pair => pair.Value.Select(socket => (pair.Key, socket)))
                .ToArray();
        }
    }

    public SemaphoreSlim? GetSendGate(WebSocket socket)
    {
        lock (_gate)
        {
            _sendGates.TryGetValue(socket, out var sendGate);
            return sendGate;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _activeSockets.Clear();
            ClearSendGates();
        }
    }

    private void ClearSendGates()
    {
        foreach (var sendGate in _sendGates.Values)
        {
            sendGate.Dispose();
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
            sendGate.Dispose();
        }
    }
}
